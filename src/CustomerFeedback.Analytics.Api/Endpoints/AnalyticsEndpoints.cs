using CustomerFeedback.Analytics.Api.Services;
using CustomerFeedback.Shared.Security;

namespace CustomerFeedback.Analytics.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics").RequireAuthorization(Policies.StoreManager).WithTags("Analytics");

        group.MapGet("/stores", async (HttpContext http, ISummaryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetStoresAsync(http.User.GetUserId(), ct)));

        group.MapGet("/stores/{storeId:int}/summary", async (int storeId, HttpContext http, ISummaryService svc, CancellationToken ct, int? days) =>
            Results.Ok(await svc.GetSummaryAsync(http.User.GetUserId(), storeId, days, ct)));

        group.MapGet("/stores/{storeId:int}/feedback/recent", async (int storeId, HttpContext http, ISummaryService svc, CancellationToken ct, int count = 10) =>
            Results.Ok(await svc.GetRecentFeedbackAsync(http.User.GetUserId(), storeId, count, ct)));

        group.MapGet("/stores/{storeId:int}/report/monthly", async (int storeId, int year, int month, HttpContext http, ISummaryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetMonthlyReportAsync(http.User.GetUserId(), storeId, year, month, ct)));

        return app;
    }
}
