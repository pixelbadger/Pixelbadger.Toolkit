using System.Text;
using FluentAssertions;
using Pixelbadger.Toolkit.Services.Audio;

namespace Pixelbadger.Toolkit.Tests;

public class WavReaderTests
{
    private static byte[] Chunk(string id, byte[] body, bool pad = true)
    {
        using var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes(id));
        ms.Write(BitConverter.GetBytes((uint)body.Length));
        ms.Write(body);
        if (pad && body.Length % 2 == 1) ms.WriteByte(0);
        return ms.ToArray();
    }

    private static byte[] Fmt(ushort tag, ushort channels, int rate, ushort bits)
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(tag); bw.Write(channels); bw.Write(rate);
        bw.Write(rate * channels * bits / 8); bw.Write((ushort)(channels * bits / 8)); bw.Write(bits);
        return ms.ToArray();
    }

    private static byte[] FmtExtensible(ushort subTag, ushort channels, int rate, ushort bits)
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(Fmt(0xFFFE, channels, rate, bits));
        bw.Write((ushort)22); bw.Write(bits); bw.Write(3u);
        bw.Write(subTag);
        bw.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 });
        return ms.ToArray();
    }

    private static byte[] Wav(params byte[][] chunks)
    {
        using var ms = new MemoryStream();
        var body = chunks.SelectMany(c => c).ToArray();
        ms.Write("RIFF"u8);
        ms.Write(BitConverter.GetBytes((uint)(body.Length + 4)));
        ms.Write("WAVE"u8);
        ms.Write(body);
        return ms.ToArray();
    }

    private static byte[] Shorts(params short[] v) => v.SelectMany(BitConverter.GetBytes).ToArray();
    private static byte[] Floats(params float[] v) => v.SelectMany(BitConverter.GetBytes).ToArray();

    private static PcmAudio Read(byte[] bytes) => WavReader.Read(new MemoryStream(bytes));

    [Fact]
    public void Read_ShouldReturnSamples_WhenMono16BitPcm()
    {
        var audio = Read(Wav(Chunk("fmt ", Fmt(1, 1, 16000, 16)), Chunk("data", Shorts(1, -2, 300))));
        audio.SampleRate.Should().Be(16000);
        audio.Channels.Should().Be(1);
        audio.Samples.Should().Equal(1, -2, 300);
    }

    [Fact]
    public void Read_ShouldReturnInterleavedSamples_WhenStereo16BitPcm()
    {
        var audio = Read(Wav(Chunk("fmt ", Fmt(1, 2, 48000, 16)), Chunk("data", Shorts(1, 2, 3, 4))));
        audio.Channels.Should().Be(2);
        audio.Samples.Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void Read_ShouldConvertWithClamping_WhenFloat32()
    {
        var audio = Read(Wav(Chunk("fmt ", Fmt(3, 1, 48000, 32)),
            Chunk("data", Floats(0f, 0.5f, -0.5f, 1.0f, -1.0f, 2.0f, -3.0f, float.NaN))));
        audio.Samples.Should().Equal(0, 16384, -16384, 32767, -32768, 32767, -32768, 0);
    }

    [Fact]
    public void Read_ShouldAcceptExtensiblePcm()
    {
        var audio = Read(Wav(Chunk("fmt ", FmtExtensible(1, 2, 24000, 16)), Chunk("data", Shorts(5, 6))));
        audio.Samples.Should().Equal(5, 6);
        audio.SampleRate.Should().Be(24000);
    }

    [Fact]
    public void Read_ShouldAcceptExtensibleFloat()
    {
        var audio = Read(Wav(Chunk("fmt ", FmtExtensible(3, 1, 48000, 32)), Chunk("data", Floats(0.25f))));
        audio.Samples.Should().Equal(8192);
    }

    [Fact]
    public void Read_ShouldSkipUnknownChunks_WhenListPrecedesAndFollowsData()
    {
        var audio = Read(Wav(Chunk("LIST", Encoding.ASCII.GetBytes("INFOabcd")), Chunk("fmt ", Fmt(1, 1, 8000, 16)),
            Chunk("data", Shorts(7, 8)), Chunk("LIST", Encoding.ASCII.GetBytes("tail"))));
        audio.Samples.Should().Equal(7, 8);
    }

    [Fact]
    public void Read_ShouldHandleOddSizedChunkPadding()
    {
        var audio = Read(Wav(Chunk("junk", new byte[] { 1, 2, 3 }), Chunk("fmt ", Fmt(1, 1, 8000, 16)),
            Chunk("odd!", new byte[] { 9 }), Chunk("data", Shorts(11, 12))));
        audio.Samples.Should().Equal(11, 12);
    }

    [Fact]
    public void Read_ShouldReadDataBeforeFmt_WhenOrderReversed()
    {
        var audio = Read(Wav(Chunk("data", Shorts(3, 4)), Chunk("fmt ", Fmt(1, 1, 8000, 16))));
        audio.Samples.Should().Equal(3, 4);
    }

    [Fact]
    public void Read_ShouldDropIncompleteFrame_WhenDataTruncated()
    {
        var audio = Read(Wav(Chunk("fmt ", Fmt(1, 2, 48000, 16)), Chunk("data", Shorts(1, 2, 3))));
        audio.Samples.Should().Equal(1, 2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("RIFF\0\0\0\0AVI LIST")]
    public void Read_ShouldThrowNotAWav_WhenHeaderBad(string text)
    {
        var act = () => Read(Encoding.ASCII.GetBytes(text));
        act.Should().Throw<InvalidDataException>().WithMessage("Not a WAV file");
    }

    [Fact]
    public void Read_ShouldThrowUnsupported_When24BitPcm()
    {
        var act = () => Read(Wav(Chunk("fmt ", Fmt(1, 1, 48000, 24)), Chunk("data", new byte[6])));
        act.Should().Throw<InvalidDataException>()
            .WithMessage("Unsupported WAV format: 24-bit PCM. Use 16-bit PCM or 32-bit float, mono or stereo.");
    }

    [Fact]
    public void Read_ShouldThrowUnsupported_WhenThreeChannels()
    {
        var act = () => Read(Wav(Chunk("fmt ", Fmt(1, 3, 48000, 16)), Chunk("data", new byte[6])));
        act.Should().Throw<InvalidDataException>()
            .WithMessage("Unsupported WAV format: 3 channels. Use 16-bit PCM or 32-bit float, mono or stereo.");
    }

    [Fact]
    public void Read_ShouldThrowUnsupported_WhenFormatTagIsNotPcmOrFloat()
    {
        var act = () => Read(Wav(Chunk("fmt ", Fmt(2, 1, 8000, 4)), Chunk("data", new byte[4])));
        act.Should().Throw<InvalidDataException>()
            .WithMessage("Unsupported WAV format: 4-bit format tag 2. Use 16-bit PCM or 32-bit float, mono or stereo.");
    }

    [Fact]
    public void Read_ShouldThrowUnsupported_WhenDataChunkMissing()
    {
        var act = () => Read(Wav(Chunk("fmt ", Fmt(1, 1, 8000, 16))));
        act.Should().Throw<InvalidDataException>()
            .WithMessage("Unsupported WAV format: missing data chunk. Use 16-bit PCM or 32-bit float, mono or stereo.");
    }

    [Fact]
    public void Read_ShouldThrowUnsupported_WhenFmtChunkMissing()
    {
        var act = () => Read(Wav(Chunk("data", Shorts(1))));
        act.Should().Throw<InvalidDataException>()
            .WithMessage("Unsupported WAV format: missing fmt chunk. Use 16-bit PCM or 32-bit float, mono or stereo.");
    }
}
