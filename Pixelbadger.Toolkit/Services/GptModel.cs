namespace Pixelbadger.Toolkit.Services;

public record GptConfig(int VocabSize, int BlockSize, int NEmbd, int NHead, int NLayer)
{
    public int HeadDim => NEmbd / NHead;
}

/// <summary>
/// A tiny char-level GPT (decoder-only transformer) built entirely from the hand-rolled
/// <see cref="Tensor"/> autograd engine. Architecture follows nanoGPT: token + positional
/// embeddings, pre-LayerNorm transformer blocks (causal multi-head self-attention + MLP),
/// a final LayerNorm, and a weight-tied linear head.
/// </summary>
public sealed class GptModel
{
    private sealed class Block
    {
        public required Tensor Ln1Gamma, Ln1Beta;
        public required Tensor Wq, Bq, Wk, Bk, Wv, Bv, Wo, Bo;
        public required Tensor Ln2Gamma, Ln2Beta;
        public required Tensor Wfc, Bfc, Wproj, Bproj;
    }

    public GptConfig Config { get; }

    private readonly Tensor _wte;   // token embedding (V, C), also the tied output head
    private readonly Tensor _wpe;   // positional embedding (block, C)
    private readonly Block[] _blocks;
    private readonly Tensor _lnfGamma, _lnfBeta;
    private readonly List<Tensor> _parameters = new();

    public GptModel(GptConfig config)
    {
        Config = config;
        int c = config.NEmbd;

        _wte = Param(config.VocabSize, c);
        _wpe = Param(config.BlockSize, c);

        _blocks = new Block[config.NLayer];
        for (int i = 0; i < config.NLayer; i++)
        {
            _blocks[i] = new Block
            {
                Ln1Gamma = Param(1, c), Ln1Beta = Param(1, c),
                Wq = Param(c, c), Bq = Param(1, c),
                Wk = Param(c, c), Bk = Param(1, c),
                Wv = Param(c, c), Bv = Param(1, c),
                Wo = Param(c, c), Bo = Param(1, c),
                Ln2Gamma = Param(1, c), Ln2Beta = Param(1, c),
                Wfc = Param(c, 4 * c), Bfc = Param(1, 4 * c),
                Wproj = Param(4 * c, c), Bproj = Param(1, c)
            };
        }

        _lnfGamma = Param(1, c);
        _lnfBeta = Param(1, c);
    }

    private Tensor Param(int rows, int cols)
    {
        var t = new Tensor(rows, cols, requiresGrad: true);
        _parameters.Add(t);
        return t;
    }

    /// <summary>Parameters in a fixed construction order (used for checkpointing and the optimizer).</summary>
    public IReadOnlyList<Tensor> Parameters() => _parameters;

    public int ParameterCount() => _parameters.Sum(p => p.Length);

    public void ZeroGrad()
    {
        foreach (var p in _parameters)
            p.ZeroGrad();
    }

    /// <summary>Copies persisted weights into the parameter tensors (must match construction order and shapes).</summary>
    public void LoadWeights(IReadOnlyList<float[]> weights)
    {
        if (weights.Count != _parameters.Count)
            throw new ArgumentException(
                $"Checkpoint has {weights.Count} weight tensors but the model expects {_parameters.Count}.");

        for (int i = 0; i < _parameters.Count; i++)
        {
            if (weights[i].Length != _parameters[i].Length)
                throw new ArgumentException(
                    $"Weight tensor {i} length {weights[i].Length} does not match expected {_parameters[i].Length}.");
            Array.Copy(weights[i], _parameters[i].Data, weights[i].Length);
        }
    }

    /// <summary>Initializes weights from a seeded RNG: N(0, 0.02) for matrices, 0 for biases, LayerNorm gamma=1.</summary>
    public void InitWeights(int seed)
    {
        var rng = new Random(seed);

        RandomNormal(rng, _wte, 0.02f);
        RandomNormal(rng, _wpe, 0.02f);

        foreach (var b in _blocks)
        {
            Ones(b.Ln1Gamma); Zeros(b.Ln1Beta);
            RandomNormal(rng, b.Wq, 0.02f); Zeros(b.Bq);
            RandomNormal(rng, b.Wk, 0.02f); Zeros(b.Bk);
            RandomNormal(rng, b.Wv, 0.02f); Zeros(b.Bv);
            RandomNormal(rng, b.Wo, 0.02f); Zeros(b.Bo);
            Ones(b.Ln2Gamma); Zeros(b.Ln2Beta);
            RandomNormal(rng, b.Wfc, 0.02f); Zeros(b.Bfc);
            RandomNormal(rng, b.Wproj, 0.02f); Zeros(b.Bproj);
        }

        Ones(_lnfGamma);
        Zeros(_lnfBeta);
    }

