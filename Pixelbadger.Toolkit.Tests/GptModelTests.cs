using FluentAssertions;
using Pixelbadger.Toolkit.Services;

namespace Pixelbadger.Toolkit.Tests;

public class GptModelTests
{
    private static GptConfig SmallConfig() => new(VocabSize: 8, BlockSize: 4, NEmbd: 8, NHead: 2, NLayer: 2);

    [Fact]
    public void Forward_ShouldProduceLogitsWithExpectedShape()
    {
        var model = new GptModel(SmallConfig());
        model.InitWeights(0);

        var batch = new[]
        {
            new[] { 1, 2, 3, 0 },
            new[] { 4, 5, 6, 7 }
        };

        var (logits, loss) = model.Forward(batch);

        logits.Rows.Should().Be(8); // batch(2) * block(4)
        logits.Cols.Should().Be(8); // vocab
        loss.Should().BeNull();
    }

    [Fact]
    public void Forward_ShouldReturnFinitePositiveLoss_WhenTargetsProvided()
    {
        var model = new GptModel(SmallConfig());
        model.InitWeights(0);

        var batch = new[] { new[] { 1, 2, 3, 0 } };
        var targets = new[] { new[] { 2, 3, 0, 1 } };

        var (_, loss) = model.Forward(batch, targets);

        loss.Should().NotBeNull();
        float value = loss!.Data[0];
        float.IsFinite(value).Should().BeTrue();
        value.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void ParameterCount_ShouldMatchClosedFormForConfig()
    {
        var model = new GptModel(SmallConfig());

        // V*C + block*C + L*(12C^2 + 13C) + 2C, with V=8,C=8,block=4,L=2.
        model.ParameterCount().Should().Be(1856);
    }

    [Fact]
    public void InitWeights_ShouldBeDeterministicForFixedSeed()
    {
        var a = new GptModel(SmallConfig());
        a.InitWeights(42);
        var b = new GptModel(SmallConfig());
        b.InitWeights(42);

        var pa = a.Parameters();
        var pb = b.Parameters();
        for (int i = 0; i < pa.Count; i++)
            pa[i].Data.Should().Equal(pb[i].Data);
    }

    [Fact]
    public void LoadWeights_ShouldThrow_WhenCountMismatched()
    {
        var model = new GptModel(SmallConfig());

        var act = () => model.LoadWeights(new[] { new float[1] });

        act.Should().Throw<ArgumentException>().WithMessage("*weight tensors*");
    }
}
