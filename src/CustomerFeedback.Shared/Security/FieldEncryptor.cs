using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace CustomerFeedback.Shared.Security;

public class EncryptionOptions
{
    public const string SectionName = "Encryption";

    /// <summary>Base64 encoded 256-bit key. Supplied through user-secrets or an environment variable, never source control.</summary>
    public string Key { get; set; } = string.Empty;
}

public interface IFieldEncryptor
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}

/// <summary>AES-256-GCM authenticated encryption. Output is a version prefix plus base64 of nonce, tag and ciphertext.</summary>
public sealed class AesGcmFieldEncryptor : IFieldEncryptor
{
    private const string Prefix = "enc1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmFieldEncryptor(IOptions<EncryptionOptions> options)
    {
        try
        {
            _key = Convert.FromBase64String(options.Value.Key ?? string.Empty);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Encryption:Key must be a base64 string.", ex);
        }

        if (_key.Length != 32)
        {
            throw new InvalidOperationException(
                "Encryption:Key must decode to exactly 32 bytes. Run scripts/setup-dev-secrets.ps1 or set the Encryption__Key environment variable.");
        }
    }

    public string Encrypt(string plainText)
    {
        var plain = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plain.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var buffer = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(buffer, 0);
        tag.CopyTo(buffer, NonceSize);
        cipher.CopyTo(buffer, NonceSize + TagSize);
        return Prefix + Convert.ToBase64String(buffer);
    }

    public string Decrypt(string cipherText)
    {
        if (!cipherText.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("Value is not an encrypted field.");
        }

        var buffer = Convert.FromBase64String(cipherText[Prefix.Length..]);
        if (buffer.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Encrypted value is truncated.");
        }

        var nonce = buffer.AsSpan(0, NonceSize);
        var tag = buffer.AsSpan(NonceSize, TagSize);
        var cipher = buffer.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
