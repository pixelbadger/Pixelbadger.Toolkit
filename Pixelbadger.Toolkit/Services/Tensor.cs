using System.Numerics.Tensors;

namespace Pixelbadger.Toolkit.Services;

/// <summary>
/// A minimal reverse-mode automatic differentiation engine over 2D float matrices,
/// hand-rolled from scratch (no TorchSharp / native ML libraries) in the spirit of
/// Karpathy's micrograd/nanoGPT. Hot kernels are accelerated with
/// <see cref="System.Numerics.Tensors.TensorPrimitives"/> (SIMD) and
/// <see cref="System.Threading.Tasks.Parallel"/> across independent work units
/// (matrix rows, attention heads, normalization rows). Parallelism is only applied where
/// each unit writes disjoint state, and reductions inside a unit are kept sequential, so
/// results are independent of thread count and therefore reproducible.
/// </summary>
public sealed class Tensor
{
    public int Rows { get; }
    public int Cols { get; }
    public float[] Data { get; }
    public float[] Grad { get; }
    public bool RequiresGrad { get; set; }

    internal readonly List<Tensor> Parents = new();
    internal Action? BackwardFn;

    public int Length => Data.Length;

    public Tensor(int rows, int cols, bool requiresGrad = false)
    {
        Rows = rows;
        Cols = cols;
        Data = new float[rows * cols];
        Grad = new float[rows * cols];
        RequiresGrad = requiresGrad;
    }

    public static Tensor FromData(int rows, int cols, float[] data, bool requiresGrad = false)
    {
        if (data.Length != rows * cols)
            throw new ArgumentException($"Data length {data.Length} does not match shape {rows}x{cols}.");
        var t = new Tensor(rows, cols, requiresGrad);
        Array.Copy(data, t.Data, data.Length);
        return t;
    }

    public void ZeroGrad() => Array.Clear(Grad, 0, Grad.Length);

    /// <summary>
    /// Runs reverse-mode autodiff from this tensor (treated as the loss root). Performs a
    /// topological sort of the graph and accumulates gradients into every node's <see cref="Grad"/>.
    /// </summary>
    public void Backward()
    {
        var topo = new List<Tensor>();
        var visited = new HashSet<Tensor>();
        BuildTopo(this, visited, topo);

        // Seed the root gradient with ones (loss is typically a 1x1 scalar).
        Array.Fill(Grad, 1f);

        for (int i = topo.Count - 1; i >= 0; i--)
            topo[i].BackwardFn?.Invoke();
    }

    private static void BuildTopo(Tensor node, HashSet<Tensor> visited, List<Tensor> topo)
    {
        if (!visited.Add(node))
            return;
        foreach (var parent in node.Parents)
            BuildTopo(parent, visited, topo);
        topo.Add(node);
    }

    private static void AccumulateInPlace(float[] dest, ReadOnlySpan<float> src)
        => TensorPrimitives.Add(dest, src, dest);

    /// <summary>
    /// Total scalar operations below which a loop runs sequentially — Parallel.For overhead
    /// would otherwise exceed the work (e.g. tiny matrices during single-token inference).
    /// </summary>
    private const long ParallelWorkThreshold = 16 * 1024;

    /// <summary>
    /// Runs <paramref name="body"/> for each index in [0, count), in parallel when the total
    /// work justifies it. Bodies must write to disjoint state per index so results are
    /// identical regardless of thread count.
    /// </summary>
    private static void ParallelFor(int count, long workPerUnit, Action<int> body)
    {
        if (count * workPerUnit < ParallelWorkThreshold)
        {
            for (int i = 0; i < count; i++)
                body(i);
        }
        else
        {
            Parallel.For(0, count, body);
        }
    }

    // ---- Helpers -------------------------------------------------------------------

    internal static float[] Transpose(float[] data, int rows, int cols)
    {
        var result = new float[rows * cols];
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                result[j * rows + i] = data[i * cols + j];
        return result;
    }

    // ---- Core ops ------------------------------------------------------------------

