using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

/// <summary>
/// Validates the hand-rolled autograd engine. Each primitive op is checked by comparing its
/// analytic gradient against a central finite-difference approximation of L = sum(g .* output)
/// for a fixed random upstream gradient g.
/// </summary>
public class TensorTests
{
    private static Tensor RandomTensor(int rows, int cols, Random rng, float scale = 1f)
    {
        var t = new Tensor(rows, cols, requiresGrad: true);
        for (int i = 0; i < t.Length; i++)
            t.Data[i] = (float)(rng.NextDouble() * 2 - 1) * scale;
        return t;
    }

    private static float[] RandomArray(int n, Random rng)
    {
        var a = new float[n];
        for (int i = 0; i < n; i++)
            a[i] = (float)(rng.NextDouble() * 2 - 1);
        return a;
    }

    private static float Dot(float[] a, float[] b)
    {
        float s = 0f;
        for (int i = 0; i < a.Length; i++)
            s += a[i] * b[i];
        return s;
    }

    private static void AssertClose(float analytic, float numeric)
    {
        float diff = MathF.Abs(analytic - numeric);
        float bound = 3e-2f + 3e-2f * MathF.Abs(numeric);
        diff.Should().BeLessThanOrEqualTo(bound, "analytic {0} vs numeric {1}", analytic, numeric);
    }

    private static void GradCheckOp(Tensor[] inputs, Func<Tensor> build, Random rng)
    {
        var output = build();
        var g = RandomArray(output.Length, rng);

        foreach (var inp in inputs)
            inp.ZeroGrad();
        Array.Clear(output.Grad, 0, output.Grad.Length);
        Array.Copy(g, output.Grad, g.Length);
        output.BackwardFn!.Invoke();

        const float eps = 1e-2f;
        foreach (var inp in inputs)
        {
            var analytic = (float[])inp.Grad.Clone();
            for (int j = 0; j < inp.Length; j++)
            {
                float orig = inp.Data[j];
                inp.Data[j] = orig + eps;
                float lp = Dot(build().Data, g);
                inp.Data[j] = orig - eps;
                float lm = Dot(build().Data, g);
                inp.Data[j] = orig;
                float num = (lp - lm) / (2 * eps);
                AssertClose(analytic[j], num);
            }
        }
    }

    [Fact]
    public void MatMul_ShouldComputeCorrectForwardAndGradients()
    {
        var rng = new Random(1);
        var a = RandomTensor(2, 3, rng);
        var b = RandomTensor(3, 4, rng);

        var y = Tensor.MatMul(a, b);
        y.Rows.Should().Be(2);
        y.Cols.Should().Be(4);

        // Spot-check one forward element.
        float expected00 = a.Data[0] * b.Data[0] + a.Data[1] * b.Data[4] + a.Data[2] * b.Data[8];
        y.Data[0].Should().BeApproximately(expected00, 1e-5f);

        GradCheckOp([a, b], () => Tensor.MatMul(a, b), rng);
    }

    [Fact]
    public void AddBias_ShouldGradientCheck()
    {
        var rng = new Random(2);
        var x = RandomTensor(3, 4, rng);
        var bias = RandomTensor(1, 4, rng);
        GradCheckOp([x, bias], () => Tensor.AddBias(x, bias), rng);
    }

    [Fact]
    public void Add_ShouldGradientCheck()
    {
        var rng = new Random(3);
        var a = RandomTensor(3, 4, rng);
        var b = RandomTensor(3, 4, rng);
        GradCheckOp([a, b], () => Tensor.Add(a, b), rng);
    }

    [Fact]
    public void Scale_ShouldGradientCheck()
    {
        var rng = new Random(4);
        var x = RandomTensor(3, 4, rng);
        GradCheckOp([x], () => Tensor.Scale(x, 1.7f), rng);
    }

    [Fact]
    public void Transpose_ShouldGradientCheck()
    {
        var rng = new Random(5);
        var x = RandomTensor(2, 3, rng);
        GradCheckOp([x], () => Tensor.Transpose(x), rng);
    }

