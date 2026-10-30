using FluentAssertions;
using Pixelbadger.Toolkit.Opus.Ogg;

namespace Pixelbadger.Toolkit.Tests;

public class OpusHeadTests
{
    [Fact]
    public void ToBytes_ShouldRoundTripThroughParse_WhenFamilyZero()
    {
        var head = new OpusHead(1, 2, 312, 44100, -256, 0);
        var bytes = head.ToBytes();

        bytes.Should().HaveCount(19);
        bytes.Take(8).Should().Equal("OpusHead"u8.ToArray());
        OpusHead.Parse(bytes).Should().Be(head);
    }

    [Fact]
    public void ToBytes_ShouldUseLittleEndianFields_WhenSerialised()
    {
        var bytes = new OpusHead(1, 1, 0x0138, 0x0000BB80, 0, 0).ToBytes();
        bytes[10].Should().Be(0x38);
        bytes[11].Should().Be(0x01);
        bytes[12].Should().Be(0x80);
        bytes[13].Should().Be(0xBB);
    }

    [Fact]
    public void ToBytes_ShouldThrow_WhenMappingFamilyNonZero()
    {
        var act = () => new OpusHead(1, 6, 312, 48000, 0, 1).ToBytes();
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Parse_ShouldThrowNotOggOpus_WhenMagicWrong()
    {
        var bytes = new OpusHead(1, 1, 312, 48000, 0, 0).ToBytes();
        bytes[0] = (byte)'X';
        var act = () => OpusHead.Parse(bytes);
        act.Should().Throw<InvalidDataException>().WithMessage("Not an Ogg Opus file");
    }

    [Fact]
    public void Parse_ShouldThrowNotOggOpus_WhenTooShort()
    {
        var act = () => OpusHead.Parse(new byte[10]);
        act.Should().Throw<InvalidDataException>().WithMessage("Not an Ogg Opus file");
    }

    [Fact]
    public void Parse_ShouldReadFamilyOne_WhenPresent()
    {
        var bytes = new OpusHead(1, 6, 312, 48000, 0, 0).ToBytes();
        bytes[18] = 1;
        OpusHead.Parse(bytes).MappingFamily.Should().Be(1);
    }

    [Fact]
    public void Parse_ShouldThrow_WhenVersionIncompatible()
    {
        var bytes = new OpusHead(1, 1, 312, 48000, 0, 0).ToBytes();
        bytes[8] = 0x10;
        var act = () => OpusHead.Parse(bytes);
        act.Should().Throw<InvalidDataException>().WithMessage("Unsupported OpusHead version 16");
    }
}
