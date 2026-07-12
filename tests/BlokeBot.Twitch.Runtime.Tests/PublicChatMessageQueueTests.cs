using System.Collections.Immutable;
using System.Threading.Channels;
using BlokeBot.Eventing;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class PublicChatMessageQueueTests
{
    [Test]
    public void MessageWithMultipleBreakTypes_Splitting_PrefersLineSentenceThenWord()
    {
        TwitchChatMessageSplitter
            .Split("first line\nsecond line", 12)
            .ShouldBe(["first line", "second line"]);
        TwitchChatMessageSplitter
            .Split("First sentence. Second one.", 20)
            .ShouldBe(["First sentence.", "Second one."]);
        TwitchChatMessageSplitter.Split("alpha beta gamma", 10).ShouldBe(["alpha", "beta gamma"]);
    }

    [Test]
    public async Task MessageOverLength_Enqueueing_PersistsEveryPartBeforeDelivery()
    {
        var outbox = new InMemoryOutbox();
        var transport = new RecordingTransport();
        var queue = CreateQueue(
            new TwitchBotOptions
            {
                ChatMessageSendIntervalSeconds = 0,
                DuplicateChatMessageCooldownSeconds = 0,
                MaxChatMessageLength = 10,
            },
            outbox,
            transport
        );

        var receipt = await queue.EnqueueAsync(
            Command("channel", "alpha beta gamma"),
            CancellationToken.None
        );

        receipt.MessageIds.Length.ShouldBe(2);
        outbox.PendingMessages.ShouldBe(["alpha", "beta gamma"]);
        transport.Deliveries.ShouldBeEmpty();

        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);
        (await transport.ReadAsync()).Message.ShouldBe("alpha");
        (await transport.ReadAsync()).Message.ShouldBe("beta gamma");
        await StopAsync(stopping, worker);
    }

    [Test]
    public async Task DuplicateAndDistinctMessages_Processing_DelaysOnlyDuplicate()
    {
        var clock = new ManualTimeProvider(Utc(12, 0, 0));
        var outbox = new InMemoryOutbox();
        var transport = new RecordingTransport();
        var queue = CreateQueue(
            new TwitchBotOptions
            {
                ChatMessageSendIntervalSeconds = 0,
                DuplicateChatMessageCooldownSeconds = 1,
            },
            outbox,
            transport,
            clock
        );
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        _ = await queue.EnqueueAsync(Command("channel", "same"), CancellationToken.None);
        (await transport.ReadAsync()).Message.ShouldBe("same");
        _ = await queue.EnqueueAsync(Command("channel", "same"), CancellationToken.None);
        _ = await queue.EnqueueAsync(
            Command("channel", "different"),
            CancellationToken.None
        );

        (await transport.ReadAsync()).Message.ShouldBe("different");
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        (await transport.ReadAsync()).Message.ShouldBe("same");
        await StopAsync(stopping, worker);
    }

    [Test]
    public async Task RepeatedAndLaterBackups_MonitoringPublicChatQueue_AlertsOncePerIncident()
    {
        var clock = new ManualTimeProvider(Utc(12, 0, 0));
        var outbox = new InMemoryOutbox();
        var transport = new RecordingTransport();
        var observer = new RecordingQueueAlertObserver();
        var queue = CreateQueue(
            new TwitchBotOptions
            {
                ChatMessageSendIntervalSeconds = 10,
                DuplicateChatMessageCooldownSeconds = 0,
                PublicChatQueueAlerts = new PublicChatQueueAlertOptions
                {
                    StuckAfterSeconds = 5,
                },
            },
            outbox,
            transport,
            clock,
            [observer]
        );
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        _ = await queue.EnqueueAsync(Command("channel", "first"), CancellationToken.None);
        (await transport.ReadAsync()).Message.ShouldBe("first");
        _ = await queue.EnqueueAsync(Command("channel", "second"), CancellationToken.None);
        _ = await queue.EnqueueAsync(Command("channel", "third"), CancellationToken.None);

        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        var firstAlert = await observer.ReadAsync();
        firstAlert.Channel.ShouldBe("channel");
        firstAlert.OldestPendingAge.ShouldBe(TimeSpan.FromSeconds(5));
        firstAlert.PendingCount.ShouldBe(2);

        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        (await transport.ReadAsync()).Message.ShouldBe("second");
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(10));
        (await transport.ReadAsync()).Message.ShouldBe("third");
        observer.Alerts.Count.ShouldBe(1);

        _ = await queue.EnqueueAsync(Command("channel", "fourth"), CancellationToken.None);
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        _ = await observer.ReadAsync();
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        (await transport.ReadAsync()).Message.ShouldBe("fourth");
        observer.Alerts.Count.ShouldBe(2);
        await StopAsync(stopping, worker);
    }

    [Test]
    public void BacklogsAcrossChannels_CapturingAlerts_TracksIndependentlyAndResetsDrained()
    {
        var monitor = new PublicChatQueueBacklogMonitor();
        var now = Utc(12, 0, 10);
        PublicChatPendingMessage[] pending =
        [
            new("first", now.AddSeconds(-10)),
            new("first", now.AddSeconds(-6)),
            new("second", now.AddSeconds(-7)),
        ];

        var firstIncidents = monitor.CaptureAlerts(
            pending,
            now,
            TimeSpan.FromSeconds(5),
            enabled: true
        );
        firstIncidents.Select(alert => alert.Channel).ShouldBe(["first", "second"]);
        monitor
            .CaptureAlerts(pending, now, TimeSpan.FromSeconds(5), enabled: true)
            .ShouldBeEmpty();

        monitor.ResetDrainedChannels([new("second", now)]);
        var nextFirstIncident = monitor.CaptureAlerts(
            [new("first", now.AddSeconds(-5)), new("second", now.AddSeconds(-5))],
            now,
            TimeSpan.FromSeconds(5),
            enabled: true
        );
        nextFirstIncident.Select(alert => alert.Channel).ShouldBe(["first"]);
    }

    [Test]
    public async Task FailingPublicChatQueueAlertObserver_DispatchingAlert_NotifiesRemainingObservers()
    {
        var recording = new RecordingQueueAlertObserver();
        var dispatcher = new PublicChatQueueAlertDispatcher(
            [new ThrowingQueueAlertObserver("Observer failed."), recording],
            QueueAlertFanOut()
        );
        var alert = new PublicChatQueueBacklog(
            "channel",
            2,
            TimeSpan.FromSeconds(5),
            Utc(12, 0, 0)
        );

        await dispatcher.NotifyAsync([alert], CancellationToken.None);

        recording.Alerts.ShouldBe([alert]);
    }

    [Test]
    public async Task AlertHandlingEscalation_ProcessingPublicChatQueue_ContinuesDelivery()
    {
        var clock = new ManualTimeProvider(Utc(12, 0, 0));
        var outbox = new InMemoryOutbox();
        var transport = new RecordingTransport();
        var logger = new RecordingLogger<PublicChatMessageQueue>();
        var queue = CreateQueue(
            new TwitchBotOptions
            {
                ChatMessageSendIntervalSeconds = 10,
                DuplicateChatMessageCooldownSeconds = 0,
                PublicChatQueueAlerts = new PublicChatQueueAlertOptions
                {
                    StuckAfterSeconds = 5,
                },
            },
            outbox,
            transport,
            clock,
            [new ThrowingQueueAlertObserver("observer secret payload")],
            RuntimeTestObserverFanOut.EscalatingContinue<
                PublicChatQueueAlertObserverBoundary,
                PublicChatQueueBacklog,
                PublicChatQueueAlertDeadLetter
            >(
                TwitchBotObserverBoundaries.PublicChatQueueAlerts,
                new IOException("reporter secret payload")
            ),
            logger
        );
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);

        _ = await queue.EnqueueAsync(Command("channel", "first"), CancellationToken.None);
        _ = await transport.ReadAsync();
        _ = await queue.EnqueueAsync(
            Command("channel", "second secret chat payload"),
            CancellationToken.None
        );
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(10));
        (await transport.ReadAsync()).Message.ShouldBe("second secret chat payload");

        logger.Entries.ShouldNotBeEmpty();
        foreach (var entry in logger.Entries)
        {
            entry.Exception.ShouldBeNull();
            entry.Message.ShouldContain("Continuing queued chat processing");
            entry.Message.ShouldNotContain("observer secret payload");
            entry.Message.ShouldNotContain("reporter secret payload");
            entry.Message.ShouldNotContain("second secret chat payload");
        }
        await StopAsync(stopping, worker);
    }

    [Test]
    public async Task PersistenceFailure_Enqueueing_IsObservableWithoutDelivery()
    {
        var outbox = new InMemoryOutbox
        {
            EnqueueFailure = new IOException("Persistence unavailable."),
        };
        var transport = new RecordingTransport();
        var queue = CreateQueue(new TwitchBotOptions(), outbox, transport);

        await Should.ThrowAsync<IOException>(() =>
            queue
                .EnqueueAsync(Command("channel", "message"), CancellationToken.None)
                .AsTask()
        );

        outbox.PendingMessages.ShouldBeEmpty();
        transport.Deliveries.ShouldBeEmpty();
    }

    [Test]
    public async Task CallerCanceledAfterCommit_Enqueueing_LeavesDeliveryRecoverable()
    {
        using var caller = new CancellationTokenSource();
        var outbox = new InMemoryOutbox { AfterEnqueue = caller.Cancel };
        var transport = new RecordingTransport();
        var queue = CreateQueue(new TwitchBotOptions(), outbox, transport);

        var receipt = await queue.EnqueueAsync(Command("channel", "message"), caller.Token);

        receipt.MessageIds.Length.ShouldBe(1);
        caller.IsCancellationRequested.ShouldBeTrue();
        using var stopping = new CancellationTokenSource();
        var worker = queue.RunAsync(stopping.Token);
        (await transport.ReadAsync()).Message.ShouldBe("message");
        await StopAsync(stopping, worker);
    }

    private static PublicChatMessageQueue CreateQueue(
        TwitchBotOptions options,
        IPublicChatOutbox outbox,
        IPublicChatTransport transport,
        TimeProvider? timeProvider = null,
        IEnumerable<IPublicChatQueueAlertObserver>? observers = null,
        ObserverFanOut<
            PublicChatQueueAlertObserverBoundary,
            PublicChatQueueBacklog,
            PublicChatQueueAlertDeadLetter
        >? fanOut = null,
        ILogger<PublicChatMessageQueue>? logger = null
    ) =>
        new(
            TwitchBotSettings.FromOptions(options),
            timeProvider ?? TimeProvider.System,
            new PublicChatQueueBacklogMonitor(),
            new PublicChatQueueAlertDispatcher(
                observers ?? [],
                fanOut ?? QueueAlertFanOut()
            ),
            outbox,
            transport,
            logger ?? NullLogger<PublicChatMessageQueue>.Instance
        );

    private static PublicChatEnqueueCommand Command(
        string channel,
        string message
    ) =>
        new() { Channel = channel, Message = message };

    private static ObserverFanOut<
        PublicChatQueueAlertObserverBoundary,
        PublicChatQueueBacklog,
        PublicChatQueueAlertDeadLetter
    > QueueAlertFanOut() =>
        RuntimeTestObserverFanOut.Continue<
            PublicChatQueueAlertObserverBoundary,
            PublicChatQueueBacklog,
            PublicChatQueueAlertDeadLetter
        >(TwitchBotObserverBoundaries.PublicChatQueueAlerts);

    private static async Task StopAsync(
        CancellationTokenSource stopping,
        Task worker
    )
    {
        await stopping.CancelAsync();
        await worker;
    }

    private static DateTimeOffset Utc(int hour, int minute, int second) =>
        new(2026, 7, 12, hour, minute, second, TimeSpan.Zero);

    private sealed class RecordingTransport : IPublicChatTransport
    {
        private readonly Channel<PublicChatClaimedMessage> delivered =
            Channel.CreateUnbounded<PublicChatClaimedMessage>();

        public List<PublicChatClaimedMessage> Deliveries { get; } = [];

        public ValueTask SendAsync(
            PublicChatClaimedMessage message,
            CancellationToken cancellationToken
        )
        {
            Deliveries.Add(message);
            if (!delivered.Writer.TryWrite(message))
                throw new InvalidOperationException("The transport delivery could not be observed.");

            return ValueTask.CompletedTask;
        }

        public ValueTask<PublicChatClaimedMessage> ReadAsync() => delivered.Reader.ReadAsync();
    }

    private sealed class RecordingQueueAlertObserver : IPublicChatQueueAlertObserver
    {
        private readonly Channel<PublicChatQueueBacklog> alerts =
            Channel.CreateUnbounded<PublicChatQueueBacklog>();

        public List<PublicChatQueueBacklog> Alerts { get; } = [];

        public ValueTask QueueBackedUpAsync(
            PublicChatQueueBacklog backlog,
            CancellationToken cancellationToken
        )
        {
            Alerts.Add(backlog);
            if (!alerts.Writer.TryWrite(backlog))
                throw new InvalidOperationException("The queue alert could not be observed.");

            return ValueTask.CompletedTask;
        }

        public ValueTask<PublicChatQueueBacklog> ReadAsync() => alerts.Reader.ReadAsync();
    }

    private sealed class ThrowingQueueAlertObserver(string failureMessage)
        : IPublicChatQueueAlertObserver
    {
        public ValueTask QueueBackedUpAsync(
            PublicChatQueueBacklog backlog,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException(failureMessage);
    }

    private sealed class InMemoryOutbox : IPublicChatOutbox
    {
        private readonly object gate = new();
        private readonly List<Row> rows = [];
        private long nextId = 1;

        public Action? AfterEnqueue { get; init; }

        public Exception? EnqueueFailure { get; init; }

        public IReadOnlyList<string> PendingMessages
        {
            get
            {
                lock (gate)
                {
                    return rows
                        .Where(row => row.Status == RowStatus.Pending)
                        .Select(row => row.Message!)
                        .ToArray();
                }
            }
        }

        public ValueTask<PublicChatOutboxReceipt> EnqueueAsync(
            PublicChatOutboxBatch batch,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (EnqueueFailure is { } failure)
                throw failure;

            long[] ids;
            lock (gate)
            {
                ids = batch.Items
                    .Select(item =>
                    {
                        var id = nextId++;
                        rows.Add(new Row(id, batch.Channel, item, batch.EnqueuedAt));
                        return id;
                    })
                    .ToArray();
            }

            AfterEnqueue?.Invoke();
            return ValueTask.FromResult(
                new PublicChatOutboxReceipt(ImmutableArray.Create(ids))
            );
        }

        public ValueTask<PublicChatClaimOutcome> TryClaimNextAsync(
            DateTimeOffset now,
            DateTimeOffset claimExpiresAt,
            TimeSpan sendInterval,
            TimeSpan duplicateCooldown,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                var active = rows.FirstOrDefault(row =>
                    row.Status is RowStatus.Claimed or RowStatus.Sending
                );
                if (active is not null)
                {
                    return ValueTask.FromResult<PublicChatClaimOutcome>(
                        new PublicChatClaimOutcome.AwaitingAvailability(active.ClaimExpiresAt)
                    );
                }

                var previousAttempt = rows
                    .Where(row => row.CompletedAt is not null)
                    .Select(row => row.CompletedAt!.Value)
                    .DefaultIfEmpty(DateTimeOffset.MinValue)
                    .Max();
                var pending = rows
                    .Where(row => row.Status == RowStatus.Pending)
                    .Select(row =>
                    {
                        var eligibleAt = row.NextAttemptAt;
                        if (previousAttempt != DateTimeOffset.MinValue)
                            eligibleAt = Max(eligibleAt, previousAttempt + sendInterval);
                        var previousDelivery = rows
                            .Where(other =>
                                other.Status == RowStatus.Delivered
                                && other.Item.DeduplicationKey
                                    == row.Item.DeduplicationKey
                            )
                            .Select(other => other.CompletedAt!.Value)
                            .DefaultIfEmpty(DateTimeOffset.MinValue)
                            .Max();
                        if (previousDelivery != DateTimeOffset.MinValue)
                        {
                            eligibleAt = Max(
                                eligibleAt,
                                previousDelivery + duplicateCooldown
                            );
                        }

                        return new Candidate(row, eligibleAt);
                    })
                    .OrderBy(candidate => candidate.EligibleAt)
                    .ThenBy(candidate => candidate.Row.EnqueuedAt)
                    .ThenBy(candidate => candidate.Row.Id)
                    .FirstOrDefault();
                if (pending is null)
                {
                    return ValueTask.FromResult<PublicChatClaimOutcome>(
                        new PublicChatClaimOutcome.Empty()
                    );
                }

                if (pending.EligibleAt > now)
                {
                    return ValueTask.FromResult<PublicChatClaimOutcome>(
                        new PublicChatClaimOutcome.AwaitingAvailability(pending.EligibleAt)
                    );
                }

                var token = new PublicChatClaimToken(Guid.NewGuid());
                pending.Row.Status = RowStatus.Claimed;
                pending.Row.ClaimToken = token;
                pending.Row.ClaimExpiresAt = claimExpiresAt;
                return ValueTask.FromResult<PublicChatClaimOutcome>(
                    new PublicChatClaimOutcome.Claimed(pending.Row.Claimed(token))
                );
            }
        }

        public ValueTask<PublicChatClaimUpdate> BeginSendAsync(
            PublicChatClaimedMessage message,
            DateTimeOffset sendStartedAt,
            DateTimeOffset claimExpiresAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                var row = Owned(message, RowStatus.Claimed);
                if (row is null)
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );

                row.Status = RowStatus.Sending;
                row.AttemptCount++;
                row.ClaimExpiresAt = claimExpiresAt;
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        public ValueTask<PublicChatClaimUpdate> MarkDeliveredAsync(
            PublicChatClaimedMessage message,
            DateTimeOffset deliveredAt,
            CancellationToken cancellationToken
        ) => Complete(message, RowStatus.Delivered, deliveredAt, cancellationToken);

        public ValueTask<PublicChatClaimUpdate> MarkFaultedAsync(
            PublicChatClaimedMessage message,
            DateTimeOffset faultedAt,
            CancellationToken cancellationToken
        ) => Complete(message, RowStatus.Faulted, faultedAt, cancellationToken);

        public ValueTask<PublicChatClaimUpdate> ReleaseClaimAsync(
            PublicChatClaimedMessage message,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                var row = Owned(message, RowStatus.Claimed);
                if (row is null)
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );

                row.Status = RowStatus.Pending;
                row.ClaimToken = null;
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        public ValueTask<IReadOnlyList<PublicChatPendingMessage>> LoadOutstandingAsync(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                IReadOnlyList<PublicChatPendingMessage> pending = rows
                    .Where(row => row.Status is RowStatus.Pending or RowStatus.Claimed or RowStatus.Sending)
                    .OrderBy(row => row.EnqueuedAt)
                    .ThenBy(row => row.Id)
                    .Select(row => new PublicChatPendingMessage(row.Channel, row.EnqueuedAt))
                    .ToArray();
                return ValueTask.FromResult(pending);
            }
        }

        private ValueTask<PublicChatClaimUpdate> Complete(
            PublicChatClaimedMessage message,
            RowStatus status,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                var row = Owned(message, RowStatus.Sending);
                if (row is null)
                    return ValueTask.FromResult<PublicChatClaimUpdate>(
                        new PublicChatClaimUpdate.OwnershipLost()
                    );

                row.Status = status;
                row.CompletedAt = completedAt;
                row.Message = null;
                row.ClaimToken = null;
                return ValueTask.FromResult<PublicChatClaimUpdate>(
                    new PublicChatClaimUpdate.Applied()
                );
            }
        }

        private Row? Owned(PublicChatClaimedMessage message, RowStatus status) =>
            rows.SingleOrDefault(row =>
                row.Id == message.Id
                && row.Status == status
                && row.ClaimToken == message.ClaimToken
            );

        private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
            left >= right ? left : right;

        private sealed class Row(
            long id,
            string channel,
            PublicChatOutboxItem item,
            DateTimeOffset enqueuedAt
        )
        {
            public long Id { get; } = id;

            public string Channel { get; } = channel;

            public PublicChatOutboxItem Item { get; } = item;

            public string? Message { get; set; } = item.Message;

            public DateTimeOffset EnqueuedAt { get; } = enqueuedAt;

            public DateTimeOffset NextAttemptAt { get; } = enqueuedAt;

            public RowStatus Status { get; set; }

            public int AttemptCount { get; set; }

            public PublicChatClaimToken? ClaimToken { get; set; }

            public DateTimeOffset ClaimExpiresAt { get; set; }

            public DateTimeOffset? CompletedAt { get; set; }

            public PublicChatClaimedMessage Claimed(PublicChatClaimToken token) =>
                new()
                {
                    Id = Id,
                    Channel = Channel,
                    Message = Message!,
                    EnqueuedAt = EnqueuedAt,
                    Attempt = AttemptCount + 1,
                    ClaimToken = token,
                    ClaimExpiresAt = ClaimExpiresAt,
                    DeduplicationKey = Item.DeduplicationKey,
                };
        }

        private sealed record Candidate(Row Row, DateTimeOffset EligibleAt);

        private enum RowStatus
        {
            Pending,
            Claimed,
            Sending,
            Delivered,
            Faulted,
        }
    }

    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add(new LogEntry(formatter(state, exception), exception));
    }

    private sealed record LogEntry(string Message, Exception? Exception);

    private sealed class ManualTimeProvider(DateTimeOffset initialNow) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ManualTimer> timers = [];
        private readonly Channel<bool> timerRegistrations = Channel.CreateUnbounded<bool>();
        private DateTimeOffset now = initialNow;
        private bool waitingForTimerRegistration;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
                return now;
        }

        public override long GetTimestamp() => GetUtcNow().UtcTicks;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            List<ManualTimer> due;
            lock (gate)
            {
                now = now.Add(delta);
                due = timers.Where(timer => timer.IsDue(now)).ToList();
            }

            foreach (var timer in due)
                timer.Fire();
        }

        public ValueTask<bool> WaitForTimerRegistrationAsync()
        {
            lock (gate)
            {
                if (timers.Count > 0)
                    return ValueTask.FromResult(true);

                if (waitingForTimerRegistration)
                    throw new InvalidOperationException("Only one timer observer is supported.");

                waitingForTimerRegistration = true;
                return timerRegistrations.Reader.ReadAsync();
            }
        }

        private void AddTimer(ManualTimer timer)
        {
            lock (gate)
            {
                if (!timers.Contains(timer))
                    timers.Add(timer);
                if (!waitingForTimerRegistration)
                    return;

                waitingForTimerRegistration = false;
                if (!timerRegistrations.Writer.TryWrite(true))
                    throw new InvalidOperationException("The timer observer could not be notified.");
            }
        }

        private void RemoveTimer(ManualTimer timer)
        {
            lock (gate)
                timers.Remove(timer);
        }

        private DateTimeOffset CurrentNowLocked => now;

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state
        ) : ITimer
        {
            private TimeSpan period;
            private DateTimeOffset dueAt = DateTimeOffset.MaxValue;
            private bool disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner.gate)
                {
                    if (disposed)
                        return false;

                    this.period = period;
                    dueAt = dueTime == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : owner.CurrentNowLocked.Add(dueTime);
                    owner.AddTimer(this);
                }

                if (dueTime != Timeout.InfiniteTimeSpan && dueTime <= TimeSpan.Zero)
                    Fire();

                return true;
            }

            public void Dispose()
            {
                lock (owner.gate)
                {
                    if (disposed)
                        return;

                    disposed = true;
                    owner.RemoveTimer(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public bool IsDue(DateTimeOffset value)
            {
                lock (owner.gate)
                    return !disposed && dueAt <= value;
            }

            public void Fire()
            {
                lock (owner.gate)
                {
                    if (disposed || dueAt > owner.CurrentNowLocked)
                        return;

                    if (period > TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
                        dueAt = owner.CurrentNowLocked.Add(period);
                    else
                    {
                        disposed = true;
                        owner.RemoveTimer(this);
                    }
                }

                callback(state);
            }
        }
    }
}
