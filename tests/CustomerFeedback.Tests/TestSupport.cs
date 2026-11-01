using CustomerFeedback.Shared.Data;
using CustomerFeedback.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace CustomerFeedback.Tests;

internal static class TestSupport
{
    public static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    public static AesGcmFieldEncryptor Encryptor() =>
        new(Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(new byte[32]) }));

    /// <summary>Fresh in-memory database (seeded with roles, categories and two stores through HasData).</summary>
    public static FeedbackDbContext NewDb(string? name = null)
    {
        var options = new DbContextOptionsBuilder<FeedbackDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options;
        var db = new FeedbackDbContext(options, Encryptor());
        db.Database.EnsureCreated();
        return db;
    }

    public static FakeTimeProvider Clock() => new(Now);

    public static IOptionsSnapshot<T> Snapshot<T>(T value) where T : class
    {
        var mock = new Mock<IOptionsSnapshot<T>>();
        mock.Setup(o => o.Value).Returns(value);
        return mock.Object;
    }
}