    /// <summary>Matrix multiply: A(m,k) · B(k,n) = (m,n). SIMD dot products, parallel over rows.</summary>
    public static Tensor MatMul(Tensor a, Tensor b)
    {
        if (a.Cols != b.Rows)
            throw new ArgumentException($"MatMul shape mismatch: {a.Rows}x{a.Cols} · {b.Rows}x{b.Cols}.");

        int m = a.Rows, k = a.Cols, n = b.Cols;
        var outp = new Tensor(m, n);

        // Pre-transpose B so each column is a contiguous span for TensorPrimitives.Dot.
        var bt = Transpose(b.Data, k, n); // (n, k)

        ParallelFor(m, (long)n * k, i =>
        {
            var aRow = new ReadOnlySpan<float>(a.Data, i * k, k);
            for (int j = 0; j < n; j++)
            {
                var btRow = new ReadOnlySpan<float>(bt, j * k, k);
                outp.Data[i * n + j] = TensorPrimitives.Dot(aRow, btRow);
            }
        });

        outp.Parents.Add(a);
        outp.Parents.Add(b);
        outp.BackwardFn = () =>
        {
            // dA[i,l] = sum_j dY[i,j] * B[l,j] = Dot(dY_row_i, B_row_l)
            ParallelFor(m, (long)k * n, i =>
            {
                var dyRow = new ReadOnlySpan<float>(outp.Grad, i * n, n);
                for (int l = 0; l < k; l++)
                {
                    var bRow = new ReadOnlySpan<float>(b.Data, l * n, n);
                    a.Grad[i * k + l] += TensorPrimitives.Dot(dyRow, bRow);
                }
            });

            // dB[l,j] = sum_i A[i,l] * dY[i,j] = Dot(At_row_l, dYt_row_j)
            var at = Transpose(a.Data, m, k);       // (k, m)
            var dyt = Transpose(outp.Grad, m, n);   // (n, m)
            ParallelFor(k, (long)n * m, l =>
            {
                var atRow = new ReadOnlySpan<float>(at, l * m, m);
                for (int j = 0; j < n; j++)
                {
                    var dytRow = new ReadOnlySpan<float>(dyt, j * m, m);
                    b.Grad[l * n + j] += TensorPrimitives.Dot(atRow, dytRow);
                }
            });
        };
        return outp;
    }

    /// <summary>Transpose with autograd: X(r,c) -> (c,r).</summary>
    public static Tensor Transpose(Tensor x)
    {
        var outp = new Tensor(x.Cols, x.Rows);
        var t = Transpose(x.Data, x.Rows, x.Cols);
        Array.Copy(t, outp.Data, t.Length);

        outp.Parents.Add(x);
        outp.BackwardFn = () =>
        {
            for (int i = 0; i < x.Rows; i++)
                for (int j = 0; j < x.Cols; j++)
                    x.Grad[i * x.Cols + j] += outp.Grad[j * x.Rows + i];
        };
        return outp;
    }

    /// <summary>Adds a per-column bias (broadcast over rows): X(r,c) + b(1,c).</summary>
    public static Tensor AddBias(Tensor x, Tensor bias)
    {
        if (bias.Length != x.Cols)
            throw new ArgumentException("Bias length must equal number of columns.");

        var outp = new Tensor(x.Rows, x.Cols);
        ParallelFor(x.Rows, x.Cols, i =>
        {
            var xRow = new ReadOnlySpan<float>(x.Data, i * x.Cols, x.Cols);
            var oRow = new Span<float>(outp.Data, i * x.Cols, x.Cols);
            TensorPrimitives.Add(xRow, bias.Data, oRow);
        });

        outp.Parents.Add(x);
        outp.Parents.Add(bias);
        outp.BackwardFn = () =>
        {
            AccumulateInPlace(x.Grad, outp.Grad);
            // Sequential over rows (bias.Grad is shared), SIMD within each row.
            for (int i = 0; i < x.Rows; i++)
                AccumulateInPlace(bias.Grad, new ReadOnlySpan<float>(outp.Grad, i * x.Cols, x.Cols));
        };
        return outp;
    }

    /// <summary>Elementwise add of two equally-shaped tensors (used for residual connections).</summary>
    public static Tensor Add(Tensor a, Tensor b)
    {
        if (a.Rows != b.Rows || a.Cols != b.Cols)
            throw new ArgumentException("Add shape mismatch.");

        var outp = new Tensor(a.Rows, a.Cols);
        TensorPrimitives.Add(a.Data, b.Data, outp.Data);

        outp.Parents.Add(a);
        outp.Parents.Add(b);
        outp.BackwardFn = () =>
        {
            AccumulateInPlace(a.Grad, outp.Grad);
            AccumulateInPlace(b.Grad, outp.Grad);
        };
        return outp;
    }

