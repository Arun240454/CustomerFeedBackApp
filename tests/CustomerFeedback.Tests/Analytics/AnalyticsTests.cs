using CustomerFeedback.Analytics.Api.Contracts;
using CustomerFeedback.Analytics.Api.Services;
using CustomerFeedback.Shared.Data;
using CustomerFeedback.Shared.Domain;
using CustomerFeedback.Shared.Errors;
using CustomerFeedback.Shared.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CustomerFeedback.Tests.Analytics;

public class TrendCalculatorTests
{
    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(3, 10, 3.33)]
    [InlineData(2, 10, 5.0)]
    public void Average_rounds_to_two_places_and_handles_empty(int count, int sum, double? expected)
    {
        TrendCalculator.Average(count, sum).Should().Be(expected is null ? null : (decimal)expected);
    }

    [Theory]
    [InlineData(4.0, 3.5, "improving")]
    [InlineData(3.0, 3.5, "declining")]
    [InlineData(3.5, 3.45, "stable")]
    [InlineData(3.6, 3.5, "improving")]
    [InlineData(null, 3.5, "insufficient_data")]
    [InlineData(3.5, null, "insufficient_data")]
    public void Direction_uses_a_stable_band(double? current, double? previous, string expected)
    {
        TrendCalculator.Direction((decimal?)current, (decimal?)previous).Should().Be(expected);
    }

    [Fact]
    public void BuildDailySeries_zero_fills_missing_days_in_order()
    {
        var from = new DateOnly(2026, 6, 1);
        var rows = new[] { new DailyAggregate(new DateOnly(2026, 6, 2), 2, 9) };

        var series = TrendCalculator.BuildDailySeries(rows, from, new DateOnly(2026, 6, 3));

        series.Select(p => p.Date.Day).Should().Equal(1, 2, 3);
        series.Select(p => p.SubmissionCount).Should().Equal(0, 2, 0);
        series[1].AvgRating.Should().Be(4.5m);
        series[0].AvgRating.Should().BeNull();
    }
}

