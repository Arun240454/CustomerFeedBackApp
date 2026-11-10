namespace CustomerFeedback.Analytics.Api.Contracts;

public sealed record ManagedStoreDto(int StoreId, string StoreCode, string StoreName, string? City);

/// <summary>One calendar day of submissions as read from the database.</summary>
public sealed record DailyAggregate(DateOnly Date, int Count, int RatingSum);

public sealed record CategoryAggregate(short CategoryId, string CategoryName, int Count, int RatingSum);

public sealed record DailyTrendPoint(DateOnly Date, int SubmissionCount, decimal? AvgRating);

public sealed record CategoryScoreDto(short CategoryId, string CategoryName, int RatingCount, decimal AvgRating);

public sealed record RatingBucketDto(int Rating, int Count);

public sealed record StoreSummaryDto(
    int StoreId,
    string StoreName,
    int WindowDays,
    long TotalFeedbackAllTime,
    int FeedbackInWindow,
    decimal? AvgSatisfactionInWindow,
    decimal? AvgSatisfactionPreviousWindow,
    decimal? TrendDelta,
    string TrendDirection,
    IReadOnlyList<RatingBucketDto> RatingDistribution,
    IReadOnlyList<DailyTrendPoint> DailyTrend,
    IReadOnlyList<CategoryScoreDto> TopCategories);

public sealed record RecentFeedbackDto(
    long SubmissionId,
    byte OverallRating,
    string? Comments,
    DateTime SubmittedAtUtc,
    bool IsAnonymous,
    IReadOnlyList<string> Categories);

/// <summary>Everything the monthly PDF report needs for one store and one calendar month.</summary>
public sealed record MonthlyReportDto(
    int StoreId,
    string StoreName,
    int Year,
    int Month,
    DateTime GeneratedAtUtc,
    int TotalFeedback,
    decimal? AvgRating,
    int PreviousMonthFeedback,
    decimal? PreviousMonthAvgRating,
    decimal? Delta,
    string TrendDirection,
    IReadOnlyList<RatingBucketDto> RatingDistribution,
    IReadOnlyList<DailyTrendPoint> DailyTrend,
    IReadOnlyList<CategoryScoreDto> Categories);