    /// <summary>Multiply every element by a scalar constant.</summary>
    public static Tensor Scale(Tensor x, float s)
    {
        var outp = new Tensor(x.Rows, x.Cols);
        TensorPrimitives.Multiply(x.Data, s, outp.Data);

        outp.Parents.Add(x);
        outp.BackwardFn = () => TensorPrimitives.MultiplyAdd(outp.Grad, s, x.Grad, x.Grad);
        return outp;
    }

    /// <summary>Row-wise softmax with max-subtraction for numerical stability.</summary>
    public static Tensor SoftmaxRows(Tensor x)
    {
        int r = x.Rows, c = x.Cols;
        var outp = new Tensor(r, c);

        ParallelFor(r, c, i =>
        {
            var row = new ReadOnlySpan<float>(x.Data, i * c, c);
            var oRow = new Span<float>(outp.Data, i * c, c);
            float max = TensorPrimitives.Max(row);
            TensorPrimitives.Subtract(row, max, oRow);
            TensorPrimitives.Exp(oRow, oRow);
            float sum = TensorPrimitives.Sum(oRow);
            TensorPrimitives.Divide(oRow, sum, oRow);
        });

        outp.Parents.Add(x);
        outp.BackwardFn = () =>
        {
            ParallelFor(r, c, i =>
            {
                var y = new ReadOnlySpan<float>(outp.Data, i * c, c);
                var dy = new ReadOnlySpan<float>(outp.Grad, i * c, c);
                float dot = TensorPrimitives.Dot(dy, y);
                for (int j = 0; j < c; j++)
                    x.Grad[i * c + j] += y[j] * (dy[j] - dot);
            });
        };
        return outp;
    }

    /// <summary>Row-wise layer normalization with learnable gamma/beta.</summary>
    public static Tensor LayerNorm(Tensor x, Tensor gamma, Tensor beta, float eps = 1e-5f)
    {
        int r = x.Rows, c = x.Cols;
        var outp = new Tensor(r, c);
        var xhat = new float[r * c];
        var invStd = new float[r];

        ParallelFor(r, c, i =>
        {
            var row = new ReadOnlySpan<float>(x.Data, i * c, c);
            float mean = TensorPrimitives.Sum(row) / c;
            float var = 0f;
            for (int j = 0; j < c; j++)
            {
                float d = row[j] - mean;
                var += d * d;
            }
            var /= c;
            float istd = 1f / MathF.Sqrt(var + eps);
            invStd[i] = istd;
            for (int j = 0; j < c; j++)
            {
                float xh = (row[j] - mean) * istd;
                xhat[i * c + j] = xh;
                outp.Data[i * c + j] = gamma.Data[j] * xh + beta.Data[j];
            }
        });

        outp.Parents.Add(x);
        outp.Parents.Add(gamma);
        outp.Parents.Add(beta);
        outp.BackwardFn = () =>
        {
            // x.Grad: rows are independent.
            ParallelFor(r, c, i =>
            {
                float sumDxhat = 0f, sumDxhatXhat = 0f;
                for (int j = 0; j < c; j++)
                {
                    float dxhat = outp.Grad[i * c + j] * gamma.Data[j];
                    sumDxhat += dxhat;
                    sumDxhatXhat += dxhat * xhat[i * c + j];
                }
                float istd = invStd[i];
                for (int j = 0; j < c; j++)
                {
                    float dxhat = outp.Grad[i * c + j] * gamma.Data[j];
                    x.Grad[i * c + j] += (istd / c) * (c * dxhat - sumDxhat - xhat[i * c + j] * sumDxhatXhat);
                }
            });

            // gamma/beta grads: columns are independent; each column sums rows sequentially.
            ParallelFor(c, r, j =>
            {
                float gSum = 0f, bSum = 0f;
                for (int i = 0; i < r; i++)
                {
                    float dy = outp.Grad[i * c + j];
                    gSum += dy * xhat[i * c + j];
                    bSum += dy;
                }
                gamma.Grad[j] += gSum;
                beta.Grad[j] += bSum;
            });
        };
        return outp;
    }

