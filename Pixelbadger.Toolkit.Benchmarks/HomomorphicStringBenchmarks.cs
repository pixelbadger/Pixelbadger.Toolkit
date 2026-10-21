using BenchmarkDotNet.Attributes;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Models;

namespace Pixelbadger.Toolkit.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory("slow")]
public class HomomorphicStringBenchmarks
{
    private HomomorphicEncryptionComponent _component = null!;
    private PaillierPublicKey _publicKey = null!;
    private PaillierKeyPair _keyPair = null!;
    private EncryptedString _encryptedShort = null!;
    private EncryptedString _encryptedLong = null!;
    private EncryptedNumber _encryptedA = null!;
    private EncryptedNumber _encryptedB = null!;

    [GlobalSetup]
    public void Setup()
    {
        _component = new HomomorphicEncryptionComponent();
        _keyPair = _component.GenerateKey();
        _publicKey = new PaillierPublicKey { N = _keyPair.N };
        _encryptedShort = _component.EncryptString(ShortPlaintext, _publicKey);
        _encryptedLong = _component.EncryptString(LongPlaintext, _publicKey);
        _encryptedA = _component.Encrypt(42, _publicKey);
        _encryptedB = _component.Encrypt(7, _publicKey);
    }

    public string ShortPlaintext { get; set; } = "hello";

    public string LongPlaintext => "The quick brown fox jumps over the lazy dog. Pack my box!";

    // Every encrypted string is padded to MaxStringLength entries, so a full-length input costs the same as a short one.
    public string MaxPlaintext => new('x', HomomorphicEncryptionComponent.MaxStringLength);

    [Benchmark]
    public EncryptedString EncryptString_Short() =>
        _component.EncryptString(ShortPlaintext, _publicKey);

    [Benchmark]
    public EncryptedString EncryptString_Long() =>
        _component.EncryptString(LongPlaintext, _publicKey);

    [Benchmark]
    public EncryptedString EncryptString_Max() =>
        _component.EncryptString(MaxPlaintext, _publicKey);

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

    [Benchmark]
    public EncryptedString ReplaceInString_IntoPadding() =>
        _component.ReplaceInString(_encryptedShort, 90, "pad");

    // Arithmetic results are re-randomised, so each operation pays one extra r^n mod n^2.
    [Benchmark]
    public EncryptedNumber AddEncrypted() =>
        _component.AddEncrypted(_encryptedA, _encryptedB);

    [Benchmark]
    public EncryptedNumber SubtractEncrypted() =>
        _component.SubtractEncrypted(_encryptedA, _encryptedB);

    [Benchmark]
    public EncryptedNumber MultiplyEncrypted_ByZero() =>
        _component.MultiplyEncrypted(_encryptedA, 0);

    [Benchmark]
    public EncryptedNumber MultiplyEncrypted_ByScalar() =>
        _component.MultiplyEncrypted(_encryptedA, 7);

    [Benchmark]
    public PaillierKeyPair GenerateKey() =>
        _component.GenerateKey();
}
