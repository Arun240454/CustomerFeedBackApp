using CustomerFeedback.Feedback.Api.Contracts;
using CustomerFeedback.Shared.Data;
using CustomerFeedback.Shared.Domain;
using CustomerFeedback.Shared.Errors;
using CustomerFeedback.Shared.Messaging;
using CustomerFeedback.Shared.Security;
using CustomerFeedback.Shared.Validation;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CustomerFeedback.Feedback.Api.Services;

public interface IFeedbackService
{
    Task<FeedbackReceipt> SubmitAsync(int userId, SubmitFeedbackRequest request, CancellationToken ct);
    Task<PagedResult<MyFeedbackDto>> GetMyFeedbackAsync(int userId, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<StoreDto>> GetStoresAsync(CancellationToken ct);
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct);
}

public sealed class FeedbackService(
    FeedbackDbContext db,
    IEventPublisher events,
    IValidator<SubmitFeedbackRequest> validator,
    TimeProvider clock,
    ILogger<FeedbackService> logger) : IFeedbackService
{
    public const int MaxPageSize = 50;

    public async Task<FeedbackReceipt> SubmitAsync(int userId, SubmitFeedbackRequest request, CancellationToken ct)
    {
        var clean = request with
        {
            Comments = InputSanitizer.Clean(request.Comments, allowNewLines: true),
            Items = request.Items?
                .Select(i => i with { Comment = InputSanitizer.Clean(i.Comment, allowNewLines: true) })
                .ToList(),
        };
        await validator.ValidateOrThrowAsync(clean, ct);

        if (!await db.Stores.AnyAsync(s => s.StoreId == clean.StoreId && s.IsActive, ct))
        {
            throw new NotFoundException($"Store {clean.StoreId} was not found.");
        }

        var items = clean.Items ?? [];
        if (items.Count > 0)
        {
            var requested = items.Select(i => i.CategoryId).ToList();
            var known = await db.FeedbackCategories.Where(c => c.IsActive && requested.Contains(c.CategoryId))
                .Select(c => c.CategoryId).ToListAsync(ct);
            var unknown = requested.Except(known).ToList();
            if (unknown.Count > 0)
            {
                throw new RequestValidationException(new Dictionary<string, string[]>
                {
                    ["items"] = [$"Unknown category id(s): {string.Join(", ", unknown)}."],
                });
            }
        }

        var submittedAt = clock.GetUtcNow().UtcDateTime;
        long submissionId = 0;

        // The event needs the generated id, so the submission and its outbox row are saved in one transaction:
        // the categorisation event can never be lost, or sent for a submission that was not stored.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            db.ChangeTracker.Clear();
            await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(token) : null;

            var submission = new FeedbackSubmission
            {
                StoreId = clean.StoreId,
                CustomerUserId = clean.IsAnonymous ? null : userId,
                OverallRating = clean.OverallRating,
                Comments = clean.Comments,
                VisitDate = clean.VisitDate,
                SubmittedAtUtc = submittedAt,
                Items = items.Select(i => new FeedbackItem { CategoryId = i.CategoryId, Rating = i.Rating, Comment = i.Comment }).ToList(),
            };
            db.FeedbackSubmissions.Add(submission);
            await db.SaveChangesAsync(token);

            events.Publish(new FeedbackSubmitted(submission.SubmissionId, submission.StoreId, submittedAt));
            await db.SaveChangesAsync(token);

            if (tx is not null)
            {
                await tx.CommitAsync(token);
            }

            submissionId = submission.SubmissionId;
        }, ct);

        logger.LogInformation("Feedback {SubmissionId} stored for store {StoreId} (anonymous: {Anonymous})",
            submissionId, clean.StoreId, clean.IsAnonymous);
        return new FeedbackReceipt(submissionId, submittedAt);
    }

    public async Task<PagedResult<MyFeedbackDto>> GetMyFeedbackAsync(int userId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.FeedbackSubmissions.AsNoTracking().Where(s => s.CustomerUserId == userId);
        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(s => s.SubmittedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new
            {
                s.SubmissionId, s.StoreId, s.OverallRating, s.Comments, s.VisitDate, s.SubmittedAtUtc,
                Items = s.Items.Select(i => new { i.Category!.CategoryName, i.Rating, i.Comment }).ToList(),
            })
            .ToListAsync(ct);

        var storeIds = rows.Select(r => r.StoreId).Distinct().ToList();
        var storeNames = await db.Stores.AsNoTracking().Where(s => storeIds.Contains(s.StoreId))
            .ToDictionaryAsync(s => s.StoreId, s => s.StoreName, ct);

        var items = rows.Select(r => new MyFeedbackDto(
            r.SubmissionId, storeNames.GetValueOrDefault(r.StoreId, "Unknown store"), r.OverallRating, r.Comments,
            r.VisitDate, r.SubmittedAtUtc,
            r.Items.Select(i => new MyFeedbackItemDto(i.CategoryName, i.Rating, i.Comment)).ToList())).ToList();

        return new PagedResult<MyFeedbackDto>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<StoreDto>> GetStoresAsync(CancellationToken ct) =>
        await db.Stores.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.StoreName)
            .Select(s => new StoreDto(s.StoreId, s.StoreCode, s.StoreName, s.City)).ToListAsync(ct);

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct) =>
        await db.FeedbackCategories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder)
            .Select(c => new CategoryDto(c.CategoryId, c.CategoryName, c.Description)).ToListAsync(ct);
}
