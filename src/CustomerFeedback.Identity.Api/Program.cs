using CustomerFeedback.Identity.Api.Contracts;
using System.Threading.RateLimiting;
using CustomerFeedback.Identity.Api.Endpoints;
using CustomerFeedback.Identity.Api.Services;
using CustomerFeedback.Shared.Data;
using CustomerFeedback.Shared.Hosting;
using CustomerFeedback.Shared.Security;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("identity-api");

builder.Services.Configure<LoginSecurityOptions>(builder.Configuration.GetSection(LoginSecurityOptions.SectionName));
builder.Services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<DevelopmentDataSeeder>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<FeedbackDbContext>().Database.MigrateAsync();
    if (app.Configuration.GetValue<bool>("Database:SeedSampleData"))
    {
        await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync(CancellationToken.None);
    }
}

app.UseServiceDefaults();
app.UseRateLimiter();
app.MapAuthEndpoints();

await app.RunAsync();

