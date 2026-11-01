using CustomerFeedback.Shared.Data;
using CustomerFeedback.Shared.Domain;
using CustomerFeedback.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CustomerFeedback.Analytics.Api.Services;

public interface IFeedbackCategorizer
{
    /// <summary>Returns the names of categories whose keywords appear in the text.</summary>
    IReadOnlySet<string> Categorize(string? text);
}

public sealed class KeywordFeedbackCategorizer : IFeedbackCategorizer
{
    public const string FallbackCategory = "Other";

    private static readonly Dictionary<string, string[]> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Product Quality"] = ["product", "quality", "fresh", "stock", "range", "broken", "damaged", "expired", "selection"],
        ["Staff Service"] = ["staff", "cashier", "service", "rude", "helpful", "friendly", "employee", "manager", "polite"],
        ["Cleanliness"] = ["clean", "dirty", "spill", "messy", "hygien", "smell", "dust", "tidy"],
        ["Pricing"] = ["price", "pricing", "expensive", "cheap", "cost", "value", "discount", "overpriced", "label"],
        ["Wait Time"] = ["queue", "wait", "checkout", "slow", "line", "delay", "long time"],
    };

    public IReadOnlySet<string> Categorize(string? text)
    {
        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return matches;
        }

        foreach (var (category, words) in Keywords)
        {
            if (words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(category);
            }
        }

        return matches;
    }
}

/// <summary>
/// Consumes FeedbackSubmitted. Any topic found in the free-text comment that the customer did not rate explicitly
/// becomes a category item scored with the overall rating; a submission with no category at all lands in "Other".
/// Idempotent: re-delivery never duplicates items.
/// </summary>
public sealed class FeedbackCategorizationHandler(
    FeedbackDbContext db,
    IFeedbackCategorizer categorizer,
    ILogger<FeedbackCategorizationHandler> logger) : IMessageHandler<FeedbackSubmitted>
{
    public async Task HandleAsync(FeedbackSubmitted message, CancellationToken ct)
    {
        var submission = await db.FeedbackSubmissions.Include(s => s.Items)
            .SingleOrDefaultAsync(s => s.SubmissionId == message.SubmissionId, ct);
        if (submission is null)
        {
            logger.LogWarning("Submission {SubmissionId} not found; nothing to categorise", message.SubmissionId);
            return;
        }

        var text = string.Join(' ', new[] { submission.Comments }.Concat(submission.Items.Select(i => i.Comment)));
        var matched = categorizer.Categorize(text);
        if (matched.Count == 0 && submission.Items.Count == 0)
        {
            matched = new HashSet<string>([KeywordFeedbackCategorizer.FallbackCategory]);
        }

        var alreadyRated = submission.Items.Select(i => i.CategoryId).ToHashSet();
        var categories = await db.FeedbackCategories.Where(c => c.IsActive).ToListAsync(ct);
        var added = 0;

        foreach (var category in categories.Where(c => matched.Contains(c.CategoryName) && !alreadyRated.Contains(c.CategoryId)))
        {
            submission.Items.Add(new FeedbackItem { CategoryId = category.CategoryId, Rating = submission.OverallRating });
            added++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Submission {SubmissionId} categorised: {Added} category item(s) added", submission.SubmissionId, added);
    }
}
