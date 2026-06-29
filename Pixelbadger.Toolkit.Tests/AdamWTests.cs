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
}
