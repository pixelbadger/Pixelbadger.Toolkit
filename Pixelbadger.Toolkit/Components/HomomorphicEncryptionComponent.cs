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

        var n = BigInteger.Parse(publicKey.N);
        ValidateModulus(n);
        var m = new BigInteger(plaintext);

        if (m >= n)
            throw new ArgumentException("Plaintext must be less than n.", nameof(plaintext));

        var nSquared = n * n;
        var g = n + 1;

        using var rng = RandomNumberGenerator.Create();
        BigInteger r;
        do
        {
            r = GenerateRandomBigInteger(n, rng);
        } while (BigInteger.GreatestCommonDivisor(r, n) != 1);

        var ciphertext = BigInteger.ModPow(g, m, nSquared) * BigInteger.ModPow(r, n, nSquared) % nSquared;

        return new EncryptedNumber
        {
            Ciphertext = ciphertext.ToString(),
            N = publicKey.N
        };
    }

    public BigInteger Decrypt(EncryptedNumber encryptedNumber, PaillierKeyPair key)
    {
        var n = BigInteger.Parse(key.N);
        ValidateModulus(n);
        var lambda = BigInteger.Parse(key.Lambda);
        var mu = BigInteger.Parse(key.Mu);
        var c = BigInteger.Parse(encryptedNumber.Ciphertext);
        var nSquared = n * n;

        var x = BigInteger.ModPow(c, lambda, nSquared);
        return L(x, n) * mu % n;
    }

    public EncryptedNumber AddEncrypted(EncryptedNumber a, EncryptedNumber b)
    {
        if (a.N != b.N)
            throw new ArgumentException("Both encrypted numbers must use the same public key (N).");

        var n = BigInteger.Parse(a.N);
        ValidateModulus(n);
        var nSquared = n * n;
        var c1 = BigInteger.Parse(a.Ciphertext);
        var c2 = BigInteger.Parse(b.Ciphertext);

        return new EncryptedNumber
        {
            Ciphertext = (c1 * c2 % nSquared).ToString(),
            N = a.N
        };
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
        if (n.GetBitLength() < MinimumKeyBitLength)
            throw new ArgumentException($"Paillier modulus must be at least {MinimumKeyBitLength} bits.");
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

    private static BigInteger GenerateRandomBigInteger(BigInteger max, RandomNumberGenerator rng)
    {
        var byteCount = max.GetByteCount(isUnsigned: true);
        var bytes = new byte[byteCount];
        BigInteger result;
        do
        {
            rng.GetBytes(bytes);
            result = new BigInteger(bytes, isUnsigned: true);
        } while (result <= 0 || result >= max);
        return result;
    }
}
