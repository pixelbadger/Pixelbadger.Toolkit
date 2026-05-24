using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Models;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
public class HomomorphicStringBenchmarks
{
    private HomomorphicEncryptionComponent _component = null!;
    private PaillierPublicKey _publicKey = null!;
    private PaillierKeyPair _keyPair = null!;
    private EncryptedString _encryptedShort = null!;
    private EncryptedString _encryptedLong = null!;

    [GlobalSetup]
    public void Setup()
    {
        _component = new HomomorphicEncryptionComponent();
        _keyPair = _component.GenerateKey();
        _publicKey = new PaillierPublicKey { N = _keyPair.N };
        _encryptedShort = _component.EncryptString(ShortPlaintext, _publicKey);
        _encryptedLong = _component.EncryptString(LongPlaintext, _publicKey);
    }

    [Params("hello", "hello, world!")]
    public string ShortPlaintext { get; set; } = "hello";

    public string LongPlaintext => "The quick brown fox jumps over the lazy dog. Pack my box!";

    [Benchmark]
    public EncryptedString EncryptString_Short() =>
        _component.EncryptString(ShortPlaintext, _publicKey);

    [Benchmark]
    public EncryptedString EncryptString_Long() =>
        _component.EncryptString(LongPlaintext, _publicKey);

    [Benchmark]
    public string DecryptString_Short() =>
        _component.DecryptString(_encryptedShort, _keyPair);

    [Benchmark]
    public string DecryptString_Long() =>
        _component.DecryptString(_encryptedLong, _keyPair);

    [Benchmark]
    public EncryptedString SubstringEncrypted_FromStart() =>
        _component.SubstringEncrypted(_encryptedLong, 0, 10);

    [Benchmark]
    public EncryptedString SubstringEncrypted_FromMiddle() =>
        _component.SubstringEncrypted(_encryptedLong, 10, 20);

    [Benchmark]
    public EncryptedString SubstringEncrypted_ToEnd() =>
        _component.SubstringEncrypted(_encryptedLong, 20);

    [Benchmark]
    public EncryptedString ReplaceInString_SingleChar() =>
        _component.ReplaceInString(_encryptedLong, 0, "T");

    [Benchmark]
    public EncryptedString ReplaceInString_Word() =>
        _component.ReplaceInString(_encryptedLong, 4, "slow");
}
