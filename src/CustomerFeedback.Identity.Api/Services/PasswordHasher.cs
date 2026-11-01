using System.Security.Cryptography;

namespace CustomerFeedback.Identity.Api.Services;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string storedHash);
}

/// <summary>PBKDF2-HMAC-SHA256 with a per-password random salt. Stored as $pbkdf2-sha256$iterations$salt$hash.</summary>
public sealed class Pbkdf2PasswordHasher(int iterations = Pbkdf2PasswordHasher.DefaultIterations) : IPasswordHasher
{
    public const int DefaultIterations = 600_000;
    private const string Scheme = "pbkdf2-sha256";
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return $"${Scheme}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[0] != Scheme
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var iter))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
