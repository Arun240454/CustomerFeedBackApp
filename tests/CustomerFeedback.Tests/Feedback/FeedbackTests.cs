using CustomerFeedback.Feedback.Api.Contracts;
using CustomerFeedback.Feedback.Api.Services;
using CustomerFeedback.Shared.Data;
using CustomerFeedback.Shared.Domain;
using CustomerFeedback.Shared.Errors;
using CustomerFeedback.Shared.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CustomerFeedback.Tests.Feedback;

public class SubmitFeedbackRequestValidatorTests
{
    private readonly SubmitFeedbackRequestValidator _sut = new(TestSupport.Clock());

    private static SubmitFeedbackRequest Valid() => new(1, 4, "Nice", new DateOnly(2026, 6, 14), false, [new FeedbackItemRequest(1, 5, null)]);

    [Fact]
    public void Accepts_a_valid_request() => _sut.Validate(Valid()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Rejects_ratings_outside_1_to_5(byte rating) =>
        _sut.Validate(Valid() with { OverallRating = rating }).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_invalid_store() => _sut.Validate(Valid() with { StoreId = 0 }).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_overlong_comments() =>
        _sut.Validate(Valid() with { Comments = new string('x', 2001) }).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_future_visit_dates() =>
        _sut.Validate(Valid() with { VisitDate = new DateOnly(2026, 12, 1) }).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_duplicate_categories() =>
        _sut.Validate(Valid() with { Items = [new(1, 3, null), new(1, 4, null)] }).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_too_many_items() =>
        _sut.Validate(Valid() with { Items = Enumerable.Range(1, 11).Select(i => new FeedbackItemRequest((short)i, 3, null)).ToList() })
            .IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_bad_item_rating_and_long_item_comment()
    {
        _sut.Validate(Valid() with { Items = [new(1, 9, null)] }).IsValid.Should().BeFalse();
        _sut.Validate(Valid() with { Items = [new(1, 3, new string('x', 1001))] }).IsValid.Should().BeFalse();
    }
}

public class FeedbackServiceTests : IDisposable
{
    private readonly FeedbackDbContext _db = TestSupport.NewDb();
    private readonly Mock<IEventPublisher> _events = new();
    private readonly FeedbackService _sut;

    public FeedbackServiceTests()
    {
        _sut = new FeedbackService(_db, _events.Object, new SubmitFeedbackRequestValidator(TestSupport.Clock()),
            TestSupport.Clock(), NullLogger<FeedbackService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static SubmitFeedbackRequest Request(int store = 1, bool anonymous = false, IReadOnlyList<FeedbackItemRequest>? items = null) =>
        new(store, 4, "Staff were <b>great</b>", null, anonymous, items);

    [Fact]
    public async Task Submit_stores_sanitised_feedback_with_items_and_publishes_an_event()
    {
        var receipt = await _sut.SubmitAsync(7, Request(items: [new(2, 5, "kind"), new(3, 4, null)]), CancellationToken.None);

        var saved = await _db.FeedbackSubmissions.Include(s => s.Items).SingleAsync();
        saved.SubmissionId.Should().Be(receipt.SubmissionId);
        saved.CustomerUserId.Should().Be(7);
        saved.Comments.Should().Be("Staff were great");
        saved.Items.Should().HaveCount(2);
        saved.SubmittedAtUtc.Should().Be(TestSupport.Now.UtcDateTime);
        _events.Verify(e => e.Publish(It.Is<FeedbackSubmitted>(m => m.SubmissionId == saved.SubmissionId && m.StoreId == 1)), Times.Once);
    }

    [Fact]
    public async Task Submit_drops_the_customer_identity_for_anonymous_feedback()
    {
        await _sut.SubmitAsync(7, Request(anonymous: true), CancellationToken.None);

        (await _db.FeedbackSubmissions.SingleAsync()).CustomerUserId.Should().BeNull();
    }

    [Fact]
    public async Task Submit_rejects_unknown_store()
    {
        var act = () => _sut.SubmitAsync(7, Request(store: 999), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        _events.Verify(e => e.Publish(It.IsAny<FeedbackSubmitted>()), Times.Never);
    }

    [Fact]
    public async Task Submit_rejects_inactive_store()
    {
        (await _db.Stores.FindAsync(2))!.IsActive = false;
        await _db.SaveChangesAsync();

        var act = () => _sut.SubmitAsync(7, Request(store: 2), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Submit_rejects_unknown_categories()
    {
        var act = () => _sut.SubmitAsync(7, Request(items: [new(99, 3, null)]), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<RequestValidationException>()).Which;
        ex.Errors["items"][0].Should().Contain("99");
    }

    [Fact]
    public async Task Submit_rejects_invalid_requests()
    {
        var act = () => _sut.SubmitAsync(7, Request() with { OverallRating = 9 }, CancellationToken.None);

        await act.Should().ThrowAsync<RequestValidationException>();
    }

    [Fact]
    public async Task GetMyFeedback_pages_newest_first_and_only_returns_own_items()
    {
        for (var i = 0; i < 3; i++)
        {
            await _sut.SubmitAsync(7, Request(items: [new(2, 5, null)]), CancellationToken.None);
        }

        await _sut.SubmitAsync(8, Request(), CancellationToken.None);

        var page = await _sut.GetMyFeedbackAsync(7, page: 1, pageSize: 2, CancellationToken.None);

        page.TotalCount.Should().Be(3);
        page.Items.Should().HaveCount(2);
        page.Items[0].StoreName.Should().Be("Connaught Place Store");
        page.Items[0].Items.Should().ContainSingle().Which.CategoryName.Should().Be("Staff Service");
    }

    [Fact]
    public async Task GetMyFeedback_clamps_paging_arguments()
    {
        var page = await _sut.GetMyFeedbackAsync(7, page: -3, pageSize: 5000, CancellationToken.None);

        page.Page.Should().Be(1);
        page.PageSize.Should().Be(FeedbackService.MaxPageSize);
    }

    [Fact]
    public async Task Reference_data_lists_active_stores_and_categories_in_order()
    {
        (await _sut.GetStoresAsync(CancellationToken.None)).Select(s => s.StoreCode).Should().Equal("DEL-001", "BLR-001");
        var categories = await _sut.GetCategoriesAsync(CancellationToken.None);
        categories.Should().HaveCount(6);
        categories[0].CategoryName.Should().Be("Product Quality");
    }
}
