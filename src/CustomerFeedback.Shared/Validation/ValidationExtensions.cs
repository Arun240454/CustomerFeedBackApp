using CustomerFeedback.Shared.Errors;
using FluentValidation;

namespace CustomerFeedback.Shared.Validation;

public static class ValidationExtensions
{
    /// <summary>Validates and throws <see cref="RequestValidationException"/> (mapped to HTTP 400) on failure.</summary>
    public static async Task ValidateOrThrowAsync<T>(this IValidator<T> validator, T instance, CancellationToken ct = default)
    {
        var result = await validator.ValidateAsync(instance, ct);
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(e => ToCamelCase(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        throw new RequestValidationException(errors);
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