    private const float GeluC = 0.7978845608028654f; // sqrt(2/pi)
    private const float GeluA = 0.044715f;

    /// <summary>GELU activation (tanh approximation), elementwise.</summary>
    public static Tensor Gelu(Tensor x)
    {
        var outp = new Tensor(x.Rows, x.Cols);
        ParallelFor(x.Rows, x.Cols, i =>
        {
            for (int j = i * x.Cols; j < (i + 1) * x.Cols; j++)
            {
                float v = x.Data[j];
                float inner = GeluC * (v + GeluA * v * v * v);
                float t = MathF.Tanh(inner);
                outp.Data[j] = 0.5f * v * (1f + t);
            }
        });

        outp.Parents.Add(x);
        outp.BackwardFn = () =>
        {
            ParallelFor(x.Rows, x.Cols, i =>
            {
                for (int j = i * x.Cols; j < (i + 1) * x.Cols; j++)
                {
                    float v = x.Data[j];
                    float inner = GeluC * (v + GeluA * v * v * v);
                    float t = MathF.Tanh(inner);
                    float dInner = GeluC * (1f + 3f * GeluA * v * v);
                    float dgelu = 0.5f * (1f + t) + 0.5f * v * (1f - t * t) * dInner;
                    x.Grad[j] += outp.Grad[j] * dgelu;
                }
            });
        };
        return outp;
    }

    /// <summary>Adds a causal mask to a (T,T) score matrix: positions j &gt; i become -1e9.</summary>
    public static Tensor CausalMask(Tensor scores)
    {
        if (scores.Rows != scores.Cols)
            throw new ArgumentException("Causal mask requires a square (T,T) score matrix.");

        int t = scores.Rows;
        var outp = new Tensor(t, t);
        for (int i = 0; i < t; i++)
            for (int j = 0; j < t; j++)
                outp.Data[i * t + j] = j <= i ? scores.Data[i * t + j] : -1e9f;

        outp.Parents.Add(scores);
        outp.BackwardFn = () =>
        {
            for (int i = 0; i < t; i++)
                for (int j = 0; j <= i; j++)
                    scores.Grad[i * t + j] += outp.Grad[i * t + j];
        };
        return outp;
    }

    /// <summary>Extract a contiguous block of rows.</summary>
    public static Tensor SliceRows(Tensor x, int startRow, int count)
    {
        var outp = new Tensor(count, x.Cols);
        Array.Copy(x.Data, startRow * x.Cols, outp.Data, 0, count * x.Cols);

        outp.Parents.Add(x);
        outp.BackwardFn = () =>
        {
            for (int i = 0; i < count * x.Cols; i++)
                x.Grad[startRow * x.Cols + i] += outp.Grad[i];
        };
        return outp;
    }

    /// <summary>Extract a contiguous block of columns (e.g. a single attention head).</summary>
    public static Tensor SliceCols(Tensor x, int startCol, int count)
    {
        var outp = new Tensor(x.Rows, count);
        for (int i = 0; i < x.Rows; i++)
            for (int j = 0; j < count; j++)
                outp.Data[i * count + j] = x.Data[i * x.Cols + startCol + j];

        outp.Parents.Add(x);
        outp.BackwardFn = () =>
        {
            for (int i = 0; i < x.Rows; i++)
                for (int j = 0; j < count; j++)
                    x.Grad[i * x.Cols + startCol + j] += outp.Grad[i * count + j];
        };
        return outp;
    }

    /// <summary>Concatenate tensors along columns (all must share row count).</summary>
    public static Tensor ConcatCols(IReadOnlyList<Tensor> parts)
    {
        int rows = parts[0].Rows;
        int totalCols = parts.Sum(p => p.Cols);
        var outp = new Tensor(rows, totalCols);

        int colOffset = 0;
        foreach (var p in parts)
        {
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < p.Cols; j++)
                    outp.Data[i * totalCols + colOffset + j] = p.Data[i * p.Cols + j];
            colOffset += p.Cols;
        }

        foreach (var p in parts)
            outp.Parents.Add(p);

