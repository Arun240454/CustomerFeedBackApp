using FluentValidation;

namespace CustomerFeedback.Feedback.Api.Contracts;

public sealed record FeedbackItemRequest(short CategoryId, byte Rating, string? Comment);

public sealed record SubmitFeedbackRequest(
    int StoreId,
    byte OverallRating,
    string? Comments,
    DateOnly? VisitDate,
    bool IsAnonymous,
    IReadOnlyList<FeedbackItemRequest>? Items);

public sealed record FeedbackReceipt(long SubmissionId, DateTime SubmittedAtUtc);

public sealed record StoreDto(int StoreId, string StoreCode, string StoreName, string? City);

public sealed record CategoryDto(short CategoryId, string CategoryName, string? Description);

public sealed record MyFeedbackItemDto(string CategoryName, byte Rating, string? Comment);

public sealed record MyFeedbackDto(
    long SubmissionId,
    string StoreName,
    byte OverallRating,
    string? Comments,
    DateOnly? VisitDate,
    DateTime SubmittedAtUtc,
    IReadOnlyList<MyFeedbackItemDto> Items);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed class SubmitFeedbackRequestValidator : AbstractValidator<SubmitFeedbackRequest>
{
    public const int MaxItems = 10;

    public SubmitFeedbackRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x.StoreId).GreaterThan(0);
        RuleFor(x => x.OverallRating).InclusiveBetween((byte)1, (byte)5).WithMessage("Rating must be between 1 and 5.");
        RuleFor(x => x.Comments).MaximumLength(2000);
        RuleFor(x => x.VisitDate)
            .Must(d => d is null || d.Value <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(1))
            .WithMessage("Visit date cannot be in the future.");

        RuleFor(x => x.Items)
            .Must(items => items is null || items.Count <= MaxItems).WithMessage($"At most {MaxItems} category ratings are allowed.")
            .Must(items => items is null || items.Select(i => i.CategoryId).Distinct().Count() == items.Count)
            .WithMessage("Each category can be rated only once.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.CategoryId).GreaterThan((short)0);
            item.RuleFor(i => i.Rating).InclusiveBetween((byte)1, (byte)5).WithMessage("Rating must be between 1 and 5.");
            item.RuleFor(i => i.Comment).MaximumLength(1000);
        });
    }
}
