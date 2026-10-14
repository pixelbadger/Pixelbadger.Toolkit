using System.Numerics;
using System.Security.Cryptography;
using Pixelbadger.Toolkit.Models;

namespace Pixelbadger.Toolkit.Components;

public class HomomorphicEncryptionComponent
{
    public const int DefaultKeyBitLength = 2048;
    public const int MinimumKeyBitLength = 2048;

    public PaillierKeyPair GenerateKey(int bitLength = DefaultKeyBitLength)
    {
        ValidateKeyBitLength(bitLength);

        using var rng = RandomNumberGenerator.Create();
        BigInteger p, q;
        do
        {
            p = GeneratePrime(bitLength / 2, rng);
            q = GeneratePrime(bitLength / 2, rng);
        } while (p == q || (p * q).GetBitLength() < bitLength);

        var n = p * q;
        var lambda = Lcm(p - 1, q - 1);
        var mu = ModInverse(lambda, n);

        return new PaillierKeyPair
        {
            N = n.ToString(),
            Lambda = lambda.ToString(),
            Mu = mu.ToString()
        };
    }

    public EncryptedNumber Encrypt(long plaintext, PaillierPublicKey publicKey)
    {
        if (plaintext < 0)
            throw new ArgumentException("Plaintext must be non-negative.", nameof(plaintext));

        var n = ParseModulus(publicKey.N, nameof(publicKey));
        var m = new BigInteger(plaintext);

        if (m >= n)
            throw new ArgumentException("Plaintext must be less than n.", nameof(plaintext));

        var nSquared = n * n;
        return new EncryptedNumber
        {
            Ciphertext = EncryptCore(m, n, nSquared).ToString(),
            N = n.ToString()
        };
    }

    public BigInteger Decrypt(EncryptedNumber encryptedNumber, PaillierKeyPair key)
    {
        try
        {
            var ctx = DecryptionContext.Create(key);
            return DecryptCore(encryptedNumber, ctx);
        }
        catch
        {
            throw DecryptionFailed();
        }
    }

    public const int MaxStringLength = 100;

    public EncryptedString EncryptString(string plaintext, PaillierPublicKey publicKey)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var runes = plaintext.EnumerateRunes().Select(r => r.Value).ToArray();
        if (runes.Length > MaxStringLength)
            throw new ArgumentException($"String must be at most {MaxStringLength} characters.", nameof(plaintext));
        if (Array.IndexOf(runes, 0) >= 0)
            throw new ArgumentException("String must not contain the NUL character (U+0000), which is reserved for padding.", nameof(plaintext));

        var n = ParseModulus(publicKey.N, nameof(publicKey));
        var nSquared = n * n;
        var nText = n.ToString();

        var result = new EncryptedNumber[MaxStringLength];
        Parallel.For(0, MaxStringLength, i =>
        {
            var m = i < runes.Length ? runes[i] : 0;
            result[i] = new EncryptedNumber { Ciphertext = EncryptCore(m, n, nSquared).ToString(), N = nText };
        });