        outp.BackwardFn = () =>
        {
            int off = 0;
            foreach (var p in parts)
            {
                for (int i = 0; i < rows; i++)
                    for (int j = 0; j < p.Cols; j++)
                        p.Grad[i * p.Cols + j] += outp.Grad[i * totalCols + off + j];
                off += p.Cols;
            }
        };
        return outp;
    }

    /// <summary>Concatenate tensors along rows (all must share column count).</summary>
    public static Tensor ConcatRows(IReadOnlyList<Tensor> parts)
    {
        int cols = parts[0].Cols;
        int totalRows = parts.Sum(p => p.Rows);
        var outp = new Tensor(totalRows, cols);

        int rowOffset = 0;
        foreach (var p in parts)
        {
            Array.Copy(p.Data, 0, outp.Data, rowOffset * cols, p.Rows * cols);
            rowOffset += p.Rows;
        }

        foreach (var p in parts)
            outp.Parents.Add(p);

        outp.BackwardFn = () =>
        {
            int off = 0;
            foreach (var p in parts)
            {
                for (int i = 0; i < p.Rows * cols; i++)
                    p.Grad[i] += outp.Grad[off * cols + i];
                off += p.Rows;
            }
        };
        return outp;
    }

    /// <summary>Gather rows from a (V,C) table by integer ids, producing (ids.Length, C).</summary>
    public static Tensor Gather(Tensor table, int[] ids)
    {
        int c = table.Cols;
        var outp = new Tensor(ids.Length, c);
        for (int i = 0; i < ids.Length; i++)
            Array.Copy(table.Data, ids[i] * c, outp.Data, i * c, c);

        outp.Parents.Add(table);
        outp.BackwardFn = () =>
        {
            for (int i = 0; i < ids.Length; i++)
                for (int j = 0; j < c; j++)
                    table.Grad[ids[i] * c + j] += outp.Grad[i * c + j];
        };
        return outp;
    }

    /// <summary>
    /// Fused softmax cross-entropy over logits (N,V) against integer targets, returning the
    /// mean negative log-likelihood as a 1x1 scalar tensor.
    /// </summary>
    public static Tensor CrossEntropy(Tensor logits, int[] targets)
    {
        int n = logits.Rows, v = logits.Cols;
        var outp = new Tensor(1, 1);
        var probs = new float[n * v];

        // Per-row losses land in a scratch array so rows can run in parallel; the final
        // reduction stays sequential for thread-count-independent results.
        var rowLoss = new float[n];
        ParallelFor(n, v, i =>
        {
            var row = new ReadOnlySpan<float>(logits.Data, i * v, v);
            var pRow = new Span<float>(probs, i * v, v);
            float max = TensorPrimitives.Max(row);
            TensorPrimitives.Subtract(row, max, pRow);
            TensorPrimitives.Exp(pRow, pRow);
            float sum = TensorPrimitives.Sum(pRow);
            TensorPrimitives.Divide(pRow, sum, pRow);
            rowLoss[i] = -MathF.Log(Math.Max(pRow[targets[i]], 1e-12f));
        });

        float lossSum = 0f;
        for (int i = 0; i < n; i++)
            lossSum += rowLoss[i];
        outp.Data[0] = lossSum / n;

        outp.Parents.Add(logits);
        outp.BackwardFn = () =>
        {
            float g = outp.Grad[0] / n;
            ParallelFor(n, v, i =>
            {
                for (int j = 0; j < v; j++)
                    logits.Grad[i * v + j] += g * probs[i * v + j];
                logits.Grad[i * v + targets[i]] -= g;
            });
        };
        return outp;
    }

    /// <summary>
    /// Fused causal multi-head self-attention over row-major (B*T, C) query/key/value
    /// projections: each (sequence, head) unit computes softmax(mask(q·kᵀ/√hd))·v into its own
    /// (row-block, column-block) region of the output. Units are independent — forward and
    /// backward parallelize across them while the arithmetic inside a unit stays sequential,
    /// so results are thread-count independent.
    /// </summary>
    public static Tensor CausalSelfAttention(Tensor q, Tensor k, Tensor v, int batch, int seqLen, int nHead)
    {
        int c = q.Cols;
        if (k.Rows != q.Rows || v.Rows != q.Rows || k.Cols != c || v.Cols != c)
            throw new ArgumentException("Attention inputs must share the same (B*T, C) shape.");
        if (q.Rows != batch * seqLen)
            throw new ArgumentException($"Expected {batch * seqLen} rows (batch {batch} x seqLen {seqLen}), got {q.Rows}.");
        if (nHead <= 0 || c % nHead != 0)
            throw new ArgumentException($"Embedding dim {c} must be divisible by head count {nHead}.");

        int hd = c / nHead;
        float scale = 1f / MathF.Sqrt(hd);
        int units = batch * nHead;
        long unitWork = (long)seqLen * seqLen * hd;
        var outp = new Tensor(q.Rows, c);

        // Softmax probabilities per unit, kept alive for the backward pass. Masked positions stay 0.
        var att = new float[units * seqLen * seqLen];

        ParallelFor(units, unitWork, u =>
        {
            int rowBase = (u / nHead) * seqLen;
            int colBase = (u % nHead) * hd;
            int attBase = u * seqLen * seqLen;

            for (int i = 0; i < seqLen; i++)
            {
                var qi = new ReadOnlySpan<float>(q.Data, (rowBase + i) * c + colBase, hd);
                var aRow = new Span<float>(att, attBase + i * seqLen, seqLen);

                float max = float.NegativeInfinity;
                for (int j = 0; j <= i; j++)
                {
                    var kj = new ReadOnlySpan<float>(k.Data, (rowBase + j) * c + colBase, hd);
                    float s = TensorPrimitives.Dot(qi, kj) * scale;
                    aRow[j] = s;
                    if (s > max) max = s;
                }

                float sum = 0f;
                for (int j = 0; j <= i; j++)
                {
                    float e = MathF.Exp(aRow[j] - max);
                    aRow[j] = e;
                    sum += e;
                }
                float inv = 1f / sum;
                for (int j = 0; j <= i; j++)
                    aRow[j] *= inv;

                var oi = new Span<float>(outp.Data, (rowBase + i) * c + colBase, hd);
                for (int j = 0; j <= i; j++)
                {
                    var vj = new ReadOnlySpan<float>(v.Data, (rowBase + j) * c + colBase, hd);
                    TensorPrimitives.MultiplyAdd(vj, aRow[j], oi, oi);
                }
            }
        });

        outp.Parents.Add(q);
        outp.Parents.Add(k);
        outp.Parents.Add(v);
        outp.BackwardFn = () =>
        {
            ParallelFor(units, unitWork * 3, u =>
            {
                int rowBase = (u / nHead) * seqLen;
                int colBase = (u % nHead) * hd;
                int attBase = u * seqLen * seqLen;
                var datt = new float[seqLen];

                for (int i = 0; i < seqLen; i++)
                {
                    var dOi = new ReadOnlySpan<float>(outp.Grad, (rowBase + i) * c + colBase, hd);
                    var aRow = new ReadOnlySpan<float>(att, attBase + i * seqLen, seqLen);

                    // dV_j += att_ij * dOut_i, and datt_ij = Dot(dOut_i, v_j) for the causal prefix.
                    float dot = 0f;
                    for (int j = 0; j <= i; j++)
                    {
                        var vj = new ReadOnlySpan<float>(v.Data, (rowBase + j) * c + colBase, hd);
                        datt[j] = TensorPrimitives.Dot(dOi, vj);
                        dot += datt[j] * aRow[j];

                        var dVj = new Span<float>(v.Grad, (rowBase + j) * c + colBase, hd);
                        TensorPrimitives.MultiplyAdd(dOi, aRow[j], dVj, dVj);
                    }

                    // Softmax backward gives the score grads, which flow into dQ and dK.
                    var qi = new ReadOnlySpan<float>(q.Data, (rowBase + i) * c + colBase, hd);
                    var dQi = new Span<float>(q.Grad, (rowBase + i) * c + colBase, hd);
                    for (int j = 0; j <= i; j++)
                    {
                        float ds = aRow[j] * (datt[j] - dot) * scale;
                        var kj = new ReadOnlySpan<float>(k.Data, (rowBase + j) * c + colBase, hd);
                        TensorPrimitives.MultiplyAdd(kj, ds, dQi, dQi);
                        var dKj = new Span<float>(k.Grad, (rowBase + j) * c + colBase, hd);
                        TensorPrimitives.MultiplyAdd(qi, ds, dKj, dKj);
                    }
                }
            });
        };
        return outp;
    }
}
