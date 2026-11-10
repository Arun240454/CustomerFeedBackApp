using CustomerFeedback.Identity.Api.Contracts;
using CustomerFeedback.Shared.Data;
using CustomerFeedback.Shared.Domain;
using CustomerFeedback.Shared.Errors;
using CustomerFeedback.Shared.Security;
using CustomerFeedback.Shared.Validation;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CustomerFeedback.Identity.Api.Services;

public class LoginSecurityOptions
{
    public const string SectionName = "Security";

    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public interface IAuthService
{
    Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<IReadOnlyList<StoreOptionDto>> GetStoreOptionsAsync(CancellationToken ct);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<UserDto> GetProfileAsync(int userId, CancellationToken ct);
}

public sealed class AuthService(
    FeedbackDbContext db,
    IPasswordHasher hasher,
    IJwtTokenService tokens,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator,
    IOptionsSnapshot<LoginSecurityOptions> securityOptions,
    TimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidCredentials = "Invalid email or password.";

    // Verified when the email is unknown so the response time does not reveal whether an account exists.
    private static readonly string DummyHash = new Pbkdf2PasswordHasher(1000).Hash("timing-equaliser");

    private readonly LoginSecurityOptions _security = securityOptions.Value;

    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var clean = request with
        {
            Email = InputSanitizer.CleanEmail(request.Email) ?? string.Empty,
            FirstName = InputSanitizer.CleanRequired(request.FirstName),
            LastName = InputSanitizer.CleanRequired(request.LastName),
            PhoneNumber = InputSanitizer.Clean(request.PhoneNumber),
            Role = InputSanitizer.Clean(request.Role),
        };
        await registerValidator.ValidateOrThrowAsync(clean, ct);

        if (await db.Users.AnyAsync(u => u.Email == clean.Email, ct))
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var roleName = clean.Role ?? RoleNames.Customer;
        var isManager = roleName == RoleNames.StoreManager;
        if (isManager && !await db.Stores.AnyAsync(s => s.StoreId == clean.StoreId && s.IsActive, ct))
        {
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                ["storeId"] = ["The selected store was not found."],
            });
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var user = new User
        {
            Email = clean.Email,
            PasswordHash = hasher.Hash(clean.Password),
            FirstName = clean.FirstName,
            LastName = clean.LastName,
            PhoneNumber = clean.PhoneNumber,
            RoleId = isManager ? RoleIds.StoreManager : RoleIds.Customer,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        if (isManager)
        {
            await AssignToStoreAsync(user, clean.StoreId!.Value, now);
        }

        logger.LogInformation("User {UserId} registered with role {Role}", user.UserId, roleName);
        return new UserDto(user.UserId, user.Email, user.FirstName, user.LastName, roleName);
    }

    public async Task<IReadOnlyList<StoreOptionDto>> GetStoreOptionsAsync(CancellationToken ct) =>
        await db.Stores.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.StoreName)
            .Select(s => new StoreOptionDto(s.StoreId, s.StoreCode, s.StoreName, s.City))
            .ToListAsync(ct);

    /// <summary>Links a new manager to their store. If that fails the user row is removed so no manager is left without a store.</summary>
    private async Task AssignToStoreAsync(User user, int storeId, DateTime now)
    {
        var assignment = new StoreManager { StoreId = storeId, UserId = user.UserId, AssignedAtUtc = now };
        try
        {
            db.StoreManagers.Add(assignment);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            db.Entry(assignment).State = EntityState.Detached;
            db.Users.Remove(user);
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var clean = request with { Email = InputSanitizer.CleanEmail(request.Email) ?? string.Empty };
        await loginValidator.ValidateOrThrowAsync(clean, ct);

        var user = await db.Users.Include(u => u.Role).SingleOrDefaultAsync(u => u.Email == clean.Email, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        if (user is null)
        {
            hasher.Verify(clean.Password, DummyHash);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (user.LockoutEndUtc is { } lockoutEnd && lockoutEnd > now)
        {
            logger.LogWarning("Sign-in rejected for locked user {UserId}", user.UserId);
            throw new AccountLockedException(lockoutEnd);
        }

        if (!user.IsActive || !hasher.Verify(clean.Password, user.PasswordHash))
        {
            await RecordFailureAsync(user, now, ct);
            throw new UnauthorizedException(InvalidCredentials);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginUtc = now;
        user.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);

        var roleName = user.Role!.RoleName;
        var token = tokens.CreateToken(user.UserId, user.Email, roleName);
        logger.LogInformation("User {UserId} signed in", user.UserId);
        return new AuthResponse(token.Value, token.ExpiresAtUtc,
            new UserDto(user.UserId, user.Email, user.FirstName, user.LastName, roleName));
    }

    public async Task<UserDto> GetProfileAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.Role)
            .Where(u => u.UserId == userId && u.IsActive)
            .Select(u => new UserDto(u.UserId, u.Email, u.FirstName, u.LastName, u.Role!.RoleName))
            .SingleOrDefaultAsync(ct);
        return user ?? throw new NotFoundException("User not found.");
    }

    private async Task RecordFailureAsync(User user, DateTime now, CancellationToken ct)
    {
        user.FailedLoginCount++;
        user.UpdatedAtUtc = now;
        if (user.FailedLoginCount >= _security.MaxFailedLogins)
        {
            user.LockoutEndUtc = now.AddMinutes(_security.LockoutMinutes);
            user.FailedLoginCount = 0;
            logger.LogWarning("User {UserId} locked until {LockoutEnd} after repeated failed sign-ins", user.UserId, user.LockoutEndUtc);
        }

        await db.SaveChangesAsync(ct);
    }
}
