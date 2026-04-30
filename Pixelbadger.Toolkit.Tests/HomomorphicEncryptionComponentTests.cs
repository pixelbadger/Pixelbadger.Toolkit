using System.Numerics;
using FluentAssertions;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Models;

namespace Pixelbadger.Toolkit.Tests;

public class HomomorphicEncryptionComponentTests
{
    private readonly HomomorphicEncryptionComponent _component = new();
    private static readonly Lazy<PaillierKeyPair> TestKey = new(() => new HomomorphicEncryptionComponent().GenerateKey());
    private static readonly Lazy<PaillierKeyPair> SecondTestKey = new(() => new HomomorphicEncryptionComponent().GenerateKey());

    [Fact]
    public void GenerateKey_ShouldReturnKeyPairWithValidPositiveBigIntegerStrings()
    {
        var key = TestKey.Value;

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
        var key = TestKey.Value;
        var n = BigInteger.Parse(key.N);
        var lambda = BigInteger.Parse(key.Lambda);
        var mu = BigInteger.Parse(key.Mu);

        // mu = lambda^-1 mod n, so lambda * mu ≡ 1 mod n
        (lambda * mu % n).Should().Be(BigInteger.One);
    }

    [Fact]
    public void Encrypt_ShouldReturnCiphertextDifferentFromPlaintext()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.Encrypt(42L, publicKey);

