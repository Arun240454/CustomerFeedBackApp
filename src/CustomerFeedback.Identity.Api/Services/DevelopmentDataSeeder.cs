using CustomerFeedback.Shared.Data;
using CustomerFeedback.Shared.Domain;
using Microsoft.EntityFrameworkCore;

namespace CustomerFeedback.Identity.Api.Services;

/// <summary>Demo users and feedback for local development. Enabled only with Database:SeedSampleData=true.</summary>
public sealed class DevelopmentDataSeeder(FeedbackDbContext db, IPasswordHasher hasher, TimeProvider clock, ILogger<DevelopmentDataSeeder> logger)
{
    public const string DemoPassword = "Passw0rd!Demo1";

    private sealed record Sample(int StoreId, string? Customer, int DaysAgo, byte Overall, string? Comments, (short Category, byte Rating, string? Comment)[] Items);

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var hash = hasher.Hash(DemoPassword);
        User NewUser(string email, string first, string last, byte role) => new()
        {
            Email = email, PasswordHash = hash, FirstName = first, LastName = last, RoleId = role,
            CreatedAtUtc = now, UpdatedAtUtc = now,
        };

        var manager = NewUser("manager.delhi@example.com", "Meera", "Kapoor", RoleIds.StoreManager);
        var customers = new Dictionary<string, User>
        {
            ["asha"] = NewUser("asha@example.com", "Asha", "Verma", RoleIds.Customer),
            ["rohan"] = NewUser("rohan@example.com", "Rohan", "Mehta", RoleIds.Customer),
            ["priya"] = NewUser("priya@example.com", "Priya", "Nair", RoleIds.Customer),
        };
        db.Users.Add(manager);
        db.Users.AddRange(customers.Values);
        await db.SaveChangesAsync(ct);

        db.StoreManagers.Add(new StoreManager { StoreId = 1, UserId = manager.UserId, AssignedAtUtc = now });

        Sample[] samples =
        [
            new(1, "asha", 1, 5, "Great visit, very helpful staff", [(2, 5, "Very helpful staff"), (3, 5, null)]),
            new(1, "rohan", 3, 3, "Long checkout queue", [(5, 2, "Waited 15 minutes"), (2, 4, null)]),
            new(1, "priya", 6, 4, "Good products", [(1, 5, null), (4, 3, null)]),
            new(1, null, 9, 2, "Anonymous: messy aisles", [(3, 1, "Spills not cleaned"), (5, 3, null)]),
            new(1, "asha", 14, 4, null, [(1, 4, null), (2, 4, null)]),
            new(1, "rohan", 20, 5, "Loved the new range", [(1, 5, null), (4, 4, null)]),
            new(1, "priya", 27, 3, null, [(4, 2, "Prices not labelled")]),
            new(1, "asha", 35, 2, "Rude cashier", [(2, 1, null), (5, 2, null)]),
            new(1, "rohan", 42, 3, null, [(3, 3, null)]),
            new(2, "priya", 2, 5, "Excellent", [(2, 5, null), (1, 5, null)]),
        ];

        foreach (var s in samples)
        {
            db.FeedbackSubmissions.Add(new FeedbackSubmission
            {
                StoreId = s.StoreId,
                CustomerUserId = s.Customer is null ? null : customers[s.Customer].UserId,
                OverallRating = s.Overall,
                Comments = s.Comments,
                SubmittedAtUtc = now.AddDays(-s.DaysAgo),
                Items = s.Items.Select(i => new FeedbackItem { CategoryId = i.Category, Rating = i.Rating, Comment = i.Comment }).ToList(),
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo users and {Count} sample feedback submissions", samples.Length);
    }
}
