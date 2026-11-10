using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SemihBener.Abstractions;

public sealed record TokenResult(string AccessToken, DateTime ExpiresAtUtc);

/// <summary>
/// Plugin'e özel JWT üretici. Generic parametre sadece ayırt edici işarettir;
/// her plugin kendi anahtarıyla imzalar, token'lar başka plugin'de geçmez.
/// </summary>
public sealed class JwtTokenIssuer<TMarker>(JwtSettings settings)
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials =
        new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)), SecurityAlgorithms.HmacSha256);

    public TokenResult CreateToken(int userId, string userName, IDictionary<string, object>? extraClaims = null)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(settings.ExpiryMinutes);

        var claims = new Dictionary<string, object>
        {
            ["sub"] = userId.ToString(),
            ["name"] = userName,
            ["jti"] = Guid.NewGuid().ToString("N")
        };

        if (extraClaims is not null)
        {
            foreach (var (key, value) in extraClaims)
            {
                claims[key] = value;
            }
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = _credentials,
            Claims = claims
        });

        return new TokenResult(token, expires);
    }
}

/// <summary>PBKDF2-SHA256 şifre hash'i. Format: iterasyon.salt.hash (base64).</summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirst("sub")?.Value;
        return int.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("Token içinde geçerli bir 'sub' claim'i yok.");
    }
}
