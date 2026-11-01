using Microsoft.AspNetCore.Http;

namespace CustomerFeedback.Shared.Errors;

/// <summary>Base for expected, client-facing failures. Each subtype maps to one HTTP status.</summary>
public abstract class AppException(string message, int statusCode, string code) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
    : AppException("One or more validation errors occurred.", StatusCodes.Status400BadRequest, "validation_failed")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class NotFoundException(string message) : AppException(message, StatusCodes.Status404NotFound, "not_found");

public sealed class ConflictException(string message) : AppException(message, StatusCodes.Status409Conflict, "conflict");

public sealed class ForbiddenException(string message) : AppException(message, StatusCodes.Status403Forbidden, "forbidden");

public sealed class UnauthorizedException(string message) : AppException(message, StatusCodes.Status401Unauthorized, "unauthorized");

public sealed class AccountLockedException(DateTime lockoutEndUtc)
    : AppException("Account is temporarily locked due to repeated failed sign-ins.", StatusCodes.Status423Locked, "account_locked")
{
    public DateTime LockoutEndUtc { get; } = lockoutEndUtc;
}
