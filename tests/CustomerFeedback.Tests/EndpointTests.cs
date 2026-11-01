using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerFeedback.Analytics.Api.Contracts;
using CustomerFeedback.Analytics.Api.Endpoints;
using CustomerFeedback.Analytics.Api.Services;
using CustomerFeedback.Feedback.Api.Contracts;
using CustomerFeedback.Feedback.Api.Endpoints;
using CustomerFeedback.Feedback.Api.Services;
using CustomerFeedback.Identity.Api.Contracts;
using CustomerFeedback.Identity.Api.Endpoints;
using CustomerFeedback.Identity.Api.Services;
using CustomerFeedback.Shared.Domain;
using CustomerFeedback.Shared.Errors;
using CustomerFeedback.Shared.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace CustomerFeedback.Tests;

/// <summary>Exercises the real HTTP pipeline (JWT, role policies, exception handlers, routing) with mocked services.</summary>
public sealed class EndpointTests : IAsyncLifetime
{
    private const string Key = "unit-test-signing-key-0123456789-abcdef";

    private readonly Mock<IAuthService> _auth = new();
    private readonly Mock<IFeedbackService> _feedback = new();
    private readonly Mock<ISummaryService> _summary = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = Key, ["Jwt:Issuer"] = "iss", ["Jwt:Audience"] = "aud",
        });
        builder.Services.AddStandardExceptionHandling();
        builder.Services.AddJwtAuthentication(builder.Configuration);
        builder.Services.AddSingleton(_auth.Object);
        builder.Services.AddSingleton(_feedback.Object);
        builder.Services.AddSingleton(_summary.Object);

        _app = builder.Build();
        _app.UseExceptionHandler();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapAuthEndpoints();
        _app.MapFeedbackEndpoints();
        _app.MapAnalyticsEndpoints();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private HttpRequestMessage Request(HttpMethod method, string url, string? role, object? body = null, int userId = 5)
    {
        var request = new HttpRequestMessage(method, url);
        if (role is not null)
        {
            var token = new JwtTokenService(Options.Create(new JwtOptions { Key = Key, Issuer = "iss", Audience = "aud" }), TimeProvider.System)
                .CreateToken(userId, "u@x.com", role);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static readonly SubmitFeedbackRequest Submission = new(1, 4, "ok", null, false, null);

    [Fact]
    public async Task Protected_endpoints_reject_anonymous_callers()
    {
        foreach (var url in new[] { "/api/feedback/mine", "/api/stores", "/api/categories", "/api/analytics/stores", "/api/auth/me" })
        {
            (await _client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
        }
    }

    [Fact]
    public async Task Role_policies_keep_customers_and_managers_apart()
    {
        (await _client.SendAsync(Request(HttpMethod.Get, "/api/analytics/stores/1/summary", RoleNames.Customer)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.SendAsync(Request(HttpMethod.Post, "/api/feedback", RoleNames.StoreManager, Submission)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.SendAsync(Request(HttpMethod.Get, "/api/feedback/mine", RoleNames.StoreManager)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Customer_can_submit_feedback_and_the_user_id_comes_from_the_token()
    {
        _feedback.Setup(f => f.SubmitAsync(42, It.IsAny<SubmitFeedbackRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FeedbackReceipt(9, DateTime.UtcNow));

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/feedback", RoleNames.Customer, Submission, userId: 42));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().EndWith("/api/feedback/9");
    }

    [Fact]
    public async Task Customer_can_read_own_feedback_and_reference_data()
    {
        _feedback.Setup(f => f.GetMyFeedbackAsync(5, 2, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<MyFeedbackDto>([], 2, 3, 0));
        _feedback.Setup(f => f.GetStoresAsync(It.IsAny<CancellationToken>())).ReturnsAsync([new StoreDto(1, "DEL", "Delhi", null)]);
        _feedback.Setup(f => f.GetCategoriesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([new CategoryDto(1, "Pricing", null)]);

        (await _client.SendAsync(Request(HttpMethod.Get, "/api/feedback/mine?page=2&pageSize=3", RoleNames.Customer))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.SendAsync(Request(HttpMethod.Get, "/api/stores", RoleNames.StoreManager))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.SendAsync(Request(HttpMethod.Get, "/api/categories", RoleNames.Customer))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Validation_failures_become_standard_problem_responses()
    {
        _feedback.Setup(f => f.SubmitAsync(It.IsAny<int>(), It.IsAny<SubmitFeedbackRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestValidationException(new Dictionary<string, string[]> { ["overallRating"] = ["Rating must be between 1 and 5."] }));

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/api/feedback", RoleNames.Customer, Submission));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("errors").GetProperty("overallRating")[0].GetString().Should().Contain("1 and 5");
    }

    [Fact]
    public async Task Unexpected_exceptions_return_a_generic_500()
    {
        _feedback.Setup(f => f.GetStoresAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db password is hunter2"));

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/stores", RoleNames.Customer));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("hunter2");
    }

    [Fact]
    public async Task Manager_summary_passes_the_store_window_and_manager_id_to_the_service()
    {
        _summary.Setup(s => s.GetSummaryAsync(7, 1, 90, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreSummaryDto(1, "Delhi", 90, 10, 5, 4m, 3m, 1m, "improving", [], [], []));
        _summary.Setup(s => s.GetStoresAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _summary.Setup(s => s.GetRecentFeedbackAsync(7, 1, 4, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        (await _client.SendAsync(Request(HttpMethod.Get, "/api/analytics/stores/1/summary?days=90", RoleNames.StoreManager, userId: 7)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.SendAsync(Request(HttpMethod.Get, "/api/analytics/stores", RoleNames.StoreManager, userId: 7)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.SendAsync(Request(HttpMethod.Get, "/api/analytics/stores/1/feedback/recent?count=4", RoleNames.StoreManager, userId: 7)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Store_access_denied_by_the_service_is_a_403_problem()
    {
        _summary.Setup(s => s.GetSummaryAsync(7, 2, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("You are not assigned to this store."));

        var response = await _client.SendAsync(Request(HttpMethod.Get, "/api/analytics/stores/2/summary", RoleNames.StoreManager, userId: 7));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("forbidden");
    }

    [Fact]
    public async Task Auth_endpoints_register_login_and_return_the_profile()
    {
        var user = new UserDto(5, "u@x.com", "U", "X", RoleNames.Customer);
        _auth.Setup(a => a.RegisterAsync(It.IsAny<RegisterRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _auth.Setup(a => a.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthResponse("jwt", DateTime.UtcNow.AddHours(1), user));
        _auth.Setup(a => a.GetProfileAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        (await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("u@x.com", "Passw0rdOK1", "U", "X", null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("u@x.com", "x"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.SendAsync(Request(HttpMethod.Get, "/api/auth/me", RoleNames.Customer))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Locked_accounts_surface_as_423()
    {
        _auth.Setup(a => a.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AccountLockedException(DateTime.UtcNow.AddMinutes(10)));

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("u@x.com", "x"));

        ((int)response.StatusCode).Should().Be(423);
    }

    [Fact]
    public async Task Tokens_with_the_wrong_signature_are_rejected()
    {
        var forged = new JwtTokenService(Options.Create(new JwtOptions { Key = "a-completely-different-signing-key-123456", Issuer = "iss", Audience = "aud" }), TimeProvider.System)
            .CreateToken(1, "evil@x.com", RoleNames.StoreManager);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/analytics/stores");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forged.Value);

        (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