        return new EncryptedString { Characters = result };
    }

    public string DecryptString(EncryptedString encryptedString, PaillierKeyPair key)
    {
        try
        {
            var chars = encryptedString.Characters;
            if (chars.Length > MaxStringLength)
                throw new CryptographicException();

            var ctx = DecryptionContext.Create(key);
            var values = new int[chars.Length];
            Parallel.For(0, chars.Length, i =>
            {
                var v = DecryptCore(chars[i], ctx);
                if (v < 0 || v > 0x10FFFF || (v >= 0xD800 && v <= 0xDFFF))
                    throw new CryptographicException();
                values[i] = (int)v;
            });

            var sb = new System.Text.StringBuilder();
            foreach (var v in values)
            {
                if (v != 0)
                    sb.Append(char.ConvertFromUtf32(v));
            }
            return sb.ToString();
        }
        catch
        {
            throw DecryptionFailed();
        }
    }

    public EncryptedString SubstringEncrypted(EncryptedString encryptedString, int start, int? length = null)
    {
        var (n, nSquared, cs) = ValidateString(encryptedString, requireNonEmpty: true);
        var count = cs.Length;

        if (start < 0)
            throw new ArgumentException("Start index must be non-negative.", nameof(start));
        if (start > count)
            throw new ArgumentException(
                $"Range [{start}, {start}) exceeds string length {count}.");
        var len = length ?? count - start;
        if (len < 0)
            throw new ArgumentException("Length must be non-negative.", nameof(length));
        if ((long)start + len > count)
            throw new ArgumentException(
                $"Range [{start}, {(long)start + len}) exceeds string length {count}.");

        var nText = n.ToString();
        var result = new EncryptedNumber[MaxStringLength];
        Parallel.For(0, MaxStringLength, i =>
        {
            var c = i < len ? Rerandomise(cs[start + i], n, nSquared) : EncryptCore(BigInteger.Zero, n, nSquared);
            result[i] = new EncryptedNumber { Ciphertext = c.ToString(), N = nText };
        });

        return new EncryptedString { Characters = result };
    }

    public EncryptedString ReplaceInString(EncryptedString encryptedString, int start, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        var (n, nSquared, cs) = ValidateString(encryptedString, requireNonEmpty: true);
        var runes = replacement.EnumerateRunes().Select(r => r.Value).ToArray();

        if (Array.IndexOf(runes, 0) >= 0)
            throw new ArgumentException("Replacement must not contain the NUL character (U+0000), which is reserved for padding.", nameof(replacement));
        if (start < 0 || (long)start + runes.Length > cs.Length)
            throw new ArgumentException(
                $"Replacement range [{start}, {(long)start + runes.Length}) exceeds string length {cs.Length}.");

        var nText = n.ToString();
        var result = new EncryptedNumber[MaxStringLength];
        Parallel.For(0, MaxStringLength, i =>
        {
            BigInteger c;
            if (i >= start && i < start + runes.Length)
                c = EncryptCore(runes[i - start], n, nSquared);
            else if (i < cs.Length)
                c = Rerandomise(cs[i], n, nSquared);
            else
                c = EncryptCore(BigInteger.Zero, n, nSquared);
            result[i] = new EncryptedNumber { Ciphertext = c.ToString(), N = nText };
        });

        return new EncryptedString { Characters = result };
    }

    public EncryptedNumber MultiplyEncrypted(EncryptedNumber a, long scalar)
    {
        if (scalar < 0)
            throw new ArgumentException("Scalar must be non-negative.", nameof(scalar));

        var (n, c) = ValidateCiphertext(a, "ciphertext");
        var nSquared = n * n;
        var product = BigInteger.ModPow(c, new BigInteger(scalar), nSquared);

        return new EncryptedNumber
        {
            Ciphertext = Rerandomise(product, n, nSquared).ToString(),
            N = n.ToString()
        };
    }

    public EncryptedNumber SubtractEncrypted(EncryptedNumber a, EncryptedNumber b)
    {
        var (n, ca, cb) = ValidatePair(a, b);
        var nSquared = n * n;

        var diff = ca * ModInverse(cb, nSquared) % nSquared;
        return new EncryptedNumber
        {
            Ciphertext = Rerandomise(diff, n, nSquared).ToString(),
            N = n.ToString()
        };
    }

    public EncryptedNumber AddEncrypted(EncryptedNumber a, EncryptedNumber b)
    {
        var (n, ca, cb) = ValidatePair(a, b);
        var nSquared = n * n;

        return new EncryptedNumber
        {
            Ciphertext = Rerandomise(ca * cb % nSquared, n, nSquared).ToString(),
            N = n.ToString()
        };
    }

    private static CryptographicException DecryptionFailed() =>
        new("Decryption failed: input is invalid or was not encrypted with this key.");

    private sealed record DecryptionContext(BigInteger N, BigInteger NSquared, BigInteger Lambda, BigInteger Mu)
    {
        public static DecryptionContext Create(PaillierKeyPair key)
        {
            var n = BigInteger.Parse(key.N, System.Globalization.CultureInfo.InvariantCulture);
            ValidateModulus(n);
            var lambda = BigInteger.Parse(key.Lambda, System.Globalization.CultureInfo.InvariantCulture);
            var mu = BigInteger.Parse(key.Mu, System.Globalization.CultureInfo.InvariantCulture);
            return new DecryptionContext(n, n * n, lambda, mu);
        }
    }

    // Throws on any problem; callers translate to the uniform decryption failure.
    private static BigInteger DecryptCore(EncryptedNumber encrypted, DecryptionContext ctx)
    {
        if (!TryParseInteger(encrypted.N, out var cn) || cn != ctx.N)
            throw new CryptographicException();
        if (!TryParseInteger(encrypted.Ciphertext, out var c)
            || c <= 0 || c >= ctx.NSquared || BigInteger.GreatestCommonDivisor(c, ctx.N) != 1)
            throw new CryptographicException();

        var x = BigInteger.ModPow(c, ctx.Lambda, ctx.NSquared);
        if ((x - 1) % ctx.N != 0)
            throw new CryptographicException();
        return L(x, ctx.N) * ctx.Mu % ctx.N;
    }

    private static bool TryParseInteger(string? text, out BigInteger value)
    {
        value = default;
        return text is not null && BigInteger.TryParse(text,
            System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static BigInteger ParseModulus(string? text, string paramName)
    {
        if (!TryParseInteger(text, out var n))
            throw new ArgumentException("Modulus N is not a valid integer.", paramName);
        ValidateModulus(n);
        return n;
    }

    // Validates a single ciphertext and returns its modulus and value.
    private static (BigInteger N, BigInteger C) ValidateCiphertext(EncryptedNumber? e, string what)
    {
        if (e is null)
            throw new ArgumentException($"The {what} is missing.");
        var n = ParseModulus(e.N, what);
        return (n, ValidateCiphertextValue(e, n, what));
    }

    private static BigInteger ValidateCiphertextValue(EncryptedNumber e, BigInteger n, string what)
    {
        if (!TryParseInteger(e.Ciphertext, out var c))
            throw new ArgumentException($"The {what} is not a valid integer.");
        if (c <= 0 || c >= n * n)
            throw new ArgumentException($"The {what} is out of range: it must satisfy 0 < c < n^2.");
        if (BigInteger.GreatestCommonDivisor(c, n) != 1)
            throw new ArgumentException($"The {what} is invalid: it must be coprime to n.");
        return c;
    }

    private static (BigInteger N, BigInteger A, BigInteger B) ValidatePair(EncryptedNumber a, EncryptedNumber b)
    {
        var (na, ca) = ValidateCiphertext(a, "first ciphertext");
        var (nb, cb) = ValidateCiphertext(b, "second ciphertext");
        if (na != nb)
            throw new ArgumentException("Both encrypted numbers must use the same public key (N).");
        return (na, ca, cb);
    }

    private static (BigInteger N, BigInteger NSquared, BigInteger[] Values) ValidateString(
        EncryptedString? s, bool requireNonEmpty)
    {
        if (s?.Characters is null)
            throw new ArgumentException("Encrypted string has no characters array.");
        var chars = s.Characters;
        if (chars.Length > MaxStringLength)
            throw new ArgumentException(
                $"Encrypted string has {chars.Length} entries; at most {MaxStringLength} are allowed.");
        if (requireNonEmpty && chars.Length == 0)
            throw new ArgumentException(
                "Encrypted string is empty; it carries no modulus N to pad from. Encrypt the string with encrypt-string first.");

        BigInteger n = default;
        var values = new BigInteger[chars.Length];
        for (var i = 0; i < chars.Length; i++)
        {
            var what = $"ciphertext at index {i}";
            if (chars[i] is null)
                throw new ArgumentException($"The {what} is missing.");
            var ni = ParseModulus(chars[i].N, what);
            if (i == 0)
                n = ni;
            else if (ni != n)
                throw new ArgumentException($"The {what} uses a different modulus N from the first character; all characters must share one N.");
            values[i] = ValidateCiphertextValue(chars[i], n, what);
        }
        return (n, n * n, values);
    }

    // g = n + 1, so g^m mod n^2 = 1 + m*n mod n^2.
    private static BigInteger EncryptCore(BigInteger m, BigInteger n, BigInteger nSquared)
    {
        var gm = (1 + m * n) % nSquared;
        return gm * RandomizerPower(n, nSquared) % nSquared;
    }

    private static BigInteger Rerandomise(BigInteger c, BigInteger n, BigInteger nSquared) =>
        c * RandomizerPower(n, nSquared) % nSquared;

    private static BigInteger RandomizerPower(BigInteger n, BigInteger nSquared)
    {
        var r = RandomUnit(n);
        return BigInteger.ModPow(r, n, nSquared);
    }

    // Uniform r in [1, n) with gcd(r, n) = 1; uses static thread-safe RNG calls.
    private static BigInteger RandomUnit(BigInteger n)
    {
        var bitLength = (int)n.GetBitLength();
        var byteCount = (bitLength + 7) / 8;
        var excessBits = byteCount * 8 - bitLength;
        var bytes = new byte[byteCount];
        while (true)
        {
            RandomNumberGenerator.Fill(bytes);
            if (excessBits > 0)
                bytes[^1] &= (byte)(0xFF >> excessBits);
            var r = new BigInteger(bytes, isUnsigned: true);
            if (r >= 1 && r < n && BigInteger.GreatestCommonDivisor(r, n) == 1)
                return r;
        }
    }

    private static BigInteger L(BigInteger x, BigInteger n) => (x - 1) / n;

    private static void ValidateKeyBitLength(int bitLength)
    {
        if (bitLength < MinimumKeyBitLength)
            throw new ArgumentException($"Paillier keys must be at least {MinimumKeyBitLength} bits.", nameof(bitLength));

        if (bitLength % 2 != 0 || bitLength % 8 != 0)
            throw new ArgumentException("Paillier key bit length must be divisible by 8 and 2.", nameof(bitLength));
    }

    private static void ValidateModulus(BigInteger n)
    {
        if (n.Sign <= 0 || n.IsEven || n.GetBitLength() < MinimumKeyBitLength)
            throw new ArgumentException(
                $"Paillier modulus must be a positive odd integer of at least {MinimumKeyBitLength} bits.");
    }

    private static BigInteger Lcm(BigInteger a, BigInteger b) =>
        a / BigInteger.GreatestCommonDivisor(a, b) * b;

    private static BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        BigInteger oldR = ((a % m) + m) % m, r = m;
        BigInteger oldS = BigInteger.One, s = BigInteger.Zero;

        while (r != 0)
        {
            var q = oldR / r;
            (oldR, r) = (r, oldR - q * r);
            (oldS, s) = (s, oldS - q * s);
        }

        if (oldR != 1)
            throw new InvalidOperationException("Modular inverse does not exist.");

        return (oldS % m + m) % m;
    }

    private static BigInteger GeneratePrime(int bitLength, RandomNumberGenerator rng)
    {
        var byteCount = bitLength / 8;
        var bytes = new byte[byteCount];
        while (true)
        {
            rng.GetBytes(bytes);
            bytes[^1] |= 0x80; // Ensure MSB set so candidate has exactly bitLength bits
            bytes[0] |= 0x01;  // Ensure odd
            var candidate = new BigInteger(bytes, isUnsigned: true);
            if (IsProbablePrime(candidate, 20, rng))
                return candidate;
        }
    }

    private static bool IsProbablePrime(BigInteger n, int witnesses, RandomNumberGenerator rng)
    {
        if (n < 2) return false;
        if (n == 2 || n == 3) return true;
        if (n.IsEven) return false;

        var d = n - 1;
        var r = 0;
        while (d.IsEven)
        {
            d >>= 1;
            r++;
        }

        var byteCount = n.GetByteCount(isUnsigned: true);
        var bytes = new byte[byteCount];

        for (var i = 0; i < witnesses; i++)
        {
            BigInteger a;
            do
            {
                rng.GetBytes(bytes);
                a = new BigInteger(bytes, isUnsigned: true) % n;
            } while (a < 2 || a >= n - 2);

            var x = BigInteger.ModPow(a, d, n);
            if (x == 1 || x == n - 1) continue;

            var composite = true;
            for (var j = 0; j < r - 1; j++)
            {
                x = BigInteger.ModPow(x, 2, n);
                if (x == n - 1)
                {
                    composite = false;
                    break;
                }
            }

            if (composite) return false;
        }

        return true;
    }
}
