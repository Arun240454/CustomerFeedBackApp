using CustomerFeedback.Identity.Api.Contracts;
using CustomerFeedback.Identity.Api.Services;
using CustomerFeedback.Shared.Data;
using CustomerFeedback.Shared.Domain;
using CustomerFeedback.Shared.Errors;
using CustomerFeedback.Shared.Security;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace CustomerFeedback.Tests.Identity;

public class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _sut = new(iterations: 1000);

    [Fact]
    public void Hash_verifies_the_original_password_only()
    {
        var hash = _sut.Hash("Correct-Horse-1");

        _sut.Verify("Correct-Horse-1", hash).Should().BeTrue();
        _sut.Verify("wrong", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_is_salted_and_self_describing()
    {
        var a = _sut.Hash("same");
        var b = _sut.Hash("same");

        a.Should().NotBe(b);
        a.Should().StartWith("$pbkdf2-sha256$1000$");
        a.Length.Should().BeLessThanOrEqualTo(255);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("$pbkdf2-sha256$abc$AAAA$AAAA")]
    [InlineData("$other$1000$AAAA$AAAA")]
    [InlineData("$pbkdf2-sha256$1000$***$***")]
    public void Verify_rejects_malformed_hashes(string stored)
    {
        _sut.Verify("x", stored).Should().BeFalse();
    }
}

public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _sut = new();

    private static RegisterRequest Valid() => new("a@b.com", "Passw0rdOK1", "Asha", "Verma", "+91 98765 43210", "Customer");

    [Fact]
    public void Accepts_a_valid_request() => _sut.Validate(Valid()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("short1A")]
    [InlineData("alllowercase1234")]
    [InlineData("ALLUPPERCASE1234")]
    [InlineData("NoDigitsHereAtAll")]
    public void Rejects_weak_passwords(string password) =>
        _sut.Validate(Valid() with { Password = password }).IsValid.Should().BeFalse();

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public void Rejects_bad_email(string email) => _sut.Validate(Valid() with { Email = email }).IsValid.Should().BeFalse();

    [Theory]
    [InlineData("Robert'); DROP")]
    [InlineData("123")]
    public void Rejects_names_with_invalid_characters(string name) =>
        _sut.Validate(Valid() with { FirstName = name }).IsValid.Should().BeFalse();

    [Fact]
    public void Accepts_names_with_apostrophes_and_unicode() =>
        _sut.Validate(Valid() with { LastName = "O'Brien-Ñandú" }).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("abc")]
    [InlineData("<script>")]
    public void Rejects_bad_phone(string phone) => _sut.Validate(Valid() with { PhoneNumber = phone }).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_unknown_role() => _sut.Validate(Valid() with { Role = "Admin" }).IsValid.Should().BeFalse();
}

public class LoginRequestValidatorTests
{
    [Fact]
    public void Requires_email_and_password()
    {
        var sut = new LoginRequestValidator();

        sut.Validate(new LoginRequest("a@b.com", "x")).IsValid.Should().BeTrue();
        sut.Validate(new LoginRequest("", "")).IsValid.Should().BeFalse();
    }
}

