using System.Text;

namespace Pixelbadger.Toolkit.Tests;

internal enum Waveform
{
    Sine,
    Square
}

/// <summary>Generates a minimal valid SoundFont 2 and Standard MIDI Files in code, so no binary assets are committed.</summary>
internal static class MidiTestAssets
{
    private const int CycleSamples = 100;

    /// <summary>One preset (bank 0, program 0) covering keys 0-127 at all velocities; one looped sample whose pitch is A4 at key 69.</summary>
    public static byte[] SoundFont(Waveform waveform, double releaseSeconds = 0.5)
    {
        var sampleBytes = new byte[(CycleSamples + 46) * 2];
        for (var i = 0; i < CycleSamples; i++)
        {
            var phase = (double)i / CycleSamples;
            var value = waveform == Waveform.Sine
                ? Math.Sin(2 * Math.PI * phase)
                : (phase < 0.5 ? 1.0 : -1.0);
            BitConverter.GetBytes((short)Math.Round(value * 16000)).CopyTo(sampleBytes, i * 2);
        }

        var releaseTimecents = (short)Math.Round(1200 * Math.Log2(releaseSeconds));

        var info = Concat(
            Chunk("ifil", Concat(U16(2), U16(1))),
            Chunk("isng", Ascii("EMU8000", 8)),
            Chunk("INAM", Ascii("Test", 8)));
        var sdta = Chunk("smpl", sampleBytes);

        var phdr = Concat(
            Phdr("Test", 0, 0, 0),
            Phdr("EOP", 255, 0, 1));
        var pbag = Concat(Bag(0), Bag(2));
        var pmod = new byte[10];
        var pgen = Concat(
            Gen(43, Range(0, 127)),
            Gen(41, 0),
            Gen(0, 0));
        var inst = Concat(Inst("Test", 0), Inst("EOI", 1));
        var ibag = Concat(Bag(0), Bag(5));
        var imod = new byte[10];
        var igen = Concat(
            Gen(43, Range(0, 127)),
            Gen(44, Range(0, 127)),
            Gen(38, unchecked((ushort)releaseTimecents)),
            Gen(54, 1),
            Gen(53, 0),
            Gen(0, 0));
        var shdr = Concat(
            Shdr("Sample", 0, (uint)CycleSamples, 0, (uint)CycleSamples, 44000, 69, 1),
            Shdr("EOS", 0, 0, 0, 0, 0, 0, 0));
        var pdta = Concat(
            Chunk("phdr", phdr), Chunk("pbag", pbag), Chunk("pmod", pmod), Chunk("pgen", pgen),
            Chunk("inst", inst), Chunk("ibag", ibag), Chunk("imod", imod), Chunk("igen", igen),
            Chunk("shdr", shdr));

        var body = Concat(
            Ascii("sfbk", 4),
            List("INFO", info),
            List("sdta", sdta),
            List("pdta", pdta));
        return Concat(Ascii("RIFF", 4), U32((uint)body.Length), body);
    }

    /// <summary>Format-0 SMF at 120 bpm and 480 ticks per quarter note; every note is on channel 0 at velocity 100.</summary>
    public static byte[] Midi(params (int Note, double StartSeconds, double DurationSeconds)[] notes) =>
        Midi(480, 500_000, notes);

    public static byte[] Midi(int ticksPerQuarter, int microsecondsPerQuarter,
        params (int Note, double StartSeconds, double DurationSeconds)[] notes)
    {
        var ticksPerSecond = ticksPerQuarter * 1_000_000.0 / microsecondsPerQuarter;
        var events = new List<(long Tick, int Order, byte[] Data)>();
        foreach (var (note, start, duration) in notes)
        {
            var on = (long)Math.Round(start * ticksPerSecond);
            var off = (long)Math.Round((start + duration) * ticksPerSecond);
            events.Add((on, 1, new byte[] { 0x90, (byte)note, 100 }));
            events.Add((off, 0, new byte[] { 0x80, (byte)note, 0 }));
        }

        events.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Order.CompareTo(b.Order));

        var track = new List<byte>();
        track.AddRange(new byte[] { 0x00, 0xFF, 0x51, 0x03,
            (byte)(microsecondsPerQuarter >> 16), (byte)(microsecondsPerQuarter >> 8), (byte)microsecondsPerQuarter });
        long previous = 0;
        foreach (var (tick, _, data) in events)
        {
            track.AddRange(VarLen(tick - previous));
            track.AddRange(data);
            previous = tick;
        }

        track.AddRange(new byte[] { 0x00, 0xFF, 0x2F, 0x00 });

        return Concat(
            Ascii("MThd", 4), BigEndian32(6), BigEndian16(0), BigEndian16(1), BigEndian16((ushort)ticksPerQuarter),
            Ascii("MTrk", 4), BigEndian32((uint)track.Count), track.ToArray());
    }

    public static string WriteSoundFont(string directory, string name, Waveform waveform)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, SoundFont(waveform));
        return path;
    }

    public static string WriteMidi(string directory, string name, params (int Note, double StartSeconds, double DurationSeconds)[] notes)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, Midi(notes));
        return path;
    }

    /// <summary>A uniquely named temp directory, deleted on dispose.</summary>
    public sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Combine(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }

    private static byte[] Phdr(string name, ushort preset, ushort bank, ushort bag) =>
        Concat(Ascii(name, 20), U16(preset), U16(bank), U16(bag), U32(0), U32(0), U32(0));

    private static byte[] Inst(string name, ushort bag) => Concat(Ascii(name, 20), U16(bag));

    private static byte[] Bag(ushort genIndex) => Concat(U16(genIndex), U16(0));

    private static byte[] Gen(ushort oper, ushort amount) => Concat(U16(oper), U16(amount));

    private static ushort Range(int low, int high) => (ushort)(low | (high << 8));

    private static byte[] Shdr(string name, uint start, uint end, uint loopStart, uint loopEnd, uint rate, byte pitch, ushort type) =>
        Concat(Ascii(name, 20), U32(start), U32(end), U32(loopStart), U32(loopEnd), U32(rate),
            new byte[] { pitch, 0 }, U16(0), U16(type));

    private static byte[] List(string type, byte[] content) =>
        Chunk("LIST", Concat(Ascii(type, 4), content));

    private static byte[] Chunk(string id, byte[] data)
    {
        var padded = data.Length % 2 == 1 ? Concat(data, new byte[1]) : data;
        return Concat(Ascii(id, 4), U32((uint)data.Length), padded);
    }

    private static byte[] Ascii(string text, int length)
    {
        var bytes = new byte[length];
        Encoding.ASCII.GetBytes(text).AsSpan(0, Math.Min(text.Length, length)).CopyTo(bytes);
        return bytes;
    }

    private static byte[] U16(ushort value) => BitConverter.GetBytes(value);

    private static byte[] U32(uint value) => BitConverter.GetBytes(value);

    private static byte[] BigEndian16(ushort value) => new[] { (byte)(value >> 8), (byte)value };

    private static byte[] BigEndian32(uint value) =>
        new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };

    private static byte[] VarLen(long value)
    {
        var bytes = new Stack<byte>();
        bytes.Push((byte)(value & 0x7F));
        while ((value >>= 7) > 0)
            bytes.Push((byte)((value & 0x7F) | 0x80));
        return bytes.ToArray();
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
