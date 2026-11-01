using CustomerFeedback.Analytics.Api.Contracts;
using CustomerFeedback.Shared.Errors;
using Microsoft.Extensions.Options;

namespace CustomerFeedback.Analytics.Api.Services;

public class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    public int DefaultWindowDays { get; set; } = 30;
    public int MaxWindowDays { get; set; } = 365;
    public int TopCategoryCount { get; set; } = 5;
}

public interface ISummaryService
{
    Task<IReadOnlyList<ManagedStoreDto>> GetStoresAsync(int managerId, CancellationToken ct);
    Task<StoreSummaryDto> GetSummaryAsync(int managerId, int storeId, int? days, CancellationToken ct);
    Task<IReadOnlyList<RecentFeedbackDto>> GetRecentFeedbackAsync(int managerId, int storeId, int count, CancellationToken ct);
}

/// <summary>Pure trend maths, kept separate from IO so it is trivially testable.</summary>
public static class TrendCalculator
{
    public const decimal StableBand = 0.10m;

    public static decimal? Average(int count, int sum) => count == 0 ? null : Math.Round((decimal)sum / count, 2);

    public static IReadOnlyList<DailyTrendPoint> BuildDailySeries(IEnumerable<DailyAggregate> rows, DateOnly from, DateOnly to)
    {
        var byDate = rows.ToDictionary(r => r.Date);
        var series = new List<DailyTrendPoint>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            series.Add(byDate.TryGetValue(day, out var row)
                ? new DailyTrendPoint(day, row.Count, Average(row.Count, row.RatingSum))
                : new DailyTrendPoint(day, 0, null));
        }

        return series;
    }

    public static string Direction(decimal? current, decimal? previous)
    {
        if (current is null || previous is null)
        {
            return "insufficient_data";
        }

        var delta = current.Value - previous.Value;
        return delta >= StableBand ? "improving" : delta <= -StableBand ? "declining" : "stable";
    }
}

public sealed class SummaryService(
    IAnalyticsRepository repository,
    IOptionsSnapshot<AnalyticsOptions> options,
    TimeProvider clock,
    ILogger<SummaryService> logger) : ISummaryService
{
    private readonly AnalyticsOptions _options = options.Value;

    public Task<IReadOnlyList<ManagedStoreDto>> GetStoresAsync(int managerId, CancellationToken ct) =>
        repository.GetManagedStoresAsync(managerId, ct);

    public async Task<StoreSummaryDto> GetSummaryAsync(int managerId, int storeId, int? days, CancellationToken ct)
    {
        var store = await RequireStoreAsync(managerId, storeId, ct);
        var window = ClampDays(days);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var currentStart = today.AddDays(-(window - 1));
        var previousStart = currentStart.AddDays(-window);
        var previousEnd = currentStart.AddDays(-1);

        // One covering index seek returns both windows (<= 2 * window rows); split in memory.
        var daily = await repository.GetDailyAggregatesAsync(storeId, previousStart, today, ct);
        var current = daily.Where(d => d.Date >= currentStart).ToList();
        var previous = daily.Where(d => d.Date <= previousEnd).ToList();

        var currentCount = current.Sum(d => d.Count);
        var avgCurrent = TrendCalculator.Average(currentCount, current.Sum(d => d.RatingSum));
        var avgPrevious = TrendCalculator.Average(previous.Sum(d => d.Count), previous.Sum(d => d.RatingSum));

        var total = await repository.CountAllAsync(storeId, ct);
        var distribution = await repository.GetRatingDistributionAsync(storeId, currentStart, today, ct);
        var categories = await repository.GetTopCategoriesAsync(storeId, currentStart, today, _options.TopCategoryCount, ct);

        logger.LogInformation("Summary built for store {StoreId}, manager {ManagerId}, window {Days}d, {Count} submissions",
            storeId, managerId, window, currentCount);

        return new StoreSummaryDto(
            store.StoreId, store.StoreName, window, total, currentCount, avgCurrent, avgPrevious,
            avgCurrent is not null && avgPrevious is not null ? avgCurrent - avgPrevious : null,
            TrendCalculator.Direction(avgCurrent, avgPrevious),
            Enumerable.Range(1, 5).Select(r => new RatingBucketDto(r, distribution.FirstOrDefault(b => b.Rating == r)?.Count ?? 0)).ToList(),
            TrendCalculator.BuildDailySeries(current, currentStart, today),
            categories.Select(c => new CategoryScoreDto(c.CategoryId, c.CategoryName, c.Count, TrendCalculator.Average(c.Count, c.RatingSum) ?? 0)).ToList());
    }

    public async Task<IReadOnlyList<RecentFeedbackDto>> GetRecentFeedbackAsync(int managerId, int storeId, int count, CancellationToken ct)
    {
        await RequireStoreAsync(managerId, storeId, ct);
        return await repository.GetRecentFeedbackAsync(storeId, Math.Clamp(count, 1, 50), ct);
    }

    private int ClampDays(int? days)
    {
        var requested = days ?? _options.DefaultWindowDays;
        return Math.Clamp(requested, 1, Math.Max(1, _options.MaxWindowDays));
    }

    private async Task<ManagedStoreDto> RequireStoreAsync(int managerId, int storeId, CancellationToken ct)
    {
        // Authorisation is per-store: role alone is not enough. Unassigned and unknown stores look identical.
        var store = await repository.GetManagedStoreAsync(storeId, managerId, ct);
        if (store is null)
        {
            logger.LogWarning("Manager {ManagerId} denied access to store {StoreId}", managerId, storeId);
            throw new ForbiddenException("You are not assigned to this store.");
        }

        return store;
    }
}
