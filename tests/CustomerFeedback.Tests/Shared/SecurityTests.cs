using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using CustomerFeedback.Shared.Errors;
using CustomerFeedback.Shared.Security;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CustomerFeedback.Tests.Shared;

public class FieldEncryptorTests
{
    private readonly AesGcmFieldEncryptor _sut = TestSupport.Encryptor();

    [Theory]
    [InlineData("hello")]
    [InlineData("")]
    [InlineData("unicode ✓ नमस्ते")]
    public void Encrypt_then_decrypt_round_trips(string plain)
    {
        var cipher = _sut.Encrypt(plain);

        cipher.Should().StartWith("enc1:");
        if (plain.Length > 0)
        {
            cipher.Should().NotContain(plain);
        }
        _sut.Decrypt(cipher).Should().Be(plain);
    }

    [Fact]
    public void Encrypt_uses_a_fresh_nonce_each_time()
    {
        _sut.Encrypt("same").Should().NotBe(_sut.Encrypt("same"));
    }

    [Fact]
    public void Decrypt_rejects_tampered_ciphertext()
    {
        var cipher = _sut.Encrypt("secret");
        var bytes = Convert.FromBase64String(cipher["enc1:".Length..]);
        bytes[^1] ^= 0xFF;

        var act = () => _sut.Decrypt("enc1:" + Convert.ToBase64String(bytes));

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_rejects_plain_text_and_truncated_values()
    {
        var plain = () => _sut.Decrypt("not encrypted");
        var truncated = () => _sut.Decrypt("enc1:" + Convert.ToBase64String(new byte[5]));

        plain.Should().Throw<CryptographicException>();
        truncated.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!")]
    [InlineData("AAAA")]
    public void Constructor_rejects_invalid_keys(string key)
    {
        var act = () => new AesGcmFieldEncryptor(Options.Create(new EncryptionOptions { Key = key }));

        act.Should().Throw<InvalidOperationException>();
    }
}

public class InputSanitizerTests
{
    [Theory]
    [InlineData("<script>alert(1)</script>Hello", "alert(1)Hello")]
    [InlineData("  spaced   out  ", "spaced out")]
    [InlineData("a\u0000b\u0007c", "abc")]
    [InlineData("&lt;b&gt;bold&lt;/b&gt;", "bold")]
    [InlineData("1 < 2 > 0", "1 0")]
    public void Clean_strips_markup_control_chars_and_whitespace(string input, string expected)
    {
        InputSanitizer.Clean(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<br/>")]
    public void Clean_returns_null_for_empty_results(string? input)
    {
        InputSanitizer.Clean(input).Should().BeNull();
    }

    [Fact]
    public void Clean_keeps_newlines_only_when_allowed()
    {
        InputSanitizer.Clean("a\r\nb", allowNewLines: true).Should().Contain("\n");
        InputSanitizer.Clean("a\r\nb").Should().NotContain("\n");
    }

    [Fact]
    public void CleanRequired_and_CleanEmail_normalise()
    {
        InputSanitizer.CleanRequired(null).Should().BeEmpty();
        InputSanitizer.CleanEmail("  User@Example.COM ").Should().Be("user@example.com");
    }
}

public class JwtTests
{
    private static JwtOptions Options(string key = "0123456789abcdef0123456789abcdef") =>
        new() { Key = key, ExpiryMinutes = 30, Issuer = "iss", Audience = "aud" };

    [Fact]
    public void CreateToken_contains_subject_role_and_expiry()
    {
        var clock = TestSupport.Clock();
        var sut = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options()), clock);

        var token = sut.CreateToken(42, "a@b.com", "StoreManager");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);
        jwt.Subject.Should().Be("42");
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role || c.Type == "role");
        jwt.Issuer.Should().Be("iss");
        token.ExpiresAtUtc.Should().Be(TestSupport.Now.UtcDateTime.AddMinutes(30));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public void SigningKey_rejects_weak_keys(string key)
    {
        var act = () => JwtSupport.SigningKey(Options(key));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:Key*");
    }

    [Fact]
    public void GetUserId_reads_subject_claim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, "7")]));

        principal.GetUserId().Should().Be(7);
    }

    [Fact]
    public void GetUserId_throws_when_subject_missing()
    {
        var act = () => new ClaimsPrincipal(new ClaimsIdentity()).GetUserId();

        act.Should().Throw<UnauthorizedException>();
    }
}
