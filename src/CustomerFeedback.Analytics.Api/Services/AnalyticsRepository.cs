using CustomerFeedback.Analytics.Api.Contracts;
using CustomerFeedback.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace CustomerFeedback.Analytics.Api.Services;

/// <summary>Read-side queries for the manager dashboard. Each query is a single set-based statement served by an index.</summary>
public interface IAnalyticsRepository
{
    Task<IReadOnlyList<ManagedStoreDto>> GetManagedStoresAsync(int managerId, CancellationToken ct);
    Task<ManagedStoreDto?> GetManagedStoreAsync(int storeId, int managerId, CancellationToken ct);
    Task<long> CountAllAsync(int storeId, CancellationToken ct);
    Task<IReadOnlyList<DailyAggregate>> GetDailyAggregatesAsync(int storeId, DateOnly fromDate, DateOnly toDate, CancellationToken ct);
    Task<IReadOnlyList<RatingBucketDto>> GetRatingDistributionAsync(int storeId, DateOnly fromDate, DateOnly toDate, CancellationToken ct);
    Task<IReadOnlyList<CategoryAggregate>> GetTopCategoriesAsync(int storeId, DateOnly fromDate, DateOnly toDate, int top, CancellationToken ct);
    Task<IReadOnlyList<RecentFeedbackDto>> GetRecentFeedbackAsync(int storeId, int count, CancellationToken ct);
}

public sealed class AnalyticsRepository(FeedbackDbContext db) : IAnalyticsRepository
{
    public async Task<IReadOnlyList<ManagedStoreDto>> GetManagedStoresAsync(int managerId, CancellationToken ct) =>
        await (from m in db.StoreManagers.AsNoTracking()
               join s in db.Stores on m.StoreId equals s.StoreId
               where m.UserId == managerId && s.IsActive               // IX_StoreManagers_User
               orderby s.StoreName
               select new ManagedStoreDto(s.StoreId, s.StoreCode, s.StoreName, s.City))
            .ToListAsync(ct);

    public Task<ManagedStoreDto?> GetManagedStoreAsync(int storeId, int managerId, CancellationToken ct) =>
        (from m in db.StoreManagers.AsNoTracking()
         join s in db.Stores on m.StoreId equals s.StoreId
         where m.UserId == managerId && m.StoreId == storeId && s.IsActive   // PK (StoreId, UserId)
         select new ManagedStoreDto(s.StoreId, s.StoreCode, s.StoreName, s.City))
        .SingleOrDefaultAsync(ct);

    public Task<long> CountAllAsync(int storeId, CancellationToken ct) =>
        db.FeedbackSubmissions.LongCountAsync(s => s.StoreId == storeId, ct);

    public async Task<IReadOnlyList<DailyAggregate>> GetDailyAggregatesAsync(int storeId, DateOnly fromDate, DateOnly toDate, CancellationToken ct) =>
        await db.FeedbackSubmissions.AsNoTracking()
            .Where(s => s.StoreId == storeId && s.SubmittedDateUtc >= fromDate && s.SubmittedDateUtc <= toDate)  // IX_FeedbackSubmissions_Store_Date (covering)
            .GroupBy(s => s.SubmittedDateUtc)
            .Select(g => new DailyAggregate(g.Key, g.Count(), g.Sum(s => (int)s.OverallRating)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RatingBucketDto>> GetRatingDistributionAsync(int storeId, DateOnly fromDate, DateOnly toDate, CancellationToken ct) =>
        await db.FeedbackSubmissions.AsNoTracking()
            .Where(s => s.StoreId == storeId && s.SubmittedDateUtc >= fromDate && s.SubmittedDateUtc <= toDate)
            .GroupBy(s => (int)s.OverallRating)
            .Select(g => new RatingBucketDto(g.Key, g.Count()))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CategoryAggregate>> GetTopCategoriesAsync(int storeId, DateOnly fromDate, DateOnly toDate, int top, CancellationToken ct) =>
        await (from i in db.FeedbackItems.AsNoTracking()
               join s in db.FeedbackSubmissions on i.SubmissionId equals s.SubmissionId
               where s.StoreId == storeId && s.SubmittedDateUtc >= fromDate && s.SubmittedDateUtc <= toDate
               group i by new { i.CategoryId, i.Category!.CategoryName } into g
               orderby g.Count() descending, g.Key.CategoryName
               select new CategoryAggregate(g.Key.CategoryId, g.Key.CategoryName, g.Count(), g.Sum(x => (int)x.Rating)))
            .Take(top)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RecentFeedbackDto>> GetRecentFeedbackAsync(int storeId, int count, CancellationToken ct)
    {
        var rows = await db.FeedbackSubmissions.AsNoTracking()
            .Where(s => s.StoreId == storeId)
            .OrderByDescending(s => s.SubmittedAtUtc)
            .Take(count)
            .Select(s => new
            {
                s.SubmissionId, s.OverallRating, s.Comments, s.SubmittedAtUtc,
                IsAnonymous = s.CustomerUserId == null,
                Categories = s.Items.Select(i => i.Category!.CategoryName).ToList(),
            })
            .ToListAsync(ct);

        return rows.Select(r => new RecentFeedbackDto(r.SubmissionId, r.OverallRating, r.Comments, r.SubmittedAtUtc, r.IsAnonymous, r.Categories))
            .ToList();
    }
}
