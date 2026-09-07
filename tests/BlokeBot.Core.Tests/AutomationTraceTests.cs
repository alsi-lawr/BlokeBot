using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task Trace_ScenarioAndProductionShareRedactedFactsAndSurviveFlowDeletion()
    {
        var writes = new ScenarioWriteGuard();
        await using var fixture = await RuntimeFixture.CreateAsync(
            databaseInterceptors: [writes],
            migrateSchema: true
        );
        const string Secret = "never-persist-this-trace-value";
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var condition = Node("condition", """{"predicate":true}""");
        var action = Node("send-chat", JsonSerializer.Serialize(new { message = Secret })) with
        {
            DisplayAlias = Secret,
        };
        var draft = Draft(
            fixture.HostId,
            [source, delay, condition, action],
            [
                Edge(source, "flow", delay),
                Edge(delay, "complete", condition),
                Edge(condition, "yes", action),
            ]
        );
        var flowId = (await fixture.Flows.SaveAsync(draft, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        draft = draft with { Id = flowId };
        var scenario = fixture.Scenarios.CreateDefaultFixture(draft, source.Id) with
        {
            ClockUtc = fixture.Clock.GetUtcNow(),
        };
        writes.Armed = true;
        var evaluated = (
            await fixture.Scenarios.RunAsync(draft, scenario, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        var testTrace = await ReadTraceAsync(fixture, evaluated.TraceId);
        testTrace.ProductionRunId.ShouldBeNull();
        testTrace.Events[^1].Event.Outcome.ShouldBe(AutomationTraceOutcome.Succeeded);
        fixture.Chat.Calls.ShouldBe(0);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
            (await db.AutomationNodeRuns.CountAsync()).ShouldBe(0);
        }
        writes.Writes.ShouldBe(0);
        writes.TraceWrites.ShouldBeGreaterThan(0);
        writes.Armed = false;

        var dispatch = await fixture.Runtime.DispatchAsync(
            new(
                Context(fixture.HostId, sensitive: Secret),
                new CustomCommandSourceConfiguration(new(7))
            ),
            CancellationToken.None
        );
        var runId = dispatch.RunIds.ShouldHaveSingleItem();
        var waiting = await ReadTraceAsync(fixture, new(runId.Value));
        waiting.Events.ShouldContain(entry =>
            entry.Event.Kind == AutomationTraceEventKind.Delay
            && entry.Event.DueAtUtc == fixture.Clock.GetUtcNow().AddSeconds(1)
        );
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        (await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        fixture.Chat.Messages.ShouldBe([Secret]);
        var production = await ReadTraceAsync(fixture, new(runId.Value));
        production.ProductionRunId.ShouldBe(runId);
        static string ObservableEvents(AutomationTraceSnapshot trace) =>
            JsonSerializer.Serialize(
                trace
                    .Events.Where(entry => entry.Event.Kind != AutomationTraceEventKind.Resume)
                    .Select(entry => entry.Event with { Invocation = new(Guid.Empty) })
            );
        ObservableEvents(production).ShouldBe(ObservableEvents(testTrace));
        _ = production
            .Events.Where(entry =>
                entry.Event.Node != null
                && entry.Event.Node.Id == action.Id
                && entry.Event.Kind == AutomationTraceEventKind.Attempt
            )
            .ShouldHaveSingleItem();
        var summary = (await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        summary.TraceId.ShouldBe(production.Id);
        JsonSerializer.Serialize(production).ShouldNotContain(Secret);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var rows = await db
                .AutomationTraceEvents.Select(value => value.EventJson)
                .ToArrayAsync();
            string.Join("", rows).ShouldNotContain(Secret);
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
        }
        _ = (
            await fixture.Flows.DeleteAsync(new(fixture.HostId), flowId, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowDeleteOutcome.Deleted>();
        JsonSerializer
            .Serialize(await ReadTraceAsync(fixture, production.Id))
            .ShouldBe(JsonSerializer.Serialize(production));
        JsonSerializer
            .Serialize(await ReadTraceAsync(fixture, testTrace.Id))
            .ShouldBe(JsonSerializer.Serialize(testTrace));
        _ = (
            await new AutomationTraceStore(fixture.Database, fixture.Clock).ReadAsync(
                new(fixture.HostId + 1),
                production.Id,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationTraceReadOutcome.NotFound>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Trace_BoundsKeepAnExplicitImmutablePrefixAndExpiryReleasesOnlyTraceStorage(
        bool byteLimit
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var id = Guid.NewGuid();
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            await AutomationTraceStore.CreateAsync(
                db,
                id,
                fixture.HostId,
                null,
                null,
                now,
                CancellationToken.None
            );
            var values = byteLimit
                ? Enumerable
                    .Range(0, 200)
                    .ToImmutableDictionary(
                        i => new AutomationPortId($"port-{i}"),
                        _ => new AutomationResolvedValue(
                            new AutomationValue.Text("secret-derived-value"),
                            [
                                AutomationValueProvenance.PublicChat,
                                AutomationValueProvenance.Generated,
                            ]
                        )
                    )
                : ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty;
            var data = AutomationTraceRedaction.Event(
                id,
                AutomationTraceEventKind.ResolvedInputs,
                now,
                values: values
            );
            for (var i = 0; i <= AutomationTraceStore.MaximumEvents; i++)
            {
                await AutomationTraceStore.AppendAsync(
                    db,
                    id,
                    data with
                    {
                        TimeUtc = new(now.AddTicks(i), TimeSpan.Zero),
                    },
                    now,
                    CancellationToken.None
                );
            }
        }
        var traces = new AutomationTraceStore(fixture.Database, fixture.Clock);
        var bounded = await ReadTraceAsync(fixture, new(id));
        bounded.Truncation.ShouldBe(
            byteLimit ? AutomationTraceTruncation.ByteLimit : AutomationTraceTruncation.EventLimit
        );
        bounded
            .Events.Select(entry => entry.Sequence)
            .ShouldBe(Enumerable.Range(1, bounded.Events.Length));
        bounded.Events.Length.ShouldBeLessThanOrEqualTo(AutomationTraceStore.MaximumEvents);
        bounded.ByteCount.ShouldBeLessThanOrEqualTo(AutomationTraceStore.MaximumBytes);
        JsonSerializer.Serialize(bounded).ShouldNotContain("secret-derived-value");
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            bounded.ByteCount.ShouldBe(
                (
                    await db
                        .AutomationTraceEvents.Where(value => value.TraceId == id)
                        .Select(value => value.EventJson)
                        .ToArrayAsync()
                ).Sum(Encoding.UTF8.GetByteCount)
            );
            await AutomationTraceStore.AppendAsync(
                db,
                id,
                AutomationTraceRedaction.Event(
                    id,
                    AutomationTraceEventKind.Terminal,
                    now,
                    outcome: AutomationTraceOutcome.Succeeded
                ),
                now,
                CancellationToken.None
            );
        }
        JsonSerializer
            .Serialize(await ReadTraceAsync(fixture, new(id)))
            .ShouldBe(JsonSerializer.Serialize(bounded));
        fixture.Clock.Advance(AutomationTraceStore.Retention);
        _ = (
            await traces.ReadAsync(new(fixture.HostId), new(id), CancellationToken.None)
        ).ShouldBeOfType<AutomationTraceReadOutcome.Expired>();
        (await traces.DeleteExpiredAsync(CancellationToken.None)).ShouldBe(1);
        (await traces.DeleteExpiredAsync(CancellationToken.None)).ShouldBe(0);
        _ = (
            await traces.ReadAsync(new(fixture.HostId), new(id), CancellationToken.None)
        ).ShouldBeOfType<AutomationTraceReadOutcome.NotFound>();
        await using var cleaned = await fixture.Database.CreateDbContextAsync();
        (await cleaned.AutomationTraceEvents.CountAsync()).ShouldBe(0);
        (await cleaned.Hosts.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task Trace_AppendRollsBackWithItsOwningTransactionAndCleanupIsBounded()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var id = Guid.NewGuid();
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            await AutomationTraceStore.CreateAsync(
                db,
                id,
                fixture.HostId,
                null,
                null,
                now,
                CancellationToken.None
            );
            await using var transaction = await db.Database.BeginTransactionAsync();
            await AutomationTraceStore.AppendAsync(
                db,
                id,
                AutomationTraceRedaction.Event(id, AutomationTraceEventKind.Admission, now),
                now,
                CancellationToken.None
            );
            await transaction.RollbackAsync();
        }
        var empty = await ReadTraceAsync(fixture, new(id));
        empty.Events.ShouldBeEmpty();
        empty.ByteCount.ShouldBe(0);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            await AutomationTraceStore.AppendAsync(
                db,
                id,
                AutomationTraceRedaction.Event(id, AutomationTraceEventKind.Admission, now),
                now,
                CancellationToken.None
            );
            for (var i = 0; i < AutomationTraceStore.CleanupBatchSize; i++)
            {
                await AutomationTraceStore.CreateAsync(
                    db,
                    Guid.NewGuid(),
                    fixture.HostId,
                    null,
                    null,
                    now,
                    CancellationToken.None
                );
            }
        }
        (await ReadTraceAsync(fixture, new(id))).Events.ShouldHaveSingleItem().Sequence.ShouldBe(1);
        var traces = new AutomationTraceStore(fixture.Database, fixture.Clock);
        (await traces.ListAsync(new(fixture.HostId), null, CancellationToken.None)).Length.ShouldBe(
            AutomationTraceStore.QueryLimit
        );
        (
            await traces.ListAsync(new(fixture.HostId + 1), null, CancellationToken.None)
        ).ShouldBeEmpty();
        fixture.Clock.Advance(AutomationTraceStore.Retention);
        (await traces.ListAsync(new(fixture.HostId), null, CancellationToken.None)).ShouldBeEmpty();
        (await traces.DeleteExpiredAsync(CancellationToken.None)).ShouldBe(
            AutomationTraceStore.CleanupBatchSize
        );
        (await traces.DeleteExpiredAsync(CancellationToken.None)).ShouldBe(1);
    }

    [Test]
    public async Task Trace_ConcurrentAppendsKeepEveryEventOnceInOneContiguousPrefix()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var id = Guid.NewGuid();
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            await AutomationTraceStore.CreateAsync(
                db,
                id,
                fixture.HostId,
                null,
                null,
                now,
                CancellationToken.None
            );
        }
        await Task.WhenAll(
            Enumerable
                .Range(0, 12)
                .Select(index =>
                    Task.Run(async () =>
                    {
                        await using var db = await fixture.Database.CreateDbContextAsync();
                        await AutomationTraceStore.AppendAsync(
                            db,
                            id,
                            AutomationTraceRedaction.Event(
                                id,
                                AutomationTraceEventKind.Resume,
                                now.AddTicks(index)
                            ),
                            now,
                            CancellationToken.None
                        );
                    })
                )
        );
        var trace = await ReadTraceAsync(fixture, new(id));
        trace.Events.Select(entry => entry.Sequence).ShouldBe(Enumerable.Range(1, 12));
        trace
            .Events.Select(entry => entry.Event.TimeUtc.UtcTicks)
            .Order()
            .ShouldBe(Enumerable.Range(0, 12).Select(index => now.Ticks + index));
        trace.Truncation.ShouldBe(AutomationTraceTruncation.None);
    }

    private static async Task AssertFailedAttemptTraceAsync(
        RuntimeFixture fixture,
        Guid id,
        AutomationNodeId nodeId,
        AutomationTraceOutcome outcome,
        AutomationTraceOutcome terminal,
        bool interrupted = false
    )
    {
        var trace = await ReadTraceAsync(fixture, new(id));
        _ = trace
            .Events.Where(entry =>
                entry.Event.Kind == AutomationTraceEventKind.Attempt
                && entry.Event.Node != null
                && entry.Event.Node.Id == nodeId
            )
            .ShouldHaveSingleItem();
        var result = trace
            .Events.Last(entry =>
                entry.Event.Kind == AutomationTraceEventKind.Result
                && entry.Event.Node != null
                && entry.Event.Node.Id == nodeId
            )
            .Event;
        result.Outcome.ShouldBe(outcome);
        result.Retry.ShouldBe(AutomationTraceRetry.NotRetried);
        trace.Events[^1].Event.Kind.ShouldBe(AutomationTraceEventKind.Terminal);
        trace.Events[^1].Event.Outcome.ShouldBe(terminal);
        if (interrupted)
        {
            trace.Events.ShouldContain(entry =>
                entry.Event.Kind == AutomationTraceEventKind.Cancellation
                && entry.Event.Outcome == AutomationTraceOutcome.Cancelled
            );
            trace.Events.ShouldContain(entry =>
                entry.Event.Kind == AutomationTraceEventKind.Recovery
                && entry.Event.Retry == AutomationTraceRetry.NotRetried
            );
            trace.Events.ShouldNotContain(entry =>
                entry.Event.Kind == AutomationTraceEventKind.Result
                && entry.Event.Node != null
                && entry.Event.Node.DefinitionId == AutomationDefinitionIds.CustomCommandSource
            );
        }
    }

    private static async Task<AutomationTraceSnapshot> ReadTraceAsync(
        RuntimeFixture fixture,
        AutomationTraceId id
    ) =>
        (
            await new AutomationTraceStore(fixture.Database, fixture.Clock).ReadAsync(
                new(fixture.HostId),
                id,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<AutomationTraceReadOutcome.Available>()
            .Trace;
}
