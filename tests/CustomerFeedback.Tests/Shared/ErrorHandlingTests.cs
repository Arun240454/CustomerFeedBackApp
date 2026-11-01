using System.Text.Json;
using CustomerFeedback.Shared.Errors;
using CustomerFeedback.Shared.Validation;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerFeedback.Tests.Shared;

public class ExceptionHandlerTests
{
    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/test";
        return context;
    }

    private static JsonElement Body(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body).RootElement;
    }

    [Fact]
    public async Task Validation_handler_returns_400_with_field_errors()
    {
        var context = NewContext();
        var ex = new RequestValidationException(new Dictionary<string, string[]> { ["email"] = ["bad"] });

        var handled = await new ValidationExceptionHandler(NullLogger<ValidationExceptionHandler>.Instance)
            .TryHandleAsync(context, ex, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(400);
        var body = Body(context);
        body.GetProperty("code").GetString().Should().Be("validation_failed");
        body.GetProperty("errors").GetProperty("email")[0].GetString().Should().Be("bad");
        body.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(typeof(NotFoundException), 404)]
    [InlineData(typeof(ConflictException), 409)]
    [InlineData(typeof(ForbiddenException), 403)]
    [InlineData(typeof(UnauthorizedException), 401)]
    public async Task App_handler_maps_known_exceptions_to_their_status(Type type, int status)
    {
        var context = NewContext();
        var ex = (Exception)Activator.CreateInstance(type, "boom")!;

        var handled = await new AppExceptionHandler(NullLogger<AppExceptionHandler>.Instance)
            .TryHandleAsync(context, ex, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task App_handler_includes_lockout_end_for_locked_accounts()
    {
        var context = NewContext();
        var until = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await new AppExceptionHandler(NullLogger<AppExceptionHandler>.Instance)
            .TryHandleAsync(context, new AccountLockedException(until), CancellationToken.None);

        context.Response.StatusCode.Should().Be(423);
        Body(context).TryGetProperty("lockoutEndUtc", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Specific_handlers_ignore_unrelated_exceptions()
    {
        var context = NewContext();
        var other = new InvalidOperationException();

        (await new ValidationExceptionHandler(NullLogger<ValidationExceptionHandler>.Instance).TryHandleAsync(context, other, default)).Should().BeFalse();
        (await new AppExceptionHandler(NullLogger<AppExceptionHandler>.Instance).TryHandleAsync(context, other, default)).Should().BeFalse();
        (await new DuplicateKeyExceptionHandler(NullLogger<DuplicateKeyExceptionHandler>.Instance).TryHandleAsync(context, other, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Global_handler_returns_500_without_leaking_details()
    {
        var context = NewContext();

        var handled = await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
            .TryHandleAsync(context, new InvalidOperationException("secret connection string"), CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(500);
        context.Response.Body.Position = 0;
        new StreamReader(context.Response.Body).ReadToEnd().Should().NotContain("secret connection string");
    }

    [Fact]
    public async Task Global_handler_stays_silent_when_the_client_aborted()
    {
        var context = NewContext();
        using var cts = new CancellationTokenSource();
        context.RequestAborted = cts.Token;
        await cts.CancelAsync();

        var handled = await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
            .TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public void AddStandardExceptionHandling_registers_handlers_in_specific_to_global_order()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddStandardExceptionHandling();

        var handlers = services.BuildServiceProvider().GetServices<IExceptionHandler>().Select(h => h.GetType()).ToList();

        handlers.Should().ContainInOrder(typeof(ValidationExceptionHandler), typeof(AppExceptionHandler),
            typeof(DuplicateKeyExceptionHandler), typeof(GlobalExceptionHandler));
    }
}

public class ValidationExtensionsTests
{
    private sealed record Sample(string Name);

    private sealed class SampleValidator : AbstractValidator<Sample>
    {
        public SampleValidator() => RuleFor(x => x.Name).NotEmpty().WithMessage("required").MinimumLength(3).WithMessage("too short");
    }

    [Fact]
    public async Task Passes_silently_for_valid_input()
    {
        var act = () => new SampleValidator().ValidateOrThrowAsync(new Sample("abcd"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Throws_with_camel_cased_field_keys()
    {
        var act = () => new SampleValidator().ValidateOrThrowAsync(new Sample(""));

        var ex = (await act.Should().ThrowAsync<RequestValidationException>()).Which;
        ex.Errors.Should().ContainKey("name");
        ex.Errors["name"].Should().Contain("required");
    }
}
