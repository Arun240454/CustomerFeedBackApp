using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;

namespace CustomerFeedback.Shared.Errors;

internal static class ProblemResponse
{
    public static async ValueTask WriteAsync(
        HttpContext context, int status, string code, string title, string? detail,
        IDictionary<string, object?>? extensions, CancellationToken ct)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
            Instance = context.Request.Path,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: ct);
    }
}

/// <summary>Known failure: request failed validation. Returns 400 with per-field errors.</summary>
public sealed class ValidationExceptionHandler(ILogger<ValidationExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not RequestValidationException ex)
        {
            return false;
        }

        logger.LogInformation("Validation failed on {Path}: {FieldCount} field(s)", context.Request.Path, ex.Errors.Count);
        await ProblemResponse.WriteAsync(context, ex.StatusCode, ex.Code, "Validation failed", ex.Message,
            new Dictionary<string, object?> { ["errors"] = ex.Errors }, ct);
        return true;
    }
}

/// <summary>Known failures: not found, conflict, forbidden, unauthorized, locked.</summary>
public sealed class AppExceptionHandler(ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not AppException ex)
        {
            return false;
        }

        logger.LogWarning("{Code} on {Method} {Path}: {Message}", ex.Code, context.Request.Method, context.Request.Path, ex.Message);

        Dictionary<string, object?>? extensions = null;
        if (ex is AccountLockedException locked)
        {
            extensions = new() { ["lockoutEndUtc"] = locked.LockoutEndUtc };
        }

        await ProblemResponse.WriteAsync(context, ex.StatusCode, ex.Code, ex.GetType().Name.Replace("Exception", string.Empty), ex.Message, extensions, ct);
        return true;
    }
}

/// <summary>Known failure: a database unique-key race (duplicate email etc.) becomes 409 instead of 500.</summary>
public sealed class DuplicateKeyExceptionHandler(ILogger<DuplicateKeyExceptionHandler> logger) : IExceptionHandler
{
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not DbUpdateException { InnerException: SqlException sql }
            || sql.Number is not (UniqueConstraintViolation or UniqueIndexViolation))
        {
            return false;
        }

        logger.LogWarning(exception, "Duplicate key on {Path}", context.Request.Path);
        await ProblemResponse.WriteAsync(context, StatusCodes.Status409Conflict, "conflict", "Conflict",
            "The record already exists.", null, ct);
        return true;
    }
}

/// <summary>Catch-all: logs the full exception and returns a standardised 500 that leaks no internals.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        logger.LogError(exception, "Unhandled exception on {Method} {Path} (trace {TraceId})",
            context.Request.Method, context.Request.Path, context.TraceIdentifier);
        await ProblemResponse.WriteAsync(context, StatusCodes.Status500InternalServerError, "internal_error",
            "Internal Server Error", "An unexpected error occurred. Quote the traceId when contacting support.", null, ct);
        return true;
    }
}

public static class ExceptionHandlingExtensions
{
    /// <summary>Handlers run in registration order: specific first, global last.</summary>
    public static IServiceCollection AddStandardExceptionHandling(this IServiceCollection services)
    {
        services.AddExceptionHandler<ValidationExceptionHandler>();
        services.AddExceptionHandler<AppExceptionHandler>();
        services.AddExceptionHandler<DuplicateKeyExceptionHandler>();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        return services;
    }
}
