using CustomerFeedback.Feedback.Api.Contracts;
using CustomerFeedback.Feedback.Api.Services;
using CustomerFeedback.Shared.Security;

namespace CustomerFeedback.Feedback.Api.Endpoints;

public static class FeedbackEndpoints
{
    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        // Reference data: any signed-in user.
        app.MapGet("/api/stores", async (IFeedbackService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetStoresAsync(ct))).RequireAuthorization().WithTags("Reference");

        app.MapGet("/api/categories", async (IFeedbackService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetCategoriesAsync(ct))).RequireAuthorization().WithTags("Reference");

        // Customer-only: submitting and reviewing one's own feedback.
        var group = app.MapGroup("/api/feedback").RequireAuthorization(Policies.Customer).WithTags("Feedback");

        group.MapPost("/", async (SubmitFeedbackRequest request, HttpContext http, IFeedbackService svc, CancellationToken ct) =>
        {
            var receipt = await svc.SubmitAsync(http.User.GetUserId(), request, ct);
            return Results.Created($"/api/feedback/{receipt.SubmissionId}", receipt);
        });

        group.MapGet("/mine", async (HttpContext http, IFeedbackService svc, CancellationToken ct, int page = 1, int pageSize = 10) =>
            Results.Ok(await svc.GetMyFeedbackAsync(http.User.GetUserId(), page, pageSize, ct)));

        return app;
    }
}
