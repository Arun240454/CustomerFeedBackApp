using CustomerFeedback.Analytics.Api.Services;
using CustomerFeedback.Shared.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CustomerFeedback.Tests.Analytics;

/// <summary>
/// Read-only smoke test that proves every dashboard query translates to SQL Server (the in-memory provider cannot).
/// Opt in by pointing CF_TEST_SQLSERVER at a migrated database, e.g.
///   $env:CF_TEST_SQLSERVER = 'Server=localhost;Database=CustomerFeedbackDB;Trusted_Connection=True;TrustServerCertificate=True;'
/// Without the variable the test is reported as passed without doing anything.
/// </summary>
public class SqlServerQueryTests
{
    [Fact]
    public async Task Dashboard_queries_translate_and_execute_on_sql_server()
    {
        var connection = Environment.GetEnvironmentVariable("CF_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connection))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<FeedbackDbContext>().UseSqlServer(connection).Options;
        await using var db = new FeedbackDbContext(options, TestSupport.Encryptor());
        var sut = new AnalyticsRepository(db);
        var from = new DateOnly(2000, 1, 1);
        var to = new DateOnly(2100, 1, 1);

        var act = async () =>
        {
            await sut.GetManagedStoresAsync(1, default);
            await sut.GetManagedStoreAsync(1, 1, default);
            await sut.CountAllAsync(1, default);
            await sut.GetDailyAggregatesAsync(1, from, to, default);
            await sut.GetRatingDistributionAsync(1, from, to, default);
            await sut.GetTopCategoriesAsync(1, from, to, 5, default);
            await sut.GetRecentFeedbackAsync(1, 0, default);
        };

        await act.Should().NotThrowAsync();
    }
}
