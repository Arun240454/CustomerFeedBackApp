using System.Text.RegularExpressions;
using CustomerFeedback.Shared.Domain;
using FluentValidation;

namespace CustomerFeedback.Identity.Api.Contracts;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? Role);

public sealed record LoginRequest(string Email, string Password);

public sealed record UserDto(int UserId, string Email, string FirstName, string LastName, string Role);

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, UserDto User);

public sealed partial class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    [GeneratedRegex(@"^[\p{L}\p{M}][\p{L}\p{M}' .-]*$", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"^\+?[0-9 ()-]{7,20}$", RegexOptions.None, matchTimeoutMilliseconds: 250)]
    private static partial Regex PhoneRegex();

    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(10).WithMessage("Password must be at least 10 characters.")
            .MaximumLength(128)
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");

        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100).Matches(NameRegex()).WithMessage("First name contains invalid characters.");
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100).Matches(NameRegex()).WithMessage("Last name contains invalid characters.");
        RuleFor(x => x.PhoneNumber).Matches(PhoneRegex()).When(x => !string.IsNullOrEmpty(x.PhoneNumber))
            .WithMessage("Phone number must be 7-20 digits and may include +, spaces, brackets and dashes.");

        RuleFor(x => x.Role)
            .Must(r => r is null or RoleNames.Customer or RoleNames.StoreManager)
            .WithMessage("Role must be Customer or StoreManager.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}
