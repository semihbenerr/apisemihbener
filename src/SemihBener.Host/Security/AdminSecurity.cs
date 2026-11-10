using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace SemihBener.Host.Security;

/// <summary>Panel girişinde deneme limiti: IP başına 5 hatalı denemede 15 dakika kilit.</summary>
public sealed class LoginThrottle(IMemoryCache cache)
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    private sealed class Counter
    {
        public int Failures;
        public DateTime? LockedUntilUtc;
    }

    public bool IsLocked(string ip, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        if (cache.TryGetValue(Key(ip), out Counter? counter) && counter?.LockedUntilUtc is { } until && until > DateTime.UtcNow)
        {
            remaining = until - DateTime.UtcNow;
            return true;
        }

        return false;
    }

    public void RegisterFailure(string ip)
    {
        var counter = cache.GetOrCreate(Key(ip), entry =>
        {
            entry.SlidingExpiration = LockDuration;
            return new Counter();
        })!;

        lock (counter)
        {
            counter.Failures++;
            if (counter.Failures >= MaxFailures)
            {
                counter.LockedUntilUtc = DateTime.UtcNow.Add(LockDuration);
                counter.Failures = 0;
            }
        }
    }

    public void Reset(string ip) => cache.Remove(Key(ip));

    private static string Key(string ip) => $"admin-login:{ip}";
}

/// <summary>RFC 6238 TOTP (Google Authenticator, Microsoft Authenticator vb. ile uyumlu).</summary>
public static class Totp
{
    private const int Digits = 6;
    private const int StepSeconds = 30;

    public static string GenerateSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(20));

    public static bool Verify(string base32Secret, string? code)
    {
        code = code?.Replace(" ", "").Trim();
        if (code is null || code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        var key = Base32.Decode(base32Secret);
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;
        var codeBytes = Encoding.ASCII.GetBytes(code);
        var match = false;

        for (var offset = -1; offset <= 1; offset++)
        {
            var expected = Encoding.ASCII.GetBytes(Compute(key, step + offset));
            match |= CryptographicOperations.FixedTimeEquals(expected, codeBytes);
        }

        return match;
    }

    public static string BuildUri(string base32Secret, string account, string issuer) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={StepSeconds}";

    private static string Compute(byte[] key, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | (hash[offset + 1] << 16)
                     | (hash[offset + 2] << 8)
                     | hash[offset + 3];

        return (binary % 1_000_000).ToString("D6");
    }
}

public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(byte[] data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                output.Append(Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            output.Append(Alphabet[(buffer << (5 - bitsLeft)) & 31]);
        }

        return output.ToString();
    }

    public static byte[] Decode(string text)
    {
        text = text.Trim().TrimEnd('=').ToUpperInvariant().Replace(" ", "");
        var output = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bitsLeft = 0;

        foreach (var c in text)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0)
            {
                throw new FormatException("Geçersiz base32 karakteri.");
            }

            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }

        return output.ToArray();
    }
}
