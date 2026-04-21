using System.Numerics;
using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Models;

namespace Pixelbadger.Toolkit.Tests;

public class HomomorphicEncryptionComponentTests
{
    // 64-bit keys (32-bit primes) for fast test execution
    private const int TestKeyBitLength = 64;
    private readonly HomomorphicEncryptionComponent _component = new();

    [Fact]
    public void GenerateKey_ShouldReturnKeyPairWithValidPositiveBigIntegerStrings()
    {
        var key = _component.GenerateKey(TestKeyBitLength);

        BigInteger.TryParse(key.N, out var n).Should().BeTrue();
        BigInteger.TryParse(key.Lambda, out var lambda).Should().BeTrue();
        BigInteger.TryParse(key.Mu, out var mu).Should().BeTrue();
        n.Should().BeGreaterThan(BigInteger.Zero);
        lambda.Should().BeGreaterThan(BigInteger.Zero);
        mu.Should().BeGreaterThan(BigInteger.Zero);
    }

    [Fact]
    public void GenerateKey_ShouldSatisfyPaillierKeyRelationship_MuIsModularInverseOfLambdaModN()
    {
        var key = _component.GenerateKey(TestKeyBitLength);
        var n = BigInteger.Parse(key.N);
        var lambda = BigInteger.Parse(key.Lambda);
        var mu = BigInteger.Parse(key.Mu);

        // mu = lambda^-1 mod n, so lambda * mu ≡ 1 mod n
        (lambda * mu % n).Should().Be(BigInteger.One);
    }

    [Fact]
    public void Encrypt_ShouldReturnCiphertextDifferentFromPlaintext()
    {
        var key = _component.GenerateKey(TestKeyBitLength);
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.Encrypt(42L, publicKey);

        BigInteger.TryParse(encrypted.Ciphertext, out var ciphertext).Should().BeTrue();
        ciphertext.Should().NotBe(new BigInteger(42));
        encrypted.N.Should().Be(key.N);
    }

    [Fact]
    public void Encrypt_ShouldProduceProbabilisticCiphertext_SamePlaintextYieldsDifferentCiphertexts()
    {
        var key = _component.GenerateKey(TestKeyBitLength);
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted1 = _component.Encrypt(7L, publicKey);
        var encrypted2 = _component.Encrypt(7L, publicKey);

        // Paillier is semantically secure: same plaintext encrypts to different ciphertexts
        encrypted1.Ciphertext.Should().NotBe(encrypted2.Ciphertext);
    }

    [Fact]
    public void Decrypt_ShouldReturnOriginalPlaintext_WhenDecryptingEncryptedNumber()
    {
        var key = _component.GenerateKey(TestKeyBitLength);
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.Encrypt(42L, publicKey);
        var decrypted = _component.Decrypt(encrypted, key);

        decrypted.Should().Be(new BigInteger(42));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(100L)]
    [InlineData(999999L)]
    public void Decrypt_ShouldReturnCorrectValue_ForVariousPlaintextValues(long plaintext)
    {
        var key = _component.GenerateKey(TestKeyBitLength);
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.Encrypt(plaintext, publicKey);
        var decrypted = _component.Decrypt(encrypted, key);

        decrypted.Should().Be(new BigInteger(plaintext));
    }

    [Fact]
    public void AddEncrypted_ShouldDecryptToSumOfOriginalValues()
    {
        var key = _component.GenerateKey(TestKeyBitLength);
        var publicKey = new PaillierPublicKey { N = key.N };

        var encryptedA = _component.Encrypt(15L, publicKey);
        var encryptedB = _component.Encrypt(27L, publicKey);
        var encryptedSum = _component.AddEncrypted(encryptedA, encryptedB);
        var decryptedSum = _component.Decrypt(encryptedSum, key);

        decryptedSum.Should().Be(new BigInteger(42));
    }

    [Fact]
    public void AddEncrypted_ShouldDecryptToCorrectSum_WhenAddingZero()
    {
        var key = _component.GenerateKey(TestKeyBitLength);
        var publicKey = new PaillierPublicKey { N = key.N };

        var encryptedA = _component.Encrypt(99L, publicKey);
        var encryptedZero = _component.Encrypt(0L, publicKey);
        var encryptedSum = _component.AddEncrypted(encryptedA, encryptedZero);
        var decryptedSum = _component.Decrypt(encryptedSum, key);

        decryptedSum.Should().Be(new BigInteger(99));
    }

    [Fact]
    public void AddEncrypted_ShouldReturnEncryptedNumberWithSameN_AsInputs()
    {
        var key = _component.GenerateKey(TestKeyBitLength);
        var publicKey = new PaillierPublicKey { N = key.N };

        var encryptedA = _component.Encrypt(5L, publicKey);
        var encryptedB = _component.Encrypt(3L, publicKey);
        var encryptedSum = _component.AddEncrypted(encryptedA, encryptedB);

        encryptedSum.N.Should().Be(key.N);
    }

    [Fact]
    public void Encrypt_ShouldThrowArgumentException_WhenPlaintextIsNegative()
    {
        var key = _component.GenerateKey(TestKeyBitLength);
        var publicKey = new PaillierPublicKey { N = key.N };

        var act = () => _component.Encrypt(-1L, publicKey);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*non-negative*");
    }

    [Fact]
    public void AddEncrypted_ShouldThrowArgumentException_WhenPublicKeysDoNotMatch()
    {
        var key1 = _component.GenerateKey(TestKeyBitLength);
        var key2 = _component.GenerateKey(TestKeyBitLength);

        var encrypted1 = _component.Encrypt(5L, new PaillierPublicKey { N = key1.N });
        var encrypted2 = _component.Encrypt(3L, new PaillierPublicKey { N = key2.N });

        var act = () => _component.AddEncrypted(encrypted1, encrypted2);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*same public key*");
    }
}