public class AuthServiceTests : IDisposable
{
    private readonly FeedbackDbContext _db = TestSupport.NewDb();
    private readonly FakeTimeProvider _clock = TestSupport.Clock();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IJwtTokenService> _tokens = new();
    private readonly LoginSecurityOptions _security = new() { MaxFailedLogins = 3, LockoutMinutes = 10 };
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("HASH");
        _hasher.Setup(h => h.Verify("good-password", "HASH")).Returns(true);
        _tokens.Setup(t => t.CreateToken(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new AccessToken("jwt", TestSupport.Now.UtcDateTime.AddHours(1)));

        _sut = new AuthService(_db, _hasher.Object, _tokens.Object, new RegisterRequestValidator(), new LoginRequestValidator(),
            TestSupport.Snapshot(_security), _clock, NullLogger<AuthService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static RegisterRequest NewUser(string email = "New.User@Example.com", string? role = null) =>
        new(email, "Passw0rdOK1", "New", "User", "+911234567890", role);

    private async Task<User> SeedUserAsync(bool active = true)
    {
        await _sut.RegisterAsync(NewUser("seed@example.com"), CancellationToken.None);
        var user = await _db.Users.SingleAsync(u => u.Email == "seed@example.com");
        user.IsActive = active;
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Register_normalises_hashes_and_stores_the_user()
    {
        var dto = await _sut.RegisterAsync(NewUser(), CancellationToken.None);

        dto.Email.Should().Be("new.user@example.com");
        dto.Role.Should().Be(RoleNames.Customer);
        var stored = await _db.Users.SingleAsync();
        stored.PasswordHash.Should().Be("HASH");
        stored.RoleId.Should().Be(RoleIds.Customer);
        stored.CreatedAtUtc.Should().Be(TestSupport.Now.UtcDateTime);
    }

    [Fact]
    public async Task Register_can_create_a_store_manager()
    {
        var dto = await _sut.RegisterAsync(NewUser(role: RoleNames.StoreManager), CancellationToken.None);

        dto.Role.Should().Be(RoleNames.StoreManager);
        (await _db.Users.SingleAsync()).RoleId.Should().Be(RoleIds.StoreManager);
    }

    [Fact]
    public async Task Register_sanitises_names()
    {
        await _sut.RegisterAsync(NewUser() with { FirstName = "<b>Asha</b>" }, CancellationToken.None);

        (await _db.Users.SingleAsync()).FirstName.Should().Be("Asha");
    }

    [Fact]
    public async Task Register_rejects_duplicate_email_case_insensitively()
    {
        await _sut.RegisterAsync(NewUser(), CancellationToken.None);

        var act = () => _sut.RegisterAsync(NewUser("NEW.USER@example.com"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Register_rejects_invalid_input_without_hashing()
    {
        var act = () => _sut.RegisterAsync(NewUser() with { Password = "weak" }, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<RequestValidationException>()).Which;
        ex.Errors.Should().ContainKey("password");
        _hasher.Verify(h => h.Hash(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Login_returns_a_token_and_resets_failure_state()
    {
        var user = await SeedUserAsync();
        user.FailedLoginCount = 2;
        await _db.SaveChangesAsync();

        var response = await _sut.LoginAsync(new LoginRequest("SEED@example.com", "good-password"), CancellationToken.None);

        response.AccessToken.Should().Be("jwt");
        response.User.Role.Should().Be(RoleNames.Customer);
        var stored = await _db.Users.SingleAsync();
        stored.FailedLoginCount.Should().Be(0);
        stored.LastLoginUtc.Should().Be(TestSupport.Now.UtcDateTime);
    }

    [Fact]
    public async Task Login_with_unknown_email_is_unauthorised_and_still_burns_a_hash_check()
    {
        var act = () => _sut.LoginAsync(new LoginRequest("ghost@example.com", "whatever"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>();
        _hasher.Verify(h => h.Verify("whatever", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Login_with_wrong_password_counts_failures_then_locks_the_account()
    {
        await SeedUserAsync();
        var bad = new LoginRequest("seed@example.com", "nope");

        for (var i = 0; i < 3; i++)
        {
            var act = () => _sut.LoginAsync(bad, CancellationToken.None);
            await act.Should().ThrowAsync<UnauthorizedException>();
        }

        var stored = await _db.Users.SingleAsync();
        stored.LockoutEndUtc.Should().Be(TestSupport.Now.UtcDateTime.AddMinutes(10));

        var locked = () => _sut.LoginAsync(new LoginRequest("seed@example.com", "good-password"), CancellationToken.None);
        await locked.Should().ThrowAsync<AccountLockedException>();
    }

    [Fact]
    public async Task Login_succeeds_again_once_the_lockout_has_expired()
    {
        var user = await SeedUserAsync();
        user.LockoutEndUtc = TestSupport.Now.UtcDateTime.AddMinutes(-1);
        await _db.SaveChangesAsync();

        var response = await _sut.LoginAsync(new LoginRequest("seed@example.com", "good-password"), CancellationToken.None);

        response.AccessToken.Should().Be("jwt");
    }

    [Fact]
    public async Task Login_rejects_deactivated_accounts()
    {
        await SeedUserAsync(active: false);

        var act = () => _sut.LoginAsync(new LoginRequest("seed@example.com", "good-password"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Login_validates_input()
    {
        var act = () => _sut.LoginAsync(new LoginRequest("", ""), CancellationToken.None);

        await act.Should().ThrowAsync<RequestValidationException>();
    }

    [Fact]
    public async Task GetProfile_returns_the_user_or_not_found()
    {
        var user = await SeedUserAsync();

        (await _sut.GetProfileAsync(user.UserId, CancellationToken.None)).Email.Should().Be("seed@example.com");
        var act = () => _sut.GetProfileAsync(9999, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Phone_number_round_trips_through_the_encrypting_converter()
    {
        await _sut.RegisterAsync(NewUser(), CancellationToken.None);

        (await _db.Users.AsNoTracking().SingleAsync()).PhoneNumber.Should().Be("+911234567890");
    }
}

public class DevelopmentDataSeederTests
{
    [Fact]
    public async Task Seeds_users_manager_assignment_and_feedback_once()
    {
        using var db = TestSupport.NewDb();
        var sut = new DevelopmentDataSeeder(db, new Pbkdf2PasswordHasher(1000), TestSupport.Clock(), NullLogger<DevelopmentDataSeeder>.Instance);

        await sut.SeedAsync(CancellationToken.None);
        await sut.SeedAsync(CancellationToken.None);

        (await db.Users.CountAsync()).Should().Be(4);
        (await db.StoreManagers.CountAsync()).Should().Be(1);
        (await db.FeedbackSubmissions.CountAsync()).Should().Be(10);
        (await db.FeedbackSubmissions.CountAsync(s => s.CustomerUserId == null)).Should().Be(1);
    }
}
