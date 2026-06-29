using System.Numerics.Tensors;

namespace Pixelbadger.Toolkit.Services;

/// <summary>
/// A minimal reverse-mode automatic differentiation engine over 2D float matrices,
/// hand-rolled from scratch (no TorchSharp / native ML libraries) in the spirit of
/// Karpathy's micrograd/nanoGPT. Hot kernels are accelerated with
/// <see cref="System.Numerics.Tensors.TensorPrimitives"/> (SIMD) and, for matrix
/// multiplication, <see cref="System.Threading.Tasks.Parallel"/> across independent
/// output rows. Reductions inside a single dot-product are kept sequential so results
/// are independent of thread count and therefore reproducible.
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

        Parallel.For(0, m, i =>
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
            Parallel.For(0, m, i =>
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
            Parallel.For(0, k, l =>
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
        for (int i = 0; i < x.Rows; i++)
        {
            var xRow = new ReadOnlySpan<float>(x.Data, i * x.Cols, x.Cols);
            var oRow = new Span<float>(outp.Data, i * x.Cols, x.Cols);
            TensorPrimitives.Add(xRow, bias.Data, oRow);
        }

        outp.Parents.Add(x);
        outp.Parents.Add(bias);
        outp.BackwardFn = () =>
        {
            AccumulateInPlace(x.Grad, outp.Grad);
            for (int i = 0; i < x.Rows; i++)
                for (int j = 0; j < x.Cols; j++)
                    bias.Grad[j] += outp.Grad[i * x.Cols + j];
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
        outp.BackwardFn = () =>
        {
            for (int i = 0; i < x.Length; i++)
                x.Grad[i] += outp.Grad[i] * s;
        };
        return outp;
    }

    /// <summary>Row-wise softmax with max-subtraction for numerical stability.</summary>
    public static Tensor SoftmaxRows(Tensor x)
    {
        int r = x.Rows, c = x.Cols;
        var outp = new Tensor(r, c);

        for (int i = 0; i < r; i++)
        {
            var row = new ReadOnlySpan<float>(x.Data, i * c, c);
            var oRow = new Span<float>(outp.Data, i * c, c);
            float max = TensorPrimitives.Max(row);
            TensorPrimitives.Subtract(row, max, oRow);
            TensorPrimitives.Exp(oRow, oRow);
            float sum = TensorPrimitives.Sum(oRow);
            TensorPrimitives.Divide(oRow, sum, oRow);
        }

        outp.Parents.Add(x);
        outp.BackwardFn = () =>
        {
            for (int i = 0; i < r; i++)
            {
                var y = new ReadOnlySpan<float>(outp.Data, i * c, c);
                var dy = new ReadOnlySpan<float>(outp.Grad, i * c, c);
                float dot = TensorPrimitives.Dot(dy, y);
                for (int j = 0; j < c; j++)
                    x.Grad[i * c + j] += y[j] * (dy[j] - dot);
            }
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

        for (int i = 0; i < r; i++)
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
        }

        outp.Parents.Add(x);
        outp.Parents.Add(gamma);
        outp.Parents.Add(beta);
        outp.BackwardFn = () =>
        {
            for (int i = 0; i < r; i++)
            {
                // dxhat_j = dy_j * gamma_j
                float sumDxhat = 0f, sumDxhatXhat = 0f;
                for (int j = 0; j < c; j++)
                {
                    float dy = outp.Grad[i * c + j];
                    float dxhat = dy * gamma.Data[j];
                    sumDxhat += dxhat;
                    sumDxhatXhat += dxhat * xhat[i * c + j];
                    gamma.Grad[j] += dy * xhat[i * c + j];
                    beta.Grad[j] += dy;
                }
                float istd = invStd[i];
                for (int j = 0; j < c; j++)
                {
                    float dxhat = outp.Grad[i * c + j] * gamma.Data[j];
                    x.Grad[i * c + j] += (istd / c) * (c * dxhat - sumDxhat - xhat[i * c + j] * sumDxhatXhat);
                }
            }
        };
        return outp;
    }

    private const float GeluC = 0.7978845608028654f; // sqrt(2/pi)
    private const float GeluA = 0.044715f;

    /// <summary>GELU activation (tanh approximation), elementwise.</summary>
    public static Tensor Gelu(Tensor x)
    {
        var outp = new Tensor(x.Rows, x.Cols);
        for (int i = 0; i < x.Length; i++)
        {
            float v = x.Data[i];
            float inner = GeluC * (v + GeluA * v * v * v);
            float t = MathF.Tanh(inner);
            outp.Data[i] = 0.5f * v * (1f + t);
        }

        outp.Parents.Add(x);
        outp.BackwardFn = () =>
        {
            for (int i = 0; i < x.Length; i++)
            {
                float v = x.Data[i];
                float inner = GeluC * (v + GeluA * v * v * v);
                float t = MathF.Tanh(inner);
                float dInner = GeluC * (1f + 3f * GeluA * v * v);
                float dgelu = 0.5f * (1f + t) + 0.5f * v * (1f - t * t) * dInner;
                x.Grad[i] += outp.Grad[i] * dgelu;
            }
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

        float lossSum = 0f;
        for (int i = 0; i < n; i++)
        {
            var row = new ReadOnlySpan<float>(logits.Data, i * v, v);
            var pRow = new Span<float>(probs, i * v, v);
            float max = TensorPrimitives.Max(row);
            TensorPrimitives.Subtract(row, max, pRow);
            TensorPrimitives.Exp(pRow, pRow);
            float sum = TensorPrimitives.Sum(pRow);
            TensorPrimitives.Divide(pRow, sum, pRow);
            lossSum += -MathF.Log(Math.Max(pRow[targets[i]], 1e-12f));
        }
        outp.Data[0] = lossSum / n;

        outp.Parents.Add(logits);
        outp.BackwardFn = () =>
        {
            float g = outp.Grad[0] / n;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < v; j++)
                    logits.Grad[i * v + j] += g * probs[i * v + j];
                logits.Grad[i * v + targets[i]] -= g;
            }
        };
        return outp;
    }
}
