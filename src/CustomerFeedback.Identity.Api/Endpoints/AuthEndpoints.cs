using CustomerFeedback.Identity.Api.Contracts;
using CustomerFeedback.Identity.Api.Services;
using CustomerFeedback.Shared.Security;

namespace CustomerFeedback.Identity.Api.Endpoints;

public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest request, IAuthService auth, CancellationToken ct) =>
            {
                var user = await auth.RegisterAsync(request, ct);
                return Results.Created($"/api/auth/me", user);
            })
            .RequireRateLimiting(RateLimitPolicy)
            .AllowAnonymous();

        group.MapPost("/login", async (LoginRequest request, IAuthService auth, CancellationToken ct) =>
                Results.Ok(await auth.LoginAsync(request, ct)))
            .RequireRateLimiting(RateLimitPolicy)
            .AllowAnonymous();

        group.MapGet("/me", async (HttpContext http, IAuthService auth, CancellationToken ct) =>
                Results.Ok(await auth.GetProfileAsync(http.User.GetUserId(), ct)))
            .RequireAuthorization();

        return app;
    }
}
