using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class AdamWTests
{
    [Fact]
    public void Step_ShouldApplyExpectedAdamUpdate_OnFirstStep()
    {
        var p = Tensor.FromData(1, 1, new[] { 1.0f }, requiresGrad: true);
        p.Grad[0] = 0.5f;

        var optimizer = new AdamW([p], lr: 0.1f, beta1: 0.9f, beta2: 0.95f, eps: 1e-8f, weightDecay: 0.1f);
        optimizer.Step();

        // m=0.05, v=0.0125; bias-corrected mHat=0.5, vHat=0.25 -> step = lr*(0.1*1 + 0.5/0.5) = 0.11
        p.Data[0].Should().BeApproximately(0.89f, 1e-5f);
    }

    [Fact]
    public void Step_ShouldApplyWeightDecay_WhenGradientIsZero()
    {
        var p = Tensor.FromData(1, 1, new[] { 2.0f }, requiresGrad: true);
        p.Grad[0] = 0f;

        var optimizer = new AdamW([p], lr: 0.1f, weightDecay: 0.1f);
        optimizer.Step();

        // Only decoupled weight decay applies: 2 - 0.1*0.1*2 = 1.98
        p.Data[0].Should().BeApproximately(1.98f, 1e-5f);
    }

    [Fact]
    public void Step_ShouldMoveParameterAgainstGradientDirection()
    {
        var p = Tensor.FromData(1, 1, new[] { 0.0f }, requiresGrad: true);
        p.Grad[0] = 1.0f;

        var optimizer = new AdamW([p], lr: 0.1f, weightDecay: 0f);
        optimizer.Step();

        p.Data[0].Should().BeLessThan(0f);
    }

    [Fact]
    public void ExportThenLoadState_ShouldReproduceIdenticalTrajectory()
    {
        // The same fixed gradient sequence drives a continuous optimizer and one that is
        // snapshotted and restored into a fresh instance midway; results must match exactly.
        var grads = new[]
        {
            new[] { 0.5f, -0.3f, 0.2f, 0.9f },
            new[] { -0.1f, 0.4f, -0.7f, 0.3f },
            new[] { 0.2f, 0.2f, 0.1f, -0.5f },
            new[] { -0.6f, 0.1f, 0.8f, 0.4f },
            new[] { 0.3f, -0.9f, 0.5f, -0.2f }
        };
        var initial = new[] { 0.5f, -0.3f, 0.2f, 0.9f };

        var pA = Tensor.FromData(2, 2, (float[])initial.Clone(), requiresGrad: true);
        var pB = Tensor.FromData(2, 2, (float[])initial.Clone(), requiresGrad: true);
        var optA = new AdamW([pA], lr: 0.01f);
        var optB = new AdamW([pB], lr: 0.01f);

        static void ApplyStep(Tensor p, AdamW opt, float[] grad)
        {
            Array.Copy(grad, p.Grad, grad.Length);
            opt.Step();
        }

        for (int s = 0; s < 3; s++)
        {
            ApplyStep(pA, optA, grads[s]);
            ApplyStep(pB, optB, grads[s]);
        }

        var restored = new AdamW([pB], lr: 0.01f);
        restored.LoadState(optB.ExportState());

        for (int s = 3; s < 5; s++)
        {
            ApplyStep(pA, optA, grads[s]);
            ApplyStep(pB, restored, grads[s]);
        }

        pB.Data.Should().Equal(pA.Data);
    }

    [Fact]
    public void LoadState_ShouldThrow_WhenShapesDoNotMatch()
    {
        var p = Tensor.FromData(1, 2, new[] { 1f, 2f }, requiresGrad: true);
        var optimizer = new AdamW([p]);

        var wrongCount = () => optimizer.LoadState(new AdamWState(1, [], []));
        wrongCount.Should().Throw<ArgumentException>().WithMessage("*moment tensors*");

        var wrongLength = () => optimizer.LoadState(new AdamWState(1, [new float[3]], [new float[3]]));
        wrongLength.Should().Throw<ArgumentException>().WithMessage("*length*");
    }
}
