using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CustomerFeedback.Shared.Domain;
using CustomerFeedback.Shared.Errors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CustomerFeedback.Shared.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "CustomerFeedback.Identity";
    public string Audience { get; set; } = "CustomerFeedback.Clients";

    /// <summary>HMAC-SHA256 signing key (min 32 chars). Supplied through user-secrets or environment variables.</summary>
    public string Key { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 60;
}

public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);

public interface IJwtTokenService
{
    AccessToken CreateToken(int userId, string email, string role);
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    public AccessToken CreateToken(int userId, string email, string role)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.ExpiryMinutes);
        var credentials = new SigningCredentials(JwtSupport.SigningKey(_options), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString(CultureInfo.InvariantCulture)),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(ClaimTypes.Role, role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ],
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

public static class Policies
{
    public const string Customer = "CustomerOnly";
    public const string StoreManager = "StoreManagerOnly";
}

public static class JwtSupport
{
    public static SymmetricSecurityKey SigningKey(JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Key) || options.Key.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key is missing or shorter than 32 characters. Run scripts/setup-dev-secrets.ps1 or set the Jwt__Key environment variable.");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key));
    }

    /// <summary>Registers JWT bearer authentication plus the role policies shared by all services.</summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var key = SigningKey(jwt);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Customer, p => p.RequireRole(RoleNames.Customer))
            .AddPolicy(Policies.StoreManager, p => p.RequireRole(RoleNames.StoreManager));

        return services;
    }
}

public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : throw new UnauthorizedException("Token does not contain a valid subject.");
    }
}