    private static void RandomNormal(Random rng, Tensor t, float std)
    {
        for (int i = 0; i < t.Length; i++)
        {
            // Box-Muller transform.
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            t.Data[i] = (float)(z * std);
        }
    }

    private static void Ones(Tensor t) => Array.Fill(t.Data, 1f);
    private static void Zeros(Tensor t) => Array.Clear(t.Data, 0, t.Length);

    /// <summary>
    /// Forward pass over a batch of equal-length token sequences. Returns the logits (B*T, vocab)
    /// and, when targets are supplied, the mean cross-entropy loss as a 1x1 tensor.
    /// </summary>
    public (Tensor Logits, Tensor? Loss) Forward(int[][] batch, int[][]? targets = null)
    {
        int b = batch.Length;
        int t = batch[0].Length;
        if (t > Config.BlockSize)
            throw new ArgumentException($"Sequence length {t} exceeds block size {Config.BlockSize}.");

        int r = b * t;
        var tokenIds = new int[r];
        var posIds = new int[r];
        for (int bi = 0; bi < b; bi++)
            for (int ti = 0; ti < t; ti++)
            {
                tokenIds[bi * t + ti] = batch[bi][ti];
                posIds[bi * t + ti] = ti;
            }

        var x = Tensor.Add(Tensor.Gather(_wte, tokenIds), Tensor.Gather(_wpe, posIds));

        foreach (var block in _blocks)
            x = ForwardBlock(x, block, b, t);

        x = Tensor.LayerNorm(x, _lnfGamma, _lnfBeta);

        // Weight-tied head: logits = x · Wteᵀ.
        var logits = Tensor.MatMul(x, Tensor.Transpose(_wte));

        if (targets is null)
            return (logits, null);

        var targetFlat = new int[r];
        for (int bi = 0; bi < b; bi++)
            for (int ti = 0; ti < t; ti++)
                targetFlat[bi * t + ti] = targets[bi][ti];

        var loss = Tensor.CrossEntropy(logits, targetFlat);
        return (logits, loss);
    }

    private Tensor ForwardBlock(Tensor x, Block block, int b, int t)
    {
        var a = Tensor.LayerNorm(x, block.Ln1Gamma, block.Ln1Beta);
        x = Tensor.Add(x, Attention(a, block, b, t));

        var m = Tensor.LayerNorm(x, block.Ln2Gamma, block.Ln2Beta);
        x = Tensor.Add(x, Mlp(m, block));
        return x;
    }

    private Tensor Attention(Tensor a, Block block, int b, int t)
    {
        int hd = Config.HeadDim;
        int h = Config.NHead;
        float scale = 1f / MathF.Sqrt(hd);

        var q = Tensor.AddBias(Tensor.MatMul(a, block.Wq), block.Bq);
        var k = Tensor.AddBias(Tensor.MatMul(a, block.Wk), block.Bk);
        var v = Tensor.AddBias(Tensor.MatMul(a, block.Wv), block.Bv);

        var seqOutputs = new List<Tensor>(b);
        for (int bi = 0; bi < b; bi++)
        {
            var qSeq = Tensor.SliceRows(q, bi * t, t);
            var kSeq = Tensor.SliceRows(k, bi * t, t);
            var vSeq = Tensor.SliceRows(v, bi * t, t);

            var headOutputs = new List<Tensor>(h);
            for (int hi = 0; hi < h; hi++)
            {
                var qh = Tensor.SliceCols(qSeq, hi * hd, hd);
                var kh = Tensor.SliceCols(kSeq, hi * hd, hd);
                var vh = Tensor.SliceCols(vSeq, hi * hd, hd);

                var scores = Tensor.Scale(Tensor.MatMul(qh, Tensor.Transpose(kh)), scale);
                scores = Tensor.CausalMask(scores);
                var att = Tensor.SoftmaxRows(scores);
                headOutputs.Add(Tensor.MatMul(att, vh));
            }

            seqOutputs.Add(Tensor.ConcatCols(headOutputs));
        }

        var attnConcat = Tensor.ConcatRows(seqOutputs);
        return Tensor.AddBias(Tensor.MatMul(attnConcat, block.Wo), block.Bo);
    }

    private static Tensor Mlp(Tensor x, Block block)
    {
        var h = Tensor.AddBias(Tensor.MatMul(x, block.Wfc), block.Bfc);
        h = Tensor.Gelu(h);
        return Tensor.AddBias(Tensor.MatMul(h, block.Wproj), block.Bproj);
    }
}
