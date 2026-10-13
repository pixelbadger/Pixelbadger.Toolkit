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

        encrypted.Characters.Should().HaveCount(HomomorphicEncryptionComponent.MaxStringLength);
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

        var replaced = _component.ReplaceInString(encrypted, 2, "X");

        // Other positions decrypt to the same values but are re-randomised (unlinkable).
        _component.DecryptString(replaced, key).Should().Be("abXde");
        for (var i = 0; i < 5; i++)
            replaced.Characters[i].Ciphertext.Should().NotBe(encrypted.Characters[i].Ciphertext);
        AssertNoSharedCiphertexts(encrypted, replaced);
    }

    [Fact]
    public void ReplaceInString_ShouldThrowArgumentException_WhenRangeExceedsStringLength()
    {
        var key = TestKey.Value;
        var publicKey = new PaillierPublicKey { N = key.N };

        var encrypted = _component.EncryptString("hi", publicKey);

        var act = () => _component.ReplaceInString(encrypted, 99, "xy");

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

        substring.Characters.Should().HaveCount(100);
        _component.DecryptString(substring, key).Should().BeEmpty();
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

        substring.Characters.Should().HaveCount(100);
        foreach (var o in encrypted.Characters)
            substring.Characters.Should().NotContain(c => ReferenceEquals(c, o));
        AssertNoSharedCiphertexts(encrypted, substring);
        _component.DecryptString(substring, key).Should().Be("hel");
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

        var act = () => _component.SubstringEncrypted(encrypted, 98, 5);

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

    // ---- Fixtures and helpers ----

    private static readonly Lazy<EncryptedString> HelloWorld = new(() =>
        new HomomorphicEncryptionComponent().EncryptString("hello world", new PaillierPublicKey { N = TestKey.Value.N }));

    private const string DecryptionFailureMessage = "Decryption failed: input is invalid or was not encrypted with this key.";

    private static void AssertNoSharedCiphertexts(EncryptedString input, EncryptedString output)
    {
        var inputs = input.Characters.Select(c => c.Ciphertext).ToHashSet();
        output.Characters.Select(c => c.Ciphertext).Should().NotContain(c => inputs.Contains(c));
    }

    private static EncryptedString Legacy(string text, PaillierKeyPair key)
    {
        var c = new HomomorphicEncryptionComponent();
        var pk = new PaillierPublicKey { N = key.N };
        return new EncryptedString
        {
            Characters = text.EnumerateRunes().Select(r => c.Encrypt(r.Value, pk)).ToArray()
        };
    }

    private void AssertDecryptionFails(Action act) =>
        act.Should().Throw<System.Security.Cryptography.CryptographicException>()
            .Which.Should().Match<System.Security.Cryptography.CryptographicException>(
                e => e.Message == DecryptionFailureMessage && e.InnerException == null);

    // ---- Padding ----

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public void EncryptString_ShouldReturnExactlyHundredCharacters_ForVariousLengths(int length)
    {
        var key = TestKey.Value;
        var text = new string('a', length);

        var encrypted = _component.EncryptString(text, new PaillierPublicKey { N = key.N });

        encrypted.Characters.Should().HaveCount(100);
        _component.DecryptString(encrypted, key).Should().Be(text);
    }

    [Fact]
    public void EncryptString_ShouldPadWithDistinctCiphertexts_WhenInputIsEmpty()
    {
        var key = TestKey.Value;

        var encrypted = _component.EncryptString("", new PaillierPublicKey { N = key.N });

        encrypted.Characters.Select(c => c.Ciphertext).Distinct().Should().HaveCount(100);
        _component.Decrypt(encrypted.Characters[99], key).Should().Be(BigInteger.Zero);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("héllo wörld")]
    [InlineData("👋🌍")]
    [InlineData("")]
    public void EncryptString_ThenDecryptString_ShouldRoundTrip_ForAsciiMultiByteAstralAndEmpty(string text)
    {
        var key = TestKey.Value;

        var encrypted = _component.EncryptString(text, new PaillierPublicKey { N = key.N });

        _component.DecryptString(encrypted, key).Should().Be(text);
    }

    [Fact]
    public void EncryptString_ShouldAccept100RuneAstralString_WhichExceeds100Utf16Units()
    {
        var key = TestKey.Value;
        var text = string.Concat(Enumerable.Repeat("👋", 100));

        var encrypted = _component.EncryptString(text, new PaillierPublicKey { N = key.N });

        encrypted.Characters.Should().HaveCount(100);
        _component.DecryptString(encrypted, key).Should().Be(text);
    }

    [Fact]
    public void EncryptString_ShouldThrowArgumentException_WhenMoreThan100Runes()
    {
        var key = TestKey.Value;
        var text = string.Concat(Enumerable.Repeat("👋", 101));

        var act = () => _component.EncryptString(text, new PaillierPublicKey { N = key.N });

        act.Should().Throw<ArgumentException>().WithMessage("*100 characters*");
    }

    [Fact]
    public void EncryptString_ShouldThrowArgumentException_WhenStringContainsNul()
    {
        var key = TestKey.Value;

        var act = () => _component.EncryptString("ab\0cd", new PaillierPublicKey { N = key.N });

        act.Should().Throw<ArgumentException>().WithMessage("*NUL*");
    }

    [Fact]
    public void ReplaceInString_ShouldThrowArgumentException_WhenReplacementContainsNul()
    {
        var act = () => _component.ReplaceInString(HelloWorld.Value, 0, "a\0");

        act.Should().Throw<ArgumentException>().WithMessage("*NUL*");
    }

    [Fact]
    public void DecryptString_ShouldRemoveNulPaddingWhereverItAppears()
    {
        var key = TestKey.Value;
        var pk = new PaillierPublicKey { N = key.N };
        var zero = _component.Encrypt(0, pk);
        var a = _component.Encrypt('a', pk);
        var b = _component.Encrypt('b', pk);

        var decrypted = _component.DecryptString(new EncryptedString { Characters = [zero, a, zero, zero, b] }, key);

        decrypted.Should().Be("ab");
    }

    // ---- Replace / Substring behaviour ----

    [Fact]
    public void ReplaceInString_ShouldWriteIntoPadding_WhenStartIsPastRealEnd()
    {
        var key = TestKey.Value;
        var encrypted = _component.EncryptString("hello", new PaillierPublicKey { N = key.N });

        var replaced = _component.ReplaceInString(encrypted, 7, "X");

        replaced.Characters.Should().HaveCount(100);
        _component.DecryptString(replaced, key).Should().Be("helloX");
    }

    [Fact]
    public void ReplaceInString_ShouldAllowEmptyReplacement_AndReRandomiseEverything()
    {
        var key = TestKey.Value;
        var input = HelloWorld.Value;

        var replaced = _component.ReplaceInString(input, 3, "");

        replaced.Characters.Should().HaveCount(100);
        AssertNoSharedCiphertexts(input, replaced);
        _component.DecryptString(replaced, key).Should().Be("hello world");
    }

    [Fact]
    public void ReplaceInString_ShouldThrowArgumentException_WhenStartIsNegative()
    {
        var act = () => _component.ReplaceInString(HelloWorld.Value, -1, "x");

        act.Should().Throw<ArgumentException>().WithMessage("*exceeds string length*");
    }

    [Fact]
    public void ReplaceInString_ShouldThrowArgumentException_WhenRangeExceedsPaddedLength()
    {
        var act = () => _component.ReplaceInString(HelloWorld.Value, 99, "xy");

        act.Should().Throw<ArgumentException>().WithMessage("*exceeds string length*");
    }

    [Fact]
    public void ReplaceInString_ShouldHandleAstralReplacement()
    {
        var key = TestKey.Value;

        var replaced = _component.ReplaceInString(HelloWorld.Value, 0, "👋");

        _component.DecryptString(replaced, key).Should().Be("👋ello world");
    }

    [Fact]
    public void SubstringEncrypted_ShouldPadOutputToHundredAndDecryptCorrectly()
    {
        var key = TestKey.Value;
        var input = HelloWorld.Value;

        var sub = _component.SubstringEncrypted(input, 6, 5);

        sub.Characters.Should().HaveCount(100);
        AssertNoSharedCiphertexts(input, sub);
        _component.DecryptString(sub, key).Should().Be("world");
    }

    [Fact]
    public void SubstringEncrypted_ShouldReachIntoPadding_WhenLengthOmittedOnPaddedString()
    {
        var key = TestKey.Value;

        var sub = _component.SubstringEncrypted(HelloWorld.Value, 6);

        sub.Characters.Should().HaveCount(100);
        _component.DecryptString(sub, key).Should().Be("world");
    }

    // ---- Re-randomisation of numeric operations ----

    [Fact]
    public void AddSubtractMultiply_ShouldReturnFreshCiphertexts_NotEqualToAnyInput()
    {
        var key = TestKey.Value;
        var pk = new PaillierPublicKey { N = key.N };
        var a = _component.Encrypt(10, pk);
        var b = _component.Encrypt(3, pk);
        var inputs = new[] { a.Ciphertext, b.Ciphertext };

        var sum = _component.AddEncrypted(a, b);
        var diff = _component.SubtractEncrypted(a, b);
        var times0 = _component.MultiplyEncrypted(a, 0);
        var times1 = _component.MultiplyEncrypted(a, 1);
        var times3 = _component.MultiplyEncrypted(b, 3);
        var addZero = _component.AddEncrypted(a, _component.Encrypt(0, pk));

        foreach (var r in new[] { sum, diff, times0, times1, times3, addZero })
            inputs.Should().NotContain(r.Ciphertext);
        _component.Decrypt(sum, key).Should().Be(13);
        _component.Decrypt(diff, key).Should().Be(7);
        _component.Decrypt(times0, key).Should().Be(0);
        _component.Decrypt(times1, key).Should().Be(10);
        _component.Decrypt(times3, key).Should().Be(9);
        _component.Decrypt(addZero, key).Should().Be(10);
    }

    [Fact]
    public void MultiplyEncrypted_ShouldReturnDifferentCiphertexts_WhenCalledTwiceWithSameInputs()
    {
        var pk = new PaillierPublicKey { N = TestKey.Value.N };
        var a = _component.Encrypt(4, pk);

        _component.MultiplyEncrypted(a, 1).Ciphertext.Should().NotBe(_component.MultiplyEncrypted(a, 1).Ciphertext);
    }

    // ---- Input validation ----

    private EncryptedNumber Good() => _component.Encrypt(1, new PaillierPublicKey { N = TestKey.Value.N });

    private static EncryptedString WithChars(params EncryptedNumber[] chars) => new() { Characters = chars };

    [Fact]
    public void SubstringAndReplace_ShouldThrowArgumentException_WhenCharactersEmpty()
    {
        var empty = new EncryptedString();

        var sub = () => _component.SubstringEncrypted(empty, 0);
        var rep = () => _component.ReplaceInString(empty, 0, "");

        sub.Should().Throw<ArgumentException>().WithMessage("*empty*");
        rep.Should().Throw<ArgumentException>().WithMessage("*empty*");
    }

    [Fact]
    public void SubstringAndReplace_ShouldThrowArgumentException_WhenMoreThan100Entries()
    {
        var g = Good();
        var big = WithChars(Enumerable.Repeat(g, 101).ToArray());

        var sub = () => _component.SubstringEncrypted(big, 0, 1);
        var rep = () => _component.ReplaceInString(big, 0, "a");

        sub.Should().Throw<ArgumentException>().WithMessage("*at most 100*");
        rep.Should().Throw<ArgumentException>().WithMessage("*at most 100*");
    }

    [Fact]
    public void SubstringAndReplace_ShouldThrowArgumentException_WhenModuliDiffer()
    {
        var other = _component.Encrypt(1, new PaillierPublicKey { N = SecondTestKey.Value.N });
        var mixed = WithChars(Good(), other);

        var sub = () => _component.SubstringEncrypted(mixed, 0, 1);
        var rep = () => _component.ReplaceInString(mixed, 0, "a");

        sub.Should().Throw<ArgumentException>().WithMessage("*share one N*");
        rep.Should().Throw<ArgumentException>().WithMessage("*share one N*");
    }

    [Fact]
    public void Validation_ShouldCompareModuliAsIntegers_NotStrings()
    {
        var key = TestKey.Value;
        var g = Good();
        var variant = new EncryptedNumber { Ciphertext = g.Ciphertext, N = "+" + key.N };

        var sub = _component.SubstringEncrypted(WithChars(g, variant), 0, 2);

        sub.Characters.Should().HaveCount(100);
    }

    [Fact]
    public void Operations_ShouldThrowArgumentException_WhenModulusBelow2048Bits()
    {
        var small = new EncryptedNumber { Ciphertext = "5", N = "15" };
        var good = Good();

        new Action(() => _component.SubstringEncrypted(WithChars(small), 0, 1)).Should().Throw<ArgumentException>().WithMessage("*2048 bits*");
        new Action(() => _component.ReplaceInString(WithChars(small), 0, "a")).Should().Throw<ArgumentException>().WithMessage("*2048 bits*");
        new Action(() => _component.AddEncrypted(small, small)).Should().Throw<ArgumentException>().WithMessage("*2048 bits*");
        new Action(() => _component.SubtractEncrypted(good, small)).Should().Throw<ArgumentException>().WithMessage("*2048 bits*");
        new Action(() => _component.MultiplyEncrypted(small, 2)).Should().Throw<ArgumentException>().WithMessage("*2048 bits*");
    }

    public static IEnumerable<object[]> BadCiphertexts()
    {
        var n = BigInteger.Parse(TestKey.Value.N);
        yield return new object[] { "abc", "*not a valid integer*" };
        yield return new object[] { "", "*not a valid integer*" };
        yield return new object[] { "0", "*out of range*" };
        yield return new object[] { "-5", "*out of range*" };
        yield return new object[] { (n * n).ToString(), "*out of range*" };
        yield return new object[] { (n * 3).ToString(), "*coprime*" };
    }

    [Theory]
    [MemberData(nameof(BadCiphertexts))]
    public void Operations_ShouldThrowArgumentException_WhenCiphertextInvalid(string bad, string expected)
    {
        var key = TestKey.Value;
        var evil = new EncryptedNumber { Ciphertext = bad, N = key.N };
        var good = Good();

        new Action(() => _component.SubstringEncrypted(WithChars(good, evil), 0, 1)).Should().Throw<ArgumentException>().WithMessage(expected);
        new Action(() => _component.ReplaceInString(WithChars(good, evil), 0, "a")).Should().Throw<ArgumentException>().WithMessage(expected);
        new Action(() => _component.AddEncrypted(good, evil)).Should().Throw<ArgumentException>().WithMessage(expected);
        new Action(() => _component.AddEncrypted(evil, good)).Should().Throw<ArgumentException>().WithMessage(expected);
        new Action(() => _component.SubtractEncrypted(good, evil)).Should().Throw<ArgumentException>().WithMessage(expected);
        new Action(() => _component.MultiplyEncrypted(evil, 2)).Should().Throw<ArgumentException>().WithMessage(expected);
    }

    // ---- Decryption failure ----

    [Fact]
    public void Decrypt_ShouldThrowUniformCryptographicException_WhenWrongKey()
    {
        var enc = Good();

        AssertDecryptionFails(() => _component.Decrypt(enc, SecondTestKey.Value));
        AssertDecryptionFails(() => _component.DecryptString(HelloWorld.Value, SecondTestKey.Value));
    }

    [Fact]
    public void Decrypt_ShouldThrowUniformCryptographicException_WhenCiphertextOutOfRangeOrNotCoprime()
    {
        var key = TestKey.Value;
        var n = BigInteger.Parse(key.N);

        foreach (var bad in new[] { "0", "-1", (n * n).ToString(), (n * n + 5).ToString(), (n * 2).ToString(), "xyz", "" })
        {
            var enc = new EncryptedNumber { Ciphertext = bad, N = key.N };
            AssertDecryptionFails(() => _component.Decrypt(enc, key));
            AssertDecryptionFails(() => _component.DecryptString(WithChars(enc), key));
        }
    }

    [Fact]
    public void Decrypt_ShouldThrowUniformCryptographicException_WhenModulusMismatchesOrUnparseable()
    {
        var key = TestKey.Value;
        var good = Good();

        AssertDecryptionFails(() => _component.Decrypt(new EncryptedNumber { Ciphertext = good.Ciphertext, N = SecondTestKey.Value.N }, key));
        AssertDecryptionFails(() => _component.Decrypt(new EncryptedNumber { Ciphertext = good.Ciphertext, N = "nope" }, key));
        AssertDecryptionFails(() => _component.Decrypt(new EncryptedNumber { Ciphertext = good.Ciphertext, N = null! }, key));
    }

    [Fact]
    public void DecryptString_ShouldThrowUniformCryptographicException_WhenMoreThan100Entries()
    {
        var g = Good();

        AssertDecryptionFails(() => _component.DecryptString(WithChars(Enumerable.Repeat(g, 101).ToArray()), TestKey.Value));
    }

    [Fact]
    public void DecryptString_ShouldThrowUniformCryptographicException_WhenValueIsNotUnicodeScalar()
    {
        var key = TestKey.Value;
        var pk = new PaillierPublicKey { N = key.N };

        foreach (var bad in new long[] { 0x110000, 0xD800, 0xDFFF, long.MaxValue })
        {
            var enc = _component.Encrypt(bad, pk);
            AssertDecryptionFails(() => _component.DecryptString(WithChars(_component.Encrypt('a', pk), enc), key));
        }
    }

    [Fact]
    public void DecryptString_ShouldAcceptBoundaryScalars()
    {
        var key = TestKey.Value;
        var pk = new PaillierPublicKey { N = key.N };

        var s = _component.DecryptString(WithChars(
            _component.Encrypt(0xD7FF, pk), _component.Encrypt(0xE000, pk), _component.Encrypt(0x10FFFF, pk)), key);

        s.Should().Be("\uD7FF\uE000\uDBFF\uDFFF");
    }

    [Fact]
    public void Decrypt_ShouldReturnEmptyString_WhenCharactersEmpty()
    {
        _component.DecryptString(new EncryptedString(), TestKey.Value).Should().BeEmpty();
    }

    // ---- Legacy unpadded strings ----

    [Fact]
    public void LegacyUnpaddedString_ShouldDecrypt_AndSubstringAndReplaceShouldProduceHundredEntries()
    {
        var key = TestKey.Value;
        var legacy = Legacy("hello", key);

        _component.DecryptString(legacy, key).Should().Be("hello");

        var sub = _component.SubstringEncrypted(legacy, 1, 3);
        sub.Characters.Should().HaveCount(100);
        _component.DecryptString(sub, key).Should().Be("ell");
        AssertNoSharedCiphertexts(legacy, sub);

        var rep = _component.ReplaceInString(legacy, 3, "p!");
        rep.Characters.Should().HaveCount(100);
        _component.DecryptString(rep, key).Should().Be("help!");
    }
}