    [Fact]
    public void SoftmaxRows_ShouldGradientCheckAndSumToOne()
    {
        var rng = new Random(6);
        var x = RandomTensor(3, 5, rng);

        var y = Tensor.SoftmaxRows(x);
        for (int i = 0; i < 3; i++)
        {
            float sum = 0f;
            for (int j = 0; j < 5; j++)
                sum += y.Data[i * 5 + j];
            sum.Should().BeApproximately(1f, 1e-5f);
        }

        GradCheckOp([x], () => Tensor.SoftmaxRows(x), rng);
    }

    [Fact]
    public void LayerNorm_ShouldGradientCheck()
    {
        var rng = new Random(7);
        var x = RandomTensor(3, 6, rng);
        var gamma = RandomTensor(1, 6, rng);
        var beta = RandomTensor(1, 6, rng);
        GradCheckOp([x, gamma, beta], () => Tensor.LayerNorm(x, gamma, beta), rng);
    }

    [Fact]
    public void Gelu_ShouldGradientCheck()
    {
        var rng = new Random(8);
        var x = RandomTensor(3, 4, rng, scale: 2f);
        GradCheckOp([x], () => Tensor.Gelu(x), rng);
    }

    [Fact]
    public void Gather_ShouldGradientCheckWithRepeatedIds()
    {
        var rng = new Random(9);
        var table = RandomTensor(5, 3, rng);
        var ids = new[] { 0, 2, 2, 1 };
        GradCheckOp([table], () => Tensor.Gather(table, ids), rng);
    }

    [Fact]
    public void SliceRows_ShouldGradientCheck()
    {
        var rng = new Random(10);
        var x = RandomTensor(5, 3, rng);
        GradCheckOp([x], () => Tensor.SliceRows(x, 1, 3), rng);
    }

    [Fact]
    public void SliceCols_ShouldGradientCheck()
    {
        var rng = new Random(11);
        var x = RandomTensor(4, 6, rng);
        GradCheckOp([x], () => Tensor.SliceCols(x, 2, 3), rng);
    }

    [Fact]
    public void ConcatCols_ShouldGradientCheck()
    {
        var rng = new Random(12);
        var a = RandomTensor(3, 2, rng);
        var b = RandomTensor(3, 4, rng);
        GradCheckOp([a, b], () => Tensor.ConcatCols([a, b]), rng);
    }

    [Fact]
    public void ConcatRows_ShouldGradientCheck()
    {
        var rng = new Random(13);
        var a = RandomTensor(2, 3, rng);
        var b = RandomTensor(4, 3, rng);
        GradCheckOp([a, b], () => Tensor.ConcatRows([a, b]), rng);
    }

    [Fact]
    public void CrossEntropy_ShouldGradientCheck()
    {
        var rng = new Random(14);
        var logits = RandomTensor(4, 3, rng);
        var targets = new[] { 0, 2, 1, 2 };
        GradCheckOp([logits], () => Tensor.CrossEntropy(logits, targets), rng);
    }

    [Fact]
    public void CausalMask_ShouldZeroFutureAndRouteGradientsToPast()
    {
        var rng = new Random(15);
        var scores = RandomTensor(3, 3, rng);

        var masked = Tensor.CausalMask(scores);
        // Future positions (j > i) are filled with a large negative value.
        masked.Data[0 * 3 + 1].Should().Be(-1e9f);
        masked.Data[0 * 3 + 2].Should().Be(-1e9f);
        masked.Data[1 * 3 + 2].Should().Be(-1e9f);
        // Past/current positions are passed through unchanged.
        masked.Data[2 * 3 + 0].Should().Be(scores.Data[2 * 3 + 0]);

        Array.Fill(masked.Grad, 1f);
        masked.BackwardFn!.Invoke();
        // Lower triangle receives gradient 1, upper triangle receives 0.
        scores.Grad[2 * 3 + 0].Should().Be(1f);
        scores.Grad[0 * 3 + 1].Should().Be(0f);
        scores.Grad[1 * 3 + 2].Should().Be(0f);
    }

    [Fact]
    public void Backward_ShouldAccumulateGradientsThroughSharedInput()
    {
        var rng = new Random(16);
        var x = RandomTensor(2, 2, rng);

        // y = 2x + 3x = 5x, so dy/dx = 5 for every element.
        var y = Tensor.Add(Tensor.Scale(x, 2f), Tensor.Scale(x, 3f));
        y.Backward();

        foreach (var grad in x.Grad)
            grad.Should().BeApproximately(5f, 1e-5f);
    }
}
