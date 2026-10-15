using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class NativeOperationAutomationTests
{
    [Test]
    public async Task NativePreparation_ResolvedChatIsNotInterpolatedAgainAndLiveDeadlineRemainsConfigured()
    {
        var writes = new AutomationRuntimeTests.ScenarioWriteGuard();
        await using var fixture = await NativeFixture.CreateAsync(interceptors: [writes]);
        var source = Node("poll-started", "{}");
        var transform = Node(
            "cel-transform",
            """{"inputs":[],"outputs":[{"port-id":"text","display-name":"Text","type":"Text","nullability":"NonNullable","cel":"\"$\" + \"{unavailable}\""}]}"""
        );
        var action = Node("send-chat", """{"message":"fallback"}""") with
        {
            InputBindings = ImmutableDictionary<
                AutomationConfigurationFieldId,
                AutomationInputBinding
            >.Empty.Add(new("message"), new(AutomationInputBindingMode.Connected, null)),
        };
        var draft = Draft(
            fixture.HostId,
            [source, transform, action],
            [
                Edge(source, "flow", action),
                new(
                    Guid.NewGuid(),
                    AutomationEdgeKind.Data,
                    transform.Id,
                    new("text"),
                    action.Id,
                    new("message")
                ),
            ]
        );
        var scenarios = Scenarios(fixture);
        var input = scenarios.CreateDefaultFixture(draft, source.Id);
        var saved = await fixture.Flows.SaveAsync(draft, CancellationToken.None);
        _ = saved.ShouldBeOfType<AutomationFlowSaveOutcome.Saved>(
            saved is AutomationFlowSaveOutcome.Invalid invalid
                ? string.Join("; ", invalid.Errors)
                : null
        );
        writes.Armed = true;
        var scenario = await scenarios.RunAsync(draft, input, CancellationToken.None);
        _ = scenario.ShouldBeOfType<AutomationScenarioRunOutcome.Completed>(
            scenario is AutomationScenarioRunOutcome.Invalid invalidScenario
                ? string.Join("; ", invalidScenario.Errors)
                : null
        );
        AssertNoNativeEffects(fixture);
        writes.Writes.ShouldBe(0);
        writes.Armed = false;
        _ = await fixture.FlowRuntime.DispatchAsync(
            new(
                input.Context with
                {
                    Event = input.Context.Event with { OccurrenceId = Guid.NewGuid() },
                },
                new PollStartedSourceConfiguration()
            ),
            CancellationToken.None
        );
        fixture.Chat.Messages.ShouldBe(["${unavailable}"]);
        _ = fixture
            .Chat.Deadlines.ShouldHaveSingleItem()
            .ShouldBeOfType<PublicChatDeliveryDeadline.ConfiguredMaximum>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativePreparation_ChatRejectsNonPublicOrNonTextInputBeforeEitherEffect(
        bool sensitive
    )
    {
        var writes = new AutomationRuntimeTests.ScenarioWriteGuard();
        await using var fixture = await NativeFixture.CreateAsync(interceptors: [writes]);
        var source = Node("poll-started", "{}");
        var transform = Node(
            "cel-transform",
            """{"inputs":[],"outputs":[{"port-id":"text","display-name":"Text","type":"Text","nullability":"NonNullable","cel":"\"public\""}]}"""
        );
        var chat = Node("send-chat", """{"message":"fallback"}""") with
        {
            InputBindings = ImmutableDictionary<
                AutomationConfigurationFieldId,
                AutomationInputBinding
            >.Empty.Add(new("message"), new(AutomationInputBindingMode.Connected, null)),
        };
        var draft = Draft(
            fixture.HostId,
            [source, transform, chat],
            [
                Edge(source, "flow", chat),
                new(
                    Guid.NewGuid(),
                    AutomationEdgeKind.Data,
                    transform.Id,
                    new("text"),
                    chat.Id,
                    new("message")
                ),
            ]
        );
        _ = await fixture.SaveAsync(draft.Nodes, draft.Edges);
        var scenarios = Scenarios(fixture);
        var input = scenarios.CreateDefaultFixture(draft, source.Id);
        writes.Armed = true;
        _ = (
            await scenarios.RunAsync(draft, input, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        AutomationValue value = sensitive
            ? new AutomationValue.Text("private-chat-never-in-trace")
            : new AutomationValue.Number(42);
        var rejected = input with
        {
            ConnectedInputs =
            [
                new(
                    chat.Id,
                    new("message"),
                    new(
                        value,
                        sensitive
                            ? AutomationDataSensitivity.Sensitive
                            : AutomationDataSensitivity.Safe
                    )
                ),
            ],
        };
        (await scenarios.RunAsync(draft, rejected, CancellationToken.None))
            .ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>()
            .Errors.ShouldContain(error =>
                error.NodeId == chat.Id && error.Code == "scenario-connected-input-invalid"
            );
        var live = await fixture.Executor.ExecuteAsync(
            new(fixture.HostId),
            new SendChatActionConfiguration("fallback"),
            ImmutableDictionary<AutomationConfigurationFieldId, AutomationExpressionSource>.Empty,
            ImmutableDictionary<AutomationConfigurationFieldId, AutomationResolvedValue>.Empty.Add(
                new("message"),
                new(value, sensitive ? [] : [AutomationValueProvenance.Generated])
            ),
            input.Context,
            CancellationToken.None
        );
        live.ShouldBeOfType<AutomationActionOutcome.Failed>()
            .Code.ShouldBe("sensitive-output-blocked");
        AssertNoNativeEffects(fixture);
        writes.Writes.ShouldBe(0);
        await using var db = await fixture.Database.CreateDbContextAsync();
        string.Join("", await db.AutomationTraceEvents.Select(row => row.EventJson).ToArrayAsync())
            .ShouldNotContain("private-chat-never-in-trace");
    }

    [Test]
    public async Task NativePreparation_HypotheticalPredictionTimelineDoesNotRequireOrMutateLiveManagerState()
    {
        var writes = new AutomationRuntimeTests.ScenarioWriteGuard();
        await using var fixture = await NativeFixture.CreateAsync(interceptors: [writes]);
        await RecordBroadcasterGrant(fixture);
        var source = Node("poll-started", "{}");
        var start = Node(
            "start-prediction",
            """{"title":" Question ","outcomes":" Yes \n No ","window-seconds":120}"""
        );
        var lockNode = Node("lock-prediction", "{}");
        var resolve = Node(
            "resolve-prediction",
            """{"winning-outcome-id":"hypothetical-outcome"}"""
        );
        var marker = Node("create-marker", """{"description":"${channel.display_name}"}""");
        var draft = Draft(
            fixture.HostId,
            [source, start, lockNode, resolve, marker],
            [
                Edge(source, "flow", start),
                Edge(start, "complete", lockNode),
                Edge(lockNode, "complete", resolve),
                Edge(resolve, "complete", marker),
            ]
        );
        var scenarios = Scenarios(fixture);
        var input = scenarios.CreateDefaultFixture(draft, source.Id);
        _ = await fixture.SaveAsync(draft.Nodes, draft.Edges);
        writes.Armed = true;
        var result = (
            await scenarios.RunAsync(draft, input, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        result.Nodes.Select(node => node.NodeId).ShouldBe(draft.Nodes.Select(node => node.Id));
        AssertNoNativeEffects(fixture);
        writes.Writes.ShouldBe(0);
        writes.Armed = false;
        _ = await fixture.FlowRuntime.DispatchAsync(
            new(
                input.Context with
                {
                    Event = input.Context.Event with { OccurrenceId = Guid.NewGuid() },
                },
                new PollStartedSourceConfiguration()
            ),
            CancellationToken.None
        );
        var prepared = fixture.Predictions.Started.ShouldHaveSingleItem().Draft;
        prepared.Title.ShouldBe("Question");
        prepared.Outcomes.ShouldBe(["Yes", "No"]);
        prepared.PredictionWindowSeconds.ShouldBe(120);
        fixture.Predictions.Locked.ShouldBe([(fixture.HostId, true)]);
        fixture.Predictions.Resolved.ShouldBe([(fixture.HostId, "hypothetical-outcome", true)]);
        fixture.ClipsMarkers.Markers.ShouldBe([
            (fixture.HostId, input.Context.Channel.DisplayName),
        ]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativePreparation_FrozenChildExpressionsAndDeclaredInputRespectStopContinue(
        bool innerContinues
    )
    {
        var writes = new AutomationRuntimeTests.ScenarioWriteGuard();
        await using var fixture = await NativeFixture.CreateAsync(interceptors: [writes]);
        await RecordBroadcasterGrant(fixture);
        var contract = new AutomationSubflowInterface(
            [new(new("text"), "Text", "Child input", AutomationPortValueType.Text)],
            []
        );
        var entry = Node(AutomationSubflowDefinitions.Entry, "{}") with
        {
            Definition = AutomationSubflowDefinitions.Boundary(
                AutomationSubflowDefinitions.Entry,
                contract
            ),
        };
        var exit = Node(AutomationSubflowDefinitions.Exit, "{}") with
        {
            Definition = AutomationSubflowDefinitions.Boundary(
                AutomationSubflowDefinitions.Exit,
                contract
            ),
        };
        var marker = WithExpression(
            Node("create-marker", """{"description":"valid fixed fallback must not be used"}"""),
            "description",
            "\"\""
        ) with
        {
            FailurePolicy = innerContinues
                ? AutomationNodeFailurePolicy.Continue
                : AutomationNodeFailurePolicy.Stop,
        };
        var chat = Node("send-chat", """{"message":"fixed child fallback must not be used"}""") with
        {
            InputBindings = ImmutableDictionary<
                AutomationConfigurationFieldId,
                AutomationInputBinding
            >.Empty.Add(new("message"), new(AutomationInputBindingMode.Connected, null)),
        };
        var child = new AutomationSubflowDraft(
            new(Guid.NewGuid()),
            "Child preparation",
            contract,
            Draft(
                fixture.HostId,
                [entry, marker, chat, exit],
                [
                    Edge(entry, "complete", marker),
                    Edge(marker, "complete", chat),
                    Edge(chat, "complete", exit),
                    new(
                        Guid.NewGuid(),
                        AutomationEdgeKind.Data,
                        entry.Id,
                        new("text"),
                        chat.Id,
                        new("message")
                    ),
                ]
            ) with
            {
                IsEnabled = false,
            }
        );
        var revision = (
            await new AutomationSubflowService(
                fixture.Database,
                fixture.Flows,
                fixture.Clock
            ).PublishAsync(child, CancellationToken.None)
        )
            .ShouldBeOfType<AutomationSubflowPublishOutcome.Published>()
            .Revision;
        var invoke = Node(AutomationSubflowDefinitions.Invoke, "{}") with
        {
            Definition = AutomationSubflowDefinitions.Invocation(
                revision,
                ImmutableDictionary<AutomationPortId, AutomationValue>.Empty.Add(
                    new("text"),
                    new AutomationValue.Text("literal-${unavailable}")
                )
            ),
            InputBindings = ImmutableDictionary<
                AutomationConfigurationFieldId,
                AutomationInputBinding
            >.Empty.Add(new("text"), new(AutomationInputBindingMode.Fixed, null)),
            FailurePolicy = AutomationNodeFailurePolicy.Continue,
        };
        var source = Node("poll-started", "{}");
        var parent = Node("send-chat", """{"message":"parent continuation"}""");
        var draft = Draft(
            fixture.HostId,
            [source, invoke, parent],
            [Edge(source, "flow", invoke), Edge(invoke, "complete", parent)]
        );
        var scenarios = Scenarios(fixture);
        var input = scenarios.CreateDefaultFixture(draft, source.Id);
        _ = await fixture.SaveAsync(draft.Nodes, draft.Edges);
        writes.Armed = true;
        var result = (
            await scenarios.RunAsync(draft, input, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        result.Nodes.ShouldContain(node =>
            node.OutcomeCode == "marker-invalid"
            && node.State
                == (
                    innerContinues
                        ? AutomationNodeRunState.ContinuedAfterFailure
                        : AutomationNodeRunState.Failed
                )
        );
        AssertNoNativeEffects(fixture);
        writes.Writes.ShouldBe(0);
        writes.Armed = false;
        _ = await fixture.FlowRuntime.DispatchAsync(
            new(
                input.Context with
                {
                    Event = input.Context.Event with { OccurrenceId = Guid.NewGuid() },
                },
                new PollStartedSourceConfiguration()
            ),
            CancellationToken.None
        );
        fixture.Chat.Messages.ShouldBe(
            innerContinues
                ? ["literal-${unavailable}", "parent continuation"]
                : ["parent continuation"]
        );
        fixture.ClipsMarkers.Markers.ShouldBeEmpty();
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain("marker-invalid");
        (await fixture.RunStatusesAsync()).ShouldBe([AutomationFlowRunStatus.Completed]);
    }
}
