using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments("1.001", 1, 1001)]
    [Arguments("1e-3", 10, 1)]
    public async Task CelTimer_SavedFlowPreservesNameExactDueFrozenRestartAndVirtualScenario(
        string seconds,
        int words,
        int milliseconds
    )
    {
        var writes = new ScenarioWriteGuard();
        await using var fixture = await RuntimeFixture.CreateAsync(
            databaseInterceptors: [writes],
            migrateSchema: true
        );
        var timer = CelTimer(fixture.HostId);
        var flow = (
            await fixture.Flows.SaveAsync(timer.Draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var saved = (await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.Single(item => item.Draft.Id == flow.FlowId)
            .Draft;
        var name = Enumerable.Range(1, words).Select(index => $"word-{index}").ToArray();
        string[] arguments = [seconds, .. name];
        var expectedName = string.Join(" ", name);
        var started = fixture.Clock.GetUtcNow();
        var context = CelTimerContext(fixture.HostId, arguments);
        var run = (
            await fixture.Runtime.DispatchAsync(
                new(context, new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            )
        ).RunIds.ShouldHaveSingleItem();
        fixture.Chat.Messages.ShouldBe([expectedName]);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var state = await db.AutomationFlowRuns.SingleAsync();
            state.Status.ShouldBe(AutomationFlowRunStatus.Waiting);
            state.CompletedAtUtc.ShouldBeNull();
            var pending = await db.AutomationNodeRuns.SingleAsync(node =>
                node.NodeId == timer.Finish.Id.Value
            );
            pending.AvailableAtUtc.ShouldBe(
                started.UtcDateTime.AddTicks(milliseconds * TimeSpan.TicksPerMillisecond)
            );
            // Resumable checkpoints deliberately contain actual values; trace diagnostics do not.
            var output = (
                await db.AutomationNodeRuns.SingleAsync(node => node.NodeId == timer.Parse.Id.Value)
            ).OutputJson;
            output.ShouldNotBeNull().ShouldContain(expectedName);
        }
        var live = await ReadTraceAsync(fixture, new(run.Value));
        var outputs = live.Events.Single(entry =>
            entry.Event.Node?.Id == timer.Parse.Id
            && entry.Event.Kind == AutomationTraceEventKind.Outputs
        );
        outputs.Event.Outcome.ShouldBe(AutomationTraceOutcome.Succeeded);
        outputs.Event.Values.ShouldAllBe(value =>
            value.Provenance.Contains(AutomationValueProvenance.PublicChat)
            && value.SafeTriggerFields.Contains(new("arguments"))
        );
        JsonSerializer.Serialize(live).ShouldNotContain(expectedName);

        var editedParse = CelTimerParse("decimal(args[0]) + 100", "'changed-name'") with
        {
            Id = timer.Parse.Id,
        };
        _ = (
            await fixture.Flows.SaveAsync(
                saved with
                {
                    Nodes = saved.Nodes.Replace(
                        saved.Nodes.Single(node => node.Id == timer.Parse.Id),
                        editedParse
                    ),
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        fixture.Clock.Advance(
            TimeSpan.FromTicks((milliseconds * TimeSpan.TicksPerMillisecond) - 1)
        );
        (await fixture.NewRuntime().ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Waiting
        );
        fixture.Chat.Messages.ShouldBe([expectedName]);
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        (await fixture.NewRuntime().ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        (await fixture.NewRuntime().ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        fixture.Chat.Messages.ShouldBe([expectedName, "timer-finished"]);

        var scenario = fixture.Scenarios.CreateDefaultFixture(saved, timer.Source.Id) with
        {
            Context = context,
            ClockUtc = started,
        };
        writes.Armed = true;
        var result = (
            await fixture.Scenarios.RunAsync(saved, scenario, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        result
            .Nodes.Single(node => node.NodeId == timer.Finish.Id)
            .VirtualTimeUtc.ShouldBe(started.AddTicks(milliseconds * TimeSpan.TicksPerMillisecond));
        var trace = await ReadTraceAsync(fixture, result.TraceId);
        trace
            .Events.Single(entry => entry.Event.Kind == AutomationTraceEventKind.Delay)
            .Event.DueAtUtc.ShouldBe(started.AddTicks(milliseconds * TimeSpan.TicksPerMillisecond));
        trace.Events[^1].Event.Outcome.ShouldBe(AutomationTraceOutcome.Succeeded);
        JsonSerializer.Serialize(trace).ShouldNotContain(expectedName);
        writes.Writes.ShouldBe(0);
        fixture.Chat.Messages.ShouldBe([expectedName, "timer-finished"]);
        await using var verify = await fixture.Database.CreateDbContextAsync();
        (await verify.AutomationFlowRuns.CountAsync()).ShouldBe(1);
    }

    [Test]
    [Arguments("", "name", false, false)]
    [Arguments("private-invalid-number", "name", false, true)]
    [Arguments("1,5", "name", false, true)]
    [Arguments("1e100", "name", false, true)]
    [Arguments("1e-100", "name", false, false)]
    [Arguments("0", "name", false, false)]
    [Arguments("-1", "name", false, false)]
    [Arguments("0.0001", "name", false, false)]
    [Arguments("922337203685.478", "name", false, false)]
    [Arguments("79228162514264337593543950335", "name", false, false)]
    [Arguments("1", "", false, false)]
    [Arguments("1", "name", true, false)]
    public async Task CelTimer_ActualShapeAndNumericBranchesNeverStartInvalidTimers(
        string seconds,
        string name,
        bool tooManyWords,
        bool parseFails
    )
    {
        var writes = new ScenarioWriteGuard();
        await using var fixture = await RuntimeFixture.CreateAsync(
            databaseInterceptors: [writes],
            migrateSchema: true
        );
        var timer = CelTimer(fixture.HostId);
        var flow = (
            await fixture.Flows.SaveAsync(timer.Draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var draft = timer.Draft with { Id = flow.FlowId };
        string[] arguments =
            seconds.Length == 0 ? []
            : tooManyWords ? [seconds, .. Enumerable.Repeat(name, 11)]
            : [seconds, name];
        var context = CelTimerContext(fixture.HostId, arguments);
        var run = (
            await fixture.Runtime.DispatchAsync(
                new(context, new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            )
        ).RunIds.ShouldHaveSingleItem();
        fixture.Chat.Messages.ShouldBe(parseFails ? [] : ["timer-usage"]);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var state = await db.AutomationFlowRuns.SingleAsync();
            state.Status.ShouldBe(
                parseFails ? AutomationFlowRunStatus.Failed : AutomationFlowRunStatus.Completed
            );
            var nodes = await db.AutomationNodeRuns.ToArrayAsync();
            nodes.ShouldNotContain(node =>
                node.NodeId == timer.Start.Id.Value
                || node.NodeId == timer.Delay.Id.Value
                || node.NodeId == timer.Finish.Id.Value
            );
            if (parseFails)
            {
                nodes.Single(node => node.NodeId == timer.Parse.Id.Value).OutputJson.ShouldBeNull();
            }
            else if (arguments.Length is < 2 or > 11)
            {
                nodes.ShouldNotContain(node => node.NodeId == timer.Parse.Id.Value);
            }
        }
        var trace = await ReadTraceAsync(fixture, new(run.Value));
        trace.Events.ShouldNotContain(entry => entry.Event.Kind == AutomationTraceEventKind.Delay);
        if (parseFails)
        {
            trace
                .Events.Single(entry =>
                    entry.Event.Kind == AutomationTraceEventKind.Outputs
                    && entry.Event.Node?.Id == timer.Parse.Id
                )
                .Event.Outcome.ShouldBe(AutomationTraceOutcome.Failed);
            JsonSerializer.Serialize(trace).ShouldNotContain(seconds);
        }
        writes.Armed = true;
        var scenario = fixture.Scenarios.CreateDefaultFixture(draft, timer.Source.Id) with
        {
            Context = context,
        };
        var outcome = await fixture.Scenarios.RunAsync(draft, scenario, CancellationToken.None);
        AutomationTraceId testTraceId;
        if (parseFails)
        {
            var failure = outcome.ShouldBeOfType<AutomationScenarioRunOutcome.Failed>();
            failure.Nodes.ShouldNotContain(node =>
                node.NodeId == timer.Start.Id || node.NodeId == timer.Delay.Id
            );
            testTraceId = failure.TraceId;
        }
        else
        {
            var completed = outcome.ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
            completed.Nodes.ShouldNotContain(node =>
                node.NodeId == timer.Start.Id || node.NodeId == timer.Delay.Id
            );
            testTraceId = completed.TraceId;
        }
        var testTrace = await ReadTraceAsync(fixture, testTraceId);
        testTrace
            .Events[^1]
            .Event.Outcome.ShouldBe(
                parseFails ? AutomationTraceOutcome.Failed : AutomationTraceOutcome.Succeeded
            );
        writes.Writes.ShouldBe(0);
        fixture.Chat.Messages.ShouldBe(parseFails ? [] : ["timer-usage"]);
    }

    [Test]
    public async Task CelTimer_DateTimeHeadroomIsNotAPreStartGuarantee()
    {
        RuntimeFixture? current = null;
        var chat = new RecordingChatSender(
            null,
            (call, _, _) =>
            {
                if (call == 1)
                {
                    current!.Clock.Advance(
                        DateTimeOffset.MaxValue.AddMilliseconds(-500) - current.Clock.GetUtcNow()
                    );
                }
                return ValueTask.CompletedTask;
            }
        );
        await using var fixture = await RuntimeFixture.CreateAsync(chat: chat, migrateSchema: true);
        current = fixture;
        var timer = CelTimer(fixture.HostId);
        _ = (
            await fixture.Flows.SaveAsync(timer.Draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        _ = (
            await fixture.Runtime.DispatchAsync(
                new(
                    CelTimerContext(fixture.HostId, ["1", "already-started"]),
                    new CustomCommandSourceConfiguration(new(7))
                ),
                CancellationToken.None
            )
        ).RunIds.ShouldHaveSingleItem();
        fixture.Chat.Messages.ShouldBe(["already-started"]);
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.SingleAsync()).Status.ShouldBe(AutomationFlowRunStatus.Failed);
        (
            await db.AutomationNodeRuns.SingleAsync(node => node.NodeId == timer.Delay.Id.Value)
        ).Status.ShouldBe(AutomationNodeRunStatus.Failed);
        (
            await db.AutomationNodeRuns.AnyAsync(node => node.NodeId == timer.Finish.Id.Value)
        ).ShouldBeFalse();
    }

    private static AutomationContext CelTimerContext(int hostId, string[] arguments) =>
        Context(hostId) with
        {
            Arguments = [.. arguments.Select((text, index) => new AutomationArgument(index, text))],
            Variables = new([]),
        };

    private static CelTimerGraph CelTimer(int hostId)
    {
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var shape = Node(
            "condition",
            """{"predicate":true}""",
            bindings: Bindings(
                "predicate",
                AutomationInputBindingMode.Expression,
                LanguageExpression("size(arguments) >= 2 && size(arguments) <= 11")
            )
        );
        var parse = CelTimerParse("decimal(args[0])", "join(args, 1, size(args), ' ')");
        var check = Node(
            "cel-transform",
            TransformJson(
                [
                    new(
                        "seconds",
                        "seconds",
                        "Seconds",
                        "seconds-input",
                        AutomationPortValueType.Number,
                        AutomationPortNullability.NonNullable,
                        0m
                    ),
                    new(
                        "name",
                        "name",
                        "Name",
                        "name-input",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        ""
                    ),
                ],
                [
                    new(
                        "valid",
                        "Valid",
                        AutomationPortValueType.Boolean,
                        AutomationPortNullability.NonNullable,
                        "seconds > 0 && seconds <= decimal(922337203685477) / 1000 && seconds * 1000 == decimal(int(seconds * 1000)) && size(name) > 0"
                    ),
                ]
            ),
            bindings: Bindings("seconds-input", AutomationInputBindingMode.Connected)
                .Add(new("name-input"), new(AutomationInputBindingMode.Connected, null))
        );
        var numeric = Node(
            "condition",
            """{"predicate":true}""",
            bindings: Bindings("predicate", AutomationInputBindingMode.Connected)
        );
        var milliseconds = Node(
            "cel-transform",
            TransformJson(
                [
                    new(
                        "seconds",
                        "seconds",
                        "Seconds",
                        "seconds-input",
                        AutomationPortValueType.Number,
                        AutomationPortNullability.NonNullable,
                        0m
                    ),
                ],
                [
                    new(
                        "milliseconds",
                        "Milliseconds",
                        AutomationPortValueType.Number,
                        AutomationPortNullability.NonNullable,
                        "seconds * 1000"
                    ),
                ]
            ),
            bindings: Bindings("seconds-input", AutomationInputBindingMode.Connected)
        );
        var start = Node(
            "send-chat",
            """{"message":"unused"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var delay = BoundDelay(AutomationInputBindingMode.Connected);
        var finish = Node("send-chat", """{"message":"timer-finished"}""");
        var usage = Node("send-chat", """{"message":"timer-usage"}""");
        return new(
            Draft(
                hostId,
                [source, shape, parse, check, numeric, milliseconds, start, delay, finish, usage],
                [
                    Edge(source, "flow", shape),
                    Edge(shape, "yes", numeric),
                    Edge(shape, "no", usage),
                    Edge(numeric, "yes", start),
                    Edge(numeric, "no", usage),
                    Edge(start, "complete", delay),
                    Edge(delay, "complete", finish),
                    Edge(parse, "seconds", check, "seconds", AutomationEdgeKind.Data),
                    Edge(parse, "name", check, "name", AutomationEdgeKind.Data),
                    Edge(check, "valid", numeric, "predicate", AutomationEdgeKind.Data),
                    Edge(parse, "name", start, "message", AutomationEdgeKind.Data),
                    Edge(parse, "seconds", milliseconds, "seconds", AutomationEdgeKind.Data),
                    Edge(milliseconds, "milliseconds", delay, "duration", AutomationEdgeKind.Data),
                ]
            ),
            source,
            parse,
            start,
            delay,
            finish
        );
    }

    private static AutomationFlowDraftNode CelTimerParse(string seconds, string name) =>
        Node(
            "cel-transform",
            TransformJson(
                [
                    new(
                        "args",
                        "args",
                        "Arguments",
                        "args-input",
                        AutomationPortValueType.Arguments,
                        AutomationPortNullability.NonNullable,
                        Array.Empty<string>()
                    ),
                ],
                [
                    new(
                        "seconds",
                        "Seconds",
                        AutomationPortValueType.Number,
                        AutomationPortNullability.NonNullable,
                        seconds
                    ),
                    new(
                        "name",
                        "Name",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        name
                    ),
                ]
            ),
            bindings: Bindings(
                "args-input",
                AutomationInputBindingMode.Expression,
                LanguageExpression("arguments")
            )
        );

    private sealed record CelTimerGraph(
        AutomationFlowDraft Draft,
        AutomationFlowDraftNode Source,
        AutomationFlowDraftNode Parse,
        AutomationFlowDraftNode Start,
        AutomationFlowDraftNode Delay,
        AutomationFlowDraftNode Finish
    );
}
