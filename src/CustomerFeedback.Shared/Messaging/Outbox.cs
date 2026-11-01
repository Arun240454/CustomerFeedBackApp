using System.Text.Json;
using CustomerFeedback.Shared.Data;
using CustomerFeedback.Shared.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerFeedback.Shared.Messaging;

/// <summary>Raised by the Feedback service once a submission is stored; consumed to categorise the feedback.</summary>
public sealed record FeedbackSubmitted(long SubmissionId, int StoreId, DateTime SubmittedAtUtc);

public interface IMessageHandler<in TMessage>
{
    Task HandleAsync(TMessage message, CancellationToken ct);
}

public interface IEventPublisher
{
    /// <summary>Stages the event in the current unit of work; it is committed atomically with the caller's SaveChanges.</summary>
    void Publish<TMessage>(TMessage message) where TMessage : class;
}

public sealed class OutboxEventPublisher(FeedbackDbContext db, TimeProvider clock) : IEventPublisher
{
    public void Publish<TMessage>(TMessage message) where TMessage : class
    {
        db.OutboxMessages.Add(new OutboxMessage
        {
            MessageType = typeof(TMessage).Name,
            Payload = JsonSerializer.Serialize(message),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        });
    }
}

public interface IMessageDispatcher
{
    bool CanHandle(string messageType);
    Task DispatchAsync(IServiceProvider scopedServices, string messageType, string payload, CancellationToken ct);
}

public sealed class MessageDispatcher : IMessageDispatcher
{
    private readonly Dictionary<string, Func<IServiceProvider, string, CancellationToken, Task>> _routes = [];

    public void Register<TMessage, THandler>()
        where TMessage : class
        where THandler : IMessageHandler<TMessage>
    {
        _routes[typeof(TMessage).Name] = (sp, payload, ct) =>
        {
            var message = JsonSerializer.Deserialize<TMessage>(payload)
                ?? throw new InvalidOperationException($"Payload for {typeof(TMessage).Name} is empty.");
            return sp.GetRequiredService<THandler>().HandleAsync(message, ct);
        };
    }

    public bool CanHandle(string messageType) => _routes.ContainsKey(messageType);

    public Task DispatchAsync(IServiceProvider scopedServices, string messageType, string payload, CancellationToken ct) =>
        _routes[messageType](scopedServices, payload, ct);
}

public class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int BatchSize { get; set; } = 20;
    public int PollSeconds { get; set; } = 2;
    public int MaxAttempts { get; set; } = 5;
}

/// <summary>Claims a batch of pending outbox rows and runs their handlers. Safe with several competing consumers.</summary>
public sealed class OutboxBatchRunner(
    IServiceScopeFactory scopeFactory,
    IMessageDispatcher dispatcher,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxBatchRunner> logger)
{
    private readonly OutboxOptions _options = options.Value;

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FeedbackDbContext>();
        var relational = db.Database.IsRelational();
        var handled = 0;

        // The execution strategy retries the whole unit (claim, handle, commit) on transient SQL failures.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            db.ChangeTracker.Clear();
            await using var tx = relational ? await db.Database.BeginTransactionAsync(token) : null;

            var pending = await LoadPendingAsync(db, relational, token);
            var succeeded = 0;
            foreach (var message in pending)
            {
                if (await ProcessAsync(message, token))
                {
                    succeeded++;
                }
            }

            await db.SaveChangesAsync(token);
            if (tx is not null)
            {
                await tx.CommitAsync(token);
            }

            handled = succeeded;
        }, ct);

        return handled;
    }

    private Task<List<OutboxMessage>> LoadPendingAsync(FeedbackDbContext db, bool relational, CancellationToken ct)
    {
        if (relational)
        {
            // UPDLOCK + READPAST lets several consumers poll without ever claiming the same row.
            return db.OutboxMessages
                .FromSqlInterpolated($"""
                    SELECT TOP ({_options.BatchSize}) * FROM dbo.OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE ProcessedAtUtc IS NULL AND Attempts < {_options.MaxAttempts}
                    ORDER BY OutboxMessageId
                    """)
                .ToListAsync(ct);
        }

        return db.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null && m.Attempts < _options.MaxAttempts)
            .OrderBy(m => m.OutboxMessageId)
            .Take(_options.BatchSize)
            .ToListAsync(ct);
    }

    private async Task<bool> ProcessAsync(OutboxMessage message, CancellationToken ct)
    {
        if (!dispatcher.CanHandle(message.MessageType))
        {
            // Another service owns this message type; leave it for that consumer.
            return false;
        }

        try
        {
            await using var handlerScope = scopeFactory.CreateAsyncScope();
            await dispatcher.DispatchAsync(handlerScope.ServiceProvider, message.MessageType, message.Payload, ct);
            message.ProcessedAtUtc = clock.GetUtcNow().UtcDateTime;
            message.LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            message.Attempts++;
            message.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            logger.LogError(ex, "Outbox message {MessageId} ({MessageType}) failed on attempt {Attempt}",
                message.OutboxMessageId, message.MessageType, message.Attempts);
            return false;
        }
    }
}

public sealed class OutboxProcessor(OutboxBatchRunner runner, IOptions<OutboxOptions> options, ILogger<OutboxProcessor> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds));
        logger.LogInformation("Outbox processor started, polling every {Delay}", delay);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var handled = await runner.RunOnceAsync(stoppingToken);
                if (handled > 0)
                {
                    continue;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox polling iteration failed");
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

public static class MessagingExtensions
{
    public static IServiceCollection AddOutboxPublisher(this IServiceCollection services)
    {
        services.AddScoped<IEventPublisher, OutboxEventPublisher>();
        return services;
    }

    /// <summary>Runs the polling consumer and routes each message type to its handler.</summary>
    public static IServiceCollection AddOutboxConsumer(
        this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration,
        Action<MessageDispatcher> routes)
    {
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        var dispatcher = new MessageDispatcher();
        routes(dispatcher);
        services.AddSingleton<IMessageDispatcher>(dispatcher);
        services.AddSingleton<OutboxBatchRunner>();
        services.AddHostedService<OutboxProcessor>();
        return services;
    }
}
