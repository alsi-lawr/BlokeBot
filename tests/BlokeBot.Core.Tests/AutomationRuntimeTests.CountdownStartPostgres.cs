using System.Data.Common;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test, Explicit]
    public async Task CountdownStart_PostgreSqlHeldHostLockSerializesDuplicateBeforeTimerRead()
    {
        var barrier = new CountdownHostLockBarrier();
        await using var database = await PointProviderFixture.PostgreSqlAsync(barrier);
        var clock = new MutableTimeProvider(new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        int hostId;
        await using (var seed = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                TwitchUserId = "100",
                EnabledFeatures = HostFeatureFlags.Automations,
                CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
        }
        var features = TestHostFeatureServices.Create(
            database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            []
        );
        var expressions = new AutomationExpressionService();
        var catalog = new AutomationCatalogService(
            new([new CoreAutomationCatalogModule(), new ExpandedAutomationCatalogModule()]),
            features,
            expressions
        );
        var chat = new RecordingChatSender(null);
        var overlays = new NoOverlayCues();
        var changes = TestAutomationUiChanges.Create();
        AutomationRuntimeService runtime = null!;
        var countdowns = new AutomationCountdownService(database, clock, () => runtime, changes);
        var actions = new AutomationActionExecutor(
            features,
            chat,
            overlays,
            expressions,
            countdowns
        );
        var flows = new AutomationFlowService(
            database,
            catalog,
            expressions,
            overlays,
            clock,
            uiChanges: changes
        );
        runtime = new AutomationRuntimeService(
            database,
            catalog,
            flows,
            actions,
            clock,
            changes,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AutomationRuntimeService>.Instance
        );
        var source = Node(
            "countdown-lifecycle",
            """{"name":"race","event":"Started","remaining-seconds":0}"""
        );
        var send = Node("send-chat", """{"message":"lifecycle"}""");
        _ = (
            await flows.SaveAsync(
                Draft(hostId, [source, send], [Edge(source, "flow", send)]),
                default
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var updates = 0;
        using var subscription = changes.Subscribe(
            new AutomationUiChange.Countdown(new(hostId), "race"),
            ObserverIdentity.Named("postgres-start-once"),
            (_, _) =>
            {
                updates++;
                return ValueTask.CompletedTask;
            }
        );
        await countdowns.InitializeAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        barrier.Arm();
        var first = countdowns.ApplyAsync(
            new(hostId),
            new("race", TimeSpan.FromSeconds(20), CountdownOperation.Start),
            timeout.Token
        );
        var leader = await barrier.LeaderEntered.WaitAsync(timeout.Token);
        var second = countdowns.ApplyAsync(
            new(hostId),
            new("race", TimeSpan.FromSeconds(999), CountdownOperation.Start),
            timeout.Token
        );
        try
        {
            var contender = await barrier.ContenderAttempted.WaitAsync(timeout.Token);
            leader.Pid.ShouldNotBe(contender.Pid);
            contender.Database.ShouldBe(leader.Database);
            await using var context = await database.CreateDbContextAsync(timeout.Token);
            await using var probe = new NpgsqlConnection(context.Database.GetConnectionString());
            await probe.OpenAsync(timeout.Token);
            await using var query = new NpgsqlCommand(
                "SELECT wait_event, @leader = ANY(pg_blocking_pids(pid)) FROM pg_stat_activity WHERE pid = @contender AND state = 'active' AND wait_event_type = 'Lock'",
                probe
            );
            _ = query.Parameters.AddWithValue("leader", leader.Pid);
            _ = query.Parameters.AddWithValue("contender", contender.Pid);
            string? observedWait = null;
            using var waitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                timeout.Token,
                waitTimeout.Token
            );
            while (observedWait is null)
            {
                await using var reader = await query.ExecuteReaderAsync(waitCancellation.Token);
                if (await reader.ReadAsync(waitCancellation.Token) && reader.GetBoolean(1))
                {
                    observedWait = reader.GetString(0);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(20), waitCancellation.Token);
                }
            }
            if (
                Environment.GetEnvironmentVariable("BLOKEBOT_TEST_COUNTDOWN_LOCK_EVIDENCE") is
                { } evidence
            )
            {
                await File.WriteAllTextAsync(
                    evidence,
                    JsonSerializer.Serialize(
                        new
                        {
                            Database = leader.Database,
                            LeaderPid = leader.Pid,
                            ContenderPid = contender.Pid,
                            WaitEvent = observedWait,
                            ConfirmedLeaderBlocksContender = true,
                        }
                    ),
                    timeout.Token
                );
            }
        }
        finally
        {
            barrier.Release();
            _ = await Task.WhenAll(first, second);
        }
        _ = (await first).ShouldBeOfType<AutomationCountdownApplyOutcome.Started>();
        _ = (await second).ShouldBeOfType<AutomationCountdownApplyOutcome.AlreadyRunning>();
        await using var db = await database.CreateDbContextAsync();
        var timer = await db.AutomationCountdowns.SingleAsync();
        timer.IsRunning.ShouldBeTrue();
        timer.OccurrenceId.ShouldNotBe(Guid.Empty);
        timer.ProcessId.ShouldBe(countdowns.ProcessId);
        timer.DeadlineUtc.ShouldBe(clock.GetUtcNow().AddSeconds(20).UtcDateTime);
        updates.ShouldBe(1);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
        await runtime.ResumeDueAsync(default);
        chat.Messages.ShouldBe(["lifecycle"]);
        SameCountdownOccurrence(timer, await db.AutomationCountdowns.AsNoTracking().SingleAsync());
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
    }

    private sealed class CountdownHostLockBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<(int Pid, string Database)> _leader = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<(int Pid, string Database)> _contender = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _armed;
        private int _attempts;
        private DbCommand? _leaderCommand;

        internal Task<(int Pid, string Database)> LeaderEntered => _leader.Task;
        internal Task<(int Pid, string Database)> ContenderAttempted => _contender.Task;

        internal void Arm() => Volatile.Write(ref _armed, 1);

        internal void Release() => _ = _release.TrySetResult();

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Volatile.Read(ref _armed) == 1
                && command.CommandText.StartsWith("UPDATE hosts SET", StringComparison.Ordinal)
            )
            {
                var attempt = Interlocked.Increment(ref _attempts);
                if (attempt == 1)
                {
                    _leaderCommand = command;
                }
                else if (attempt == 2)
                {
                    var connection = (NpgsqlConnection)command.Connection!;
                    _ = _contender.TrySetResult((connection.ProcessID, connection.Database));
                }
            }
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (ReferenceEquals(command, _leaderCommand))
            {
                result.ShouldBe(1);
                var connection = (NpgsqlConnection)command.Connection!;
                _ = _leader.TrySetResult((connection.ProcessID, connection.Database));
                await _release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
