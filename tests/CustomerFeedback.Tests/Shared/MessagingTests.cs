using CustomerFeedback.Shared.Configuration;
using CustomerFeedback.Shared.Data;
using CustomerFeedback.Shared.Domain;
using CustomerFeedback.Shared.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CustomerFeedback.Tests.Shared;

public class OutboxTests
{
    private sealed class RecordingHandler : IMessageHandler<FeedbackSubmitted>
    {
        public List<FeedbackSubmitted> Received { get; } = [];
        public bool Fail { get; set; }

        public Task HandleAsync(FeedbackSubmitted message, CancellationToken ct)
        {
            if (Fail)
            {
                throw new InvalidOperationException("handler failed");
            }

            Received.Add(message);
            return Task.CompletedTask;
        }
    }

    private static (OutboxBatchRunner Runner, RecordingHandler Handler, string DbName) Build(bool fail = false)
    {
        var dbName = Guid.NewGuid().ToString();
        var handler = new RecordingHandler { Fail = fail };
        var services = new ServiceCollection();
        services.AddSingleton(TestSupport.Clock());
        services.AddSingleton<TimeProvider>(sp => sp.GetRequiredService<Microsoft.Extensions.Time.Testing.FakeTimeProvider>());
        services.AddDbContext<FeedbackDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<CustomerFeedback.Shared.Security.IFieldEncryptor>(TestSupport.Encryptor());
        services.AddSingleton(handler);
        var sp = services.BuildServiceProvider();

        var dispatcher = new MessageDispatcher();
        dispatcher.Register<FeedbackSubmitted, RecordingHandler>();
        var runner = new OutboxBatchRunner(sp.GetRequiredService<IServiceScopeFactory>(), dispatcher,
            Options.Create(new OutboxOptions { MaxAttempts = 2, BatchSize = 10 }), TestSupport.Clock(),
            NullLogger<OutboxBatchRunner>.Instance);
        return (Runner: runner, Handler: handler, DbName: dbName);
    }

    private static FeedbackDbContext Open(string name) => TestSupport.NewDb(name);

    [Fact]
    public void Publisher_stages_a_serialised_outbox_row()
    {
        using var db = TestSupport.NewDb();
        var sut = new OutboxEventPublisher(db, TestSupport.Clock());

        sut.Publish(new FeedbackSubmitted(5, 1, TestSupport.Now.UtcDateTime));
        db.SaveChanges();

        var row = db.OutboxMessages.Single();
        row.MessageType.Should().Be(nameof(FeedbackSubmitted));
        row.Payload.Should().Contain("\"SubmissionId\":5");
        row.ProcessedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Runner_dispatches_pending_messages_and_marks_them_processed()
    {
        var (runner, handler, dbName) = Build();
        using (var seed = Open(dbName))
        {
            new OutboxEventPublisher(seed, TestSupport.Clock()).Publish(new FeedbackSubmitted(9, 1, TestSupport.Now.UtcDateTime));
            await seed.SaveChangesAsync();
        }

        (await runner.RunOnceAsync(CancellationToken.None)).Should().Be(1);
        (await runner.RunOnceAsync(CancellationToken.None)).Should().Be(0);

        handler.Received.Should().ContainSingle().Which.SubmissionId.Should().Be(9);
        using var check = Open(dbName);
        check.OutboxMessages.Single().ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Runner_records_failures_and_stops_after_max_attempts()
    {
        var (runner, _, dbName) = Build(fail: true);
        using (var seed = Open(dbName))
        {
            new OutboxEventPublisher(seed, TestSupport.Clock()).Publish(new FeedbackSubmitted(1, 1, TestSupport.Now.UtcDateTime));
            await seed.SaveChangesAsync();
        }

        await runner.RunOnceAsync(CancellationToken.None);
        await runner.RunOnceAsync(CancellationToken.None);
        await runner.RunOnceAsync(CancellationToken.None);

        using var check = Open(dbName);
        var row = check.OutboxMessages.Single();
        row.Attempts.Should().Be(2);
        row.LastError.Should().Be("handler failed");
        row.ProcessedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Runner_leaves_messages_it_cannot_route()
    {
        var (runner, handler, dbName) = Build();
        using (var seed = Open(dbName))
        {
            seed.OutboxMessages.Add(new OutboxMessage { MessageType = "SomethingElse", Payload = "{}" });
            await seed.SaveChangesAsync();
        }

        (await runner.RunOnceAsync(CancellationToken.None)).Should().Be(0);

        handler.Received.Should().BeEmpty();
        using var check = Open(dbName);
        check.OutboxMessages.Single().ProcessedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Dispatcher_rejects_empty_payloads_and_reports_known_types()
    {
        var dispatcher = new MessageDispatcher();
        dispatcher.Register<FeedbackSubmitted, RecordingHandler>();
        var sp = new ServiceCollection().AddSingleton<RecordingHandler>().BuildServiceProvider();

        dispatcher.CanHandle(nameof(FeedbackSubmitted)).Should().BeTrue();
        dispatcher.CanHandle("Nope").Should().BeFalse();
        var act = () => dispatcher.DispatchAsync(sp, nameof(FeedbackSubmitted), "null", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void AddOutboxConsumer_and_publisher_register_services()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Outbox:BatchSize"] = "3" }).Build();

        services.AddOutboxPublisher().AddOutboxConsumer(config, r => r.Register<FeedbackSubmitted, RecordingHandler>());

        services.Should().Contain(d => d.ServiceType == typeof(IEventPublisher));
        services.Should().Contain(d => d.ServiceType == typeof(IMessageDispatcher));
        services.Should().Contain(d => d.ServiceType == typeof(OutboxBatchRunner));
    }

    [Fact]
    public async Task Processor_polls_until_stopped()
    {
        var calls = 0;
        var runnerMock = new Mock<IServiceScopeFactory>();
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(() =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("db down");
        });
        runnerMock.Setup(f => f.CreateScope()).Returns(scope.Object);

        var runner = new OutboxBatchRunner(runnerMock.Object, Mock.Of<IMessageDispatcher>(),
            Options.Create(new OutboxOptions()), TestSupport.Clock(), NullLogger<OutboxBatchRunner>.Instance);
        var sut = new OutboxProcessor(runner, Options.Create(new OutboxOptions { PollSeconds = 1 }), NullLogger<OutboxProcessor>.Instance);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await sut.StopAsync(CancellationToken.None);

        calls.Should().BeGreaterThan(0);
    }
}

public class DatabaseConfigurationTests
{
    [Fact]
    public void Provider_without_connection_string_loads_nothing()
    {
        var provider = new DatabaseConfigurationProvider(null);

        provider.Load();

        provider.TryGet("Security:MaxFailedLogins", out _).Should().BeFalse();
    }

    [Fact]
    public void Provider_tolerates_unreachable_database()
    {
        var provider = new DatabaseConfigurationProvider("Server=127.0.0.1,1;Database=x;Connect Timeout=1;Encrypt=False");

        var act = provider.Load;

        act.Should().NotThrow();
    }

    [Fact]
    public void Source_builds_a_provider()
    {
        new DatabaseConfigurationSource(null).Build(new ConfigurationBuilder()).Should().BeOfType<DatabaseConfigurationProvider>();
    }
}