public class SummaryServiceTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(TestSupport.Now.UtcDateTime);
    private static readonly ManagedStoreDto Store = new(1, "DEL-001", "Connaught Place Store", "New Delhi");

    private readonly Mock<IAnalyticsRepository> _repo = new();
    private readonly AnalyticsOptions _options = new() { DefaultWindowDays = 7, MaxWindowDays = 30, TopCategoryCount = 3 };
    private readonly SummaryService _sut;

    public SummaryServiceTests()
    {
        _repo.Setup(r => r.GetManagedStoreAsync(1, 10, It.IsAny<CancellationToken>())).ReturnsAsync(Store);
        _repo.Setup(r => r.CountAllAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(42);
        _repo.Setup(r => r.GetRatingDistributionAsync(1, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RatingBucketDto(5, 2), new RatingBucketDto(2, 1)]);
        _repo.Setup(r => r.GetTopCategoriesAsync(1, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CategoryAggregate(2, "Staff Service", 3, 13)]);
        _sut = new SummaryService(_repo.Object, TestSupport.Snapshot(_options), TestSupport.Clock(), NullLogger<SummaryService>.Instance);
    }

    private void SetupDaily(params DailyAggregate[] rows) =>
        _repo.Setup(r => r.GetDailyAggregatesAsync(1, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    [Fact]
    public async Task Summary_splits_current_and_previous_windows_and_computes_the_trend()
    {
        SetupDaily(
            new DailyAggregate(Today, 2, 10),                 // current: avg 5
            new DailyAggregate(Today.AddDays(-6), 2, 8),      // current (window start): avg 4 -> 4.5 overall
            new DailyAggregate(Today.AddDays(-7), 2, 6),      // previous: avg 3
            new DailyAggregate(Today.AddDays(-13), 2, 4));    // previous start -> 2.5 overall

        var summary = await _sut.GetSummaryAsync(10, 1, null, CancellationToken.None);

        summary.WindowDays.Should().Be(7);
        summary.FeedbackInWindow.Should().Be(4);
        summary.AvgSatisfactionInWindow.Should().Be(4.5m);
        summary.AvgSatisfactionPreviousWindow.Should().Be(2.5m);
        summary.TrendDelta.Should().Be(2.0m);
        summary.TrendDirection.Should().Be("improving");
        summary.TotalFeedbackAllTime.Should().Be(42);
        summary.DailyTrend.Should().HaveCount(7);
        summary.DailyTrend[^1].SubmissionCount.Should().Be(2);
        summary.RatingDistribution.Select(b => b.Count).Should().Equal(0, 1, 0, 0, 2);
        summary.TopCategories.Single().AvgRating.Should().Be(4.33m);
    }

    [Fact]
    public async Task Summary_without_data_reports_insufficient_data()
    {
        SetupDaily();

        var summary = await _sut.GetSummaryAsync(10, 1, null, CancellationToken.None);

        summary.FeedbackInWindow.Should().Be(0);
        summary.AvgSatisfactionInWindow.Should().BeNull();
        summary.TrendDelta.Should().BeNull();
        summary.TrendDirection.Should().Be("insufficient_data");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(9999, 30)]
    [InlineData(14, 14)]
    public async Task Summary_clamps_the_requested_window(int requested, int expected)
    {
        SetupDaily();

        var summary = await _sut.GetSummaryAsync(10, 1, requested, CancellationToken.None);

        summary.WindowDays.Should().Be(expected);
        summary.DailyTrend.Should().HaveCount(expected);
    }

    [Fact]
    public async Task Summary_queries_both_windows_with_a_single_daily_call()
    {
        SetupDaily();

        await _sut.GetSummaryAsync(10, 1, 7, CancellationToken.None);

        _repo.Verify(r => r.GetDailyAggregatesAsync(1, Today.AddDays(-13), Today, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.GetTopCategoriesAsync(1, Today.AddDays(-6), Today, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Monthly_report_covers_a_past_month_and_compares_with_the_month_before()
    {
        // Test clock is 2026-06-15, so May 2026 is complete and April is the comparison month.
        SetupDaily(
            new DailyAggregate(new DateOnly(2026, 5, 1), 2, 10),    // May: avg 4.5 over 4 ratings
            new DailyAggregate(new DateOnly(2026, 5, 31), 2, 8),
            new DailyAggregate(new DateOnly(2026, 4, 30), 2, 6),    // April: avg 3
            new DailyAggregate(new DateOnly(2026, 4, 1), 2, 6));

        var report = await _sut.GetMonthlyReportAsync(10, 1, 2026, 5, CancellationToken.None);

        report.StoreName.Should().Be("Connaught Place Store");
        report.TotalFeedback.Should().Be(4);
        report.AvgRating.Should().Be(4.5m);
        report.PreviousMonthFeedback.Should().Be(4);
        report.PreviousMonthAvgRating.Should().Be(3.0m);
        report.Delta.Should().Be(1.5m);
        report.TrendDirection.Should().Be("improving");
        report.DailyTrend.Should().HaveCount(31);
        report.RatingDistribution.Should().HaveCount(5);
        report.Categories.Single().CategoryName.Should().Be("Staff Service");
        _repo.Verify(r => r.GetDailyAggregatesAsync(1, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 31), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Monthly_report_for_the_running_month_stops_at_today()
    {
        SetupDaily();

        var report = await _sut.GetMonthlyReportAsync(10, 1, 2026, 6, CancellationToken.None);

        report.DailyTrend.Should().HaveCount(15);
        report.DailyTrend[^1].Date.Should().Be(new DateOnly(2026, 6, 15));
        report.TrendDirection.Should().Be("insufficient_data");
    }

    [Theory]
    [InlineData(2026, 7)]   // future month
    [InlineData(2027, 1)]   // future year
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(1999, 5)]
    public async Task Monthly_report_rejects_invalid_or_future_periods(int year, int month)
    {
        var act = () => _sut.GetMonthlyReportAsync(10, 1, year, month, CancellationToken.None);

        (await act.Should().ThrowAsync<RequestValidationException>()).Which.Errors.Should().ContainKey("month");
    }

    [Fact]
    public async Task Monthly_report_is_forbidden_for_a_store_the_manager_is_not_assigned_to()
    {
        _repo.Setup(r => r.GetManagedStoreAsync(2, 10, It.IsAny<CancellationToken>())).ReturnsAsync((ManagedStoreDto?)null);

        var act = () => _sut.GetMonthlyReportAsync(10, 2, 2026, 5, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Summary_is_forbidden_for_a_store_the_manager_is_not_assigned_to()
    {
        var act = () => _sut.GetSummaryAsync(10, 2, null, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _repo.Verify(r => r.GetDailyAggregatesAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Recent_feedback_checks_access_and_clamps_the_count()
    {
        _repo.Setup(r => r.GetRecentFeedbackAsync(1, 50, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        await _sut.GetRecentFeedbackAsync(10, 1, 500, CancellationToken.None);

        _repo.Verify(r => r.GetRecentFeedbackAsync(1, 50, It.IsAny<CancellationToken>()), Times.Once);
        var act = () => _sut.GetRecentFeedbackAsync(99, 1, 5, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Stores_are_delegated_to_the_repository()
    {
        _repo.Setup(r => r.GetManagedStoresAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync([Store]);

        (await _sut.GetStoresAsync(10, CancellationToken.None)).Should().ContainSingle();
    }
}

public class AnalyticsRepositoryTests : IDisposable
{
    private readonly FeedbackDbContext _db = TestSupport.NewDb();
    private readonly AnalyticsRepository _sut;

    public AnalyticsRepositoryTests()
    {
        _db.Users.AddRange(
            new User { Email = "m@x.com", PasswordHash = "h", FirstName = "M", LastName = "M", RoleId = RoleIds.StoreManager },
            new User { Email = "other@x.com", PasswordHash = "h", FirstName = "O", LastName = "O", RoleId = RoleIds.StoreManager });
        _db.SaveChanges();
        _db.StoreManagers.Add(new StoreManager { StoreId = 1, UserId = _db.Users.First().UserId });
        _db.FeedbackSubmissions.Add(new FeedbackSubmission
        {
            StoreId = 1, OverallRating = 5, Comments = "great", SubmittedAtUtc = TestSupport.Now.UtcDateTime,
            Items = [new FeedbackItem { CategoryId = 2, Rating = 5 }],
        });
        _db.SaveChanges();
        _sut = new AnalyticsRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Managed_stores_only_include_assigned_active_stores()
    {
        var managerId = _db.Users.First().UserId;
        var otherId = _db.Users.OrderBy(u => u.UserId).Last().UserId;

        (await _sut.GetManagedStoresAsync(managerId, default)).Should().ContainSingle().Which.StoreId.Should().Be(1);
        (await _sut.GetManagedStoresAsync(otherId, default)).Should().BeEmpty();
        (await _sut.GetManagedStoreAsync(1, managerId, default)).Should().NotBeNull();
        (await _sut.GetManagedStoreAsync(2, managerId, default)).Should().BeNull();
    }

    [Fact]
    public async Task Count_and_recent_feedback_read_the_store_data()
    {
        (await _sut.CountAllAsync(1, default)).Should().Be(1);
        (await _sut.CountAllAsync(2, default)).Should().Be(0);

        var recent = await _sut.GetRecentFeedbackAsync(1, 5, default);

        var item = recent.Should().ContainSingle().Subject;
        item.Comments.Should().Be("great");
        item.IsAnonymous.Should().BeTrue();
        item.Categories.Should().Equal("Staff Service");
    }
}

public class CategorizationTests
{
    private readonly KeywordFeedbackCategorizer _categorizer = new();

    [Fact]
    public void Categorizer_matches_topics_case_insensitively()
    {
        var result = _categorizer.Categorize("The QUEUE was long and the floor was Dirty, staff rude");

        result.Should().BeEquivalentTo(["Wait Time", "Cleanliness", "Staff Service"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zzz qqq")]
    public void Categorizer_returns_nothing_for_empty_or_unrelated_text(string? text)
    {
        _categorizer.Categorize(text).Should().BeEmpty();
    }

    private static async Task<FeedbackDbContext> SeedAsync(string? comments, params FeedbackItem[] items)
    {
        var db = TestSupport.NewDb();
        db.FeedbackSubmissions.Add(new FeedbackSubmission
        {
            StoreId = 1, OverallRating = 2, Comments = comments, SubmittedAtUtc = TestSupport.Now.UtcDateTime, Items = [.. items],
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static FeedbackCategorizationHandler Handler(FeedbackDbContext db) =>
        new(db, new KeywordFeedbackCategorizer(), NullLogger<FeedbackCategorizationHandler>.Instance);

    [Fact]
    public async Task Handler_adds_unrated_topics_with_the_overall_rating_and_keeps_explicit_ratings()
    {
        using var db = await SeedAsync("Long queue, and prices were unclear", new FeedbackItem { CategoryId = 5, Rating = 1 });

        await Handler(db).HandleAsync(new FeedbackSubmitted(1, 1, DateTime.UtcNow), default);

        var items = await db.FeedbackItems.OrderBy(i => i.CategoryId).ToListAsync();
        items.Select(i => (i.CategoryId, i.Rating)).Should().Equal((4, (byte)2), (5, (byte)1));
    }

    [Fact]
    public async Task Handler_falls_back_to_Other_when_nothing_matches_and_nothing_was_rated()
    {
        using var db = await SeedAsync("zzz");

        await Handler(db).HandleAsync(new FeedbackSubmitted(1, 1, DateTime.UtcNow), default);

        (await db.FeedbackItems.SingleAsync()).CategoryId.Should().Be(6);
    }

    [Fact]
    public async Task Handler_is_idempotent_on_redelivery()
    {
        using var db = await SeedAsync("The queue was long");
        var handler = Handler(db);

        await handler.HandleAsync(new FeedbackSubmitted(1, 1, DateTime.UtcNow), default);
        await handler.HandleAsync(new FeedbackSubmitted(1, 1, DateTime.UtcNow), default);

        (await db.FeedbackItems.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Handler_ignores_missing_submissions()
    {
        using var db = TestSupport.NewDb();

        var act = () => Handler(db).HandleAsync(new FeedbackSubmitted(123, 1, DateTime.UtcNow), default);

        await act.Should().NotThrowAsync();
    }
}