        BigInteger.TryParse(encrypted.Ciphertext, out var ciphertext).Should().BeTrue();
        ciphertext.Should().NotBe(new BigInteger(42));
        encrypted.N.Should().Be(key.N);
    }

    [Fact]
    public void Encrypt_ShouldProduceProbabilisticCiphertext_SamePlaintextYieldsDifferentCiphertexts()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted1 = _component.Encrypt(7L, publicKey);
        var encrypted2 = _component.Encrypt(7L, publicKey);

        // Paillier is semantically secure: same plaintext encrypts to different ciphertexts
        encrypted1.Ciphertext.Should().NotBe(encrypted2.Ciphertext);
    }

    [Fact]
    public void Decrypt_ShouldReturnOriginalPlaintext_WhenDecryptingEncryptedNumber()
    {
        var key = TestKey.Value;
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
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.Encrypt(plaintext, publicKey);
        var decrypted = _component.Decrypt(encrypted, key);

        decrypted.Should().Be(new BigInteger(plaintext));
    }

    [Fact]
    public void AddEncrypted_ShouldDecryptToSumOfOriginalValues()
    {
        var key = TestKey.Value;
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
        var key = TestKey.Value;
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
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encryptedA = _component.Encrypt(5L, publicKey);
        var encryptedB = _component.Encrypt(3L, publicKey);
        var encryptedSum = _component.AddEncrypted(encryptedA, encryptedB);

        encryptedSum.N.Should().Be(key.N);
    }

    [Theory]
    [InlineData(5L, 3L, 15L)]
    [InlineData(10L, 10L, 100L)]
    [InlineData(42L, 1L, 42L)]
    [InlineData(99L, 0L, 0L)]
    public void MultiplyEncrypted_ShouldDecryptToProduct_ForVariousScalars(long plaintext, long scalar, long expected)
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.Encrypt(plaintext, publicKey);
        var product = _component.MultiplyEncrypted(encrypted, scalar);
        var decrypted = _component.Decrypt(product, key);

        decrypted.Should().Be(new BigInteger(expected));
    }

    [Fact]
    public void MultiplyEncrypted_ShouldReturnEncryptedNumberWithSameN()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.Encrypt(7L, publicKey);
        var product = _component.MultiplyEncrypted(encrypted, 3L);

        product.N.Should().Be(key.N);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("Hello, World!")]
    [InlineData("café")]
    [InlineData("")]
    public void EncryptString_ThenDecryptString_ShouldReturnOriginal(string plaintext)
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString(plaintext, publicKey);
        var decrypted = _component.DecryptString(encrypted, key);

        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void EncryptString_ShouldEncryptOneCodePointPerCharacter()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello", publicKey);

        encrypted.Characters.Should().HaveCount(5);
    }

    [Fact]
    public void EncryptString_ShouldThrowArgumentException_WhenStringExceedsMaxLength()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };
        var tooLong = new string('a', HomomorphicEncryptionComponent.MaxStringLength + 1);

        var act = () => _component.EncryptString(tooLong, publicKey);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"*{HomomorphicEncryptionComponent.MaxStringLength} characters*");
    }

    [Fact]
    public void ReplaceInString_ShouldDecryptToUpdatedString_WhenReplacingMiddleChars()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello world", publicKey);
        var replaced = _component.ReplaceInString(encrypted, 6, "there");
        var decrypted = _component.DecryptString(replaced, key);

        decrypted.Should().Be("hello there");
    }

    [Fact]
    public void ReplaceInString_ShouldDecryptToUpdatedString_WhenReplacingAtStart()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello", publicKey);
        var replaced = _component.ReplaceInString(encrypted, 0, "H");
        var decrypted = _component.DecryptString(replaced, key);

        decrypted.Should().Be("Hello");
    }

    [Fact]
    public void ReplaceInString_ShouldDecryptToUpdatedString_WhenReplacingAtEnd()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello", publicKey);
        var replaced = _component.ReplaceInString(encrypted, 3, "p!");
        var decrypted = _component.DecryptString(replaced, key);

        decrypted.Should().Be("help!");
    }

    [Fact]
    public void ReplaceInString_ShouldNotModifyOtherCharacters_WhenReplacingSubset()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("abcde", publicKey);
        var originalCiphertext0 = encrypted.Characters[0].Ciphertext;
        var originalCiphertext4 = encrypted.Characters[4].Ciphertext;

        var replaced = _component.ReplaceInString(encrypted, 2, "X");

        replaced.Characters[0].Ciphertext.Should().Be(originalCiphertext0);
        replaced.Characters[4].Ciphertext.Should().Be(originalCiphertext4);
    }

    [Fact]
    public void ReplaceInString_ShouldShrinkString_WhenReplacementIsShorterThanLength()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("the quick brown fox", publicKey);
        var replaced = _component.ReplaceInString(encrypted, 4, "slow", 5);
        var decrypted = _component.DecryptString(replaced, key);

        decrypted.Should().Be("the slow brown fox");
    }

    [Fact]
    public void ReplaceInString_ShouldGrowString_WhenReplacementIsLongerThanLength()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("the fox", publicKey);
        var replaced = _component.ReplaceInString(encrypted, 4, "quick fox", 3);
        var decrypted = _component.DecryptString(replaced, key);

        decrypted.Should().Be("the quick fox");
    }

    [Fact]
    public void ReplaceInString_ShouldDeleteCharacters_WhenReplacementIsEmpty()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello world", publicKey);
        var replaced = _component.ReplaceInString(encrypted, 5, "", 6);
        var decrypted = _component.DecryptString(replaced, key);

        decrypted.Should().Be("hello");
    }

    [Fact]
    public void ReplaceInString_ShouldThrowArgumentException_WhenLengthIsNegative()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello", publicKey);

        var act = () => _component.ReplaceInString(encrypted, 0, "x", -1);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*non-negative*");
    }

    [Fact]
    public void ReplaceInString_ShouldThrowArgumentException_WhenRangeExceedsStringLength()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hi", publicKey);

        var act = () => _component.ReplaceInString(encrypted, 1, "xyz");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*exceeds string length*");
    }

    [Fact]
    public void MultiplyEncrypted_ShouldThrowArgumentException_WhenScalarIsNegative()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };
        var encrypted = _component.Encrypt(5L, publicKey);

        var act = () => _component.MultiplyEncrypted(encrypted, -1L);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*non-negative*");
    }

    [Theory]
    [InlineData(100L, 37L, 63L)]
    [InlineData(42L, 42L, 0L)]
    [InlineData(1L, 0L, 1L)]
    public void SubtractEncrypted_ShouldDecryptToDifference_WhenAGreaterThanOrEqualToB(long a, long b, long expected)
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encA = _component.Encrypt(a, publicKey);
        var encB = _component.Encrypt(b, publicKey);
        var difference = _component.SubtractEncrypted(encA, encB);
        var decrypted = _component.Decrypt(difference, key);

        decrypted.Should().Be(new BigInteger(expected));
    }

    [Fact]
    public void SubtractEncrypted_ShouldReturnEncryptedNumberWithSameN()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encA = _component.Encrypt(10L, publicKey);
        var encB = _component.Encrypt(3L, publicKey);
        var difference = _component.SubtractEncrypted(encA, encB);

        difference.N.Should().Be(key.N);
    }

    [Fact]
    public void SubtractEncrypted_ShouldThrowArgumentException_WhenPublicKeysDoNotMatch()
    {
        var key1 = TestKey.Value;
        var key2 = SecondTestKey.Value;

        var encA = _component.Encrypt(10L, new PaillierPublicKey { N = key1.N });
        var encB = _component.Encrypt(3L, new PaillierPublicKey { N = key2.N });

        var act = () => _component.SubtractEncrypted(encA, encB);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*same public key*");
    }

    [Fact]
    public void Encrypt_ShouldThrowArgumentException_WhenPlaintextIsNegative()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var act = () => _component.Encrypt(-1L, publicKey);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*non-negative*");
    }

    [Fact]
    public void AddEncrypted_ShouldThrowArgumentException_WhenPublicKeysDoNotMatch()
    {
        var key1 = TestKey.Value;
        var key2 = SecondTestKey.Value;

        var encrypted1 = _component.Encrypt(5L, new PaillierPublicKey { N = key1.N });
        var encrypted2 = _component.Encrypt(3L, new PaillierPublicKey { N = key2.N });

        var act = () => _component.AddEncrypted(encrypted1, encrypted2);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*same public key*");
    }

    [Fact]
    public void GenerateKey_ShouldThrowArgumentException_WhenBitLengthIsBelowSecurityMinimum()
    {
        var act = () => _component.GenerateKey(HomomorphicEncryptionComponent.MinimumKeyBitLength - 8);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least 2048 bits*");
    }

    [Fact]
    public void SubstringEncrypted_ShouldReturnCorrectSlice_WhenStartAndLengthProvided()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello world", publicKey);
        var substring = _component.SubstringEncrypted(encrypted, 6, 5);
        var decrypted = _component.DecryptString(substring, key);

        decrypted.Should().Be("world");
    }

    [Fact]
    public void SubstringEncrypted_ShouldReturnRemainderOfString_WhenLengthIsOmitted()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello world", publicKey);
        var substring = _component.SubstringEncrypted(encrypted, 6);
        var decrypted = _component.DecryptString(substring, key);

        decrypted.Should().Be("world");
    }

    [Fact]
    public void SubstringEncrypted_ShouldReturnFullString_WhenStartIsZeroAndLengthIsOmitted()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello", publicKey);
        var substring = _component.SubstringEncrypted(encrypted, 0);
        var decrypted = _component.DecryptString(substring, key);

        decrypted.Should().Be("hello");
    }

    [Fact]
    public void SubstringEncrypted_ShouldReturnEmptyString_WhenLengthIsZero()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello", publicKey);
        var substring = _component.SubstringEncrypted(encrypted, 2, 0);

        substring.Characters.Should().BeEmpty();
    }

    [Fact]
    public void SubstringEncrypted_ShouldReturnSingleCharacter_WhenLengthIsOne()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello", publicKey);
        var substring = _component.SubstringEncrypted(encrypted, 1, 1);
        var decrypted = _component.DecryptString(substring, key);

        decrypted.Should().Be("e");
    }

    [Fact]
    public void SubstringEncrypted_ShouldNotShareCharacterReferences_WithOriginal()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello", publicKey);
        var substring = _component.SubstringEncrypted(encrypted, 0, 3);

        substring.Characters.Should().HaveCount(3);
        substring.Characters[0].Should().BeSameAs(encrypted.Characters[0]);
    }

    [Fact]
    public void SubstringEncrypted_ShouldThrowArgumentException_WhenStartIsNegative()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello", publicKey);

        var act = () => _component.SubstringEncrypted(encrypted, -1);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*non-negative*");
    }

    [Fact]
    public void SubstringEncrypted_ShouldThrowArgumentException_WhenRangeExceedsStringLength()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello", publicKey);

        var act = () => _component.SubstringEncrypted(encrypted, 3, 5);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*exceeds string length*");
    }

    [Fact]
    public void SubstringEncrypted_ShouldThrowArgumentException_WhenLengthIsNegative()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hello", publicKey);

        var act = () => _component.SubstringEncrypted(encrypted, 0, -1);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*non-negative*");
    }
}
