using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class NativeOperationAutomationTests
{
    [Test]
    [Arguments("create-marker", "", "marker-invalid")]
    [Arguments("create-marker", "long", "marker-invalid")]
    [Arguments("create-marker", "number", "sensitive-output-blocked")]
    [Arguments("start-poll", "one", "poll-invalid")]
    [Arguments("start-prediction", "one", "prediction-invalid")]
    [Arguments("resolve-prediction", "", "prediction-outcome-invalid")]
    public async Task NativePreparation_ExpandedInvalidPayloadFailsLiveAndScenarioBeforeEffects(
        string type,
        string value,
        string code
    )
    {
        var writes = new AutomationRuntimeTests.ScenarioWriteGuard();
        await using var fixture = await NativeFixture.CreateAsync(interceptors: [writes]);
        await RecordBroadcasterGrant(fixture);
        var source = Node("poll-started", "{}");
        var action =
            value == "number"
                ? WithExpression(PreparedAction(type), "description", "42")
                : PreparedAction(type);
        var draft = Draft(fixture.HostId, [source, action], [Edge(source, "flow", action)]);
        var scenarios = Scenarios(fixture);
        var input = PollFixture(
            scenarios,
            draft,
            source,
            value == "long" ? new string('x', 141) : value
        );
        var saved = await fixture.SaveAsync(draft.Nodes, draft.Edges);
        writes.Armed = true;
        var simulated = (
            await scenarios.RunAsync(draft, input, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Failed>();
        simulated.Nodes[^1].OutcomeCode.ShouldBe(code);
        writes.Writes.ShouldBe(0);
        writes.TraceWrites.ShouldBeGreaterThan(0);
        AssertNoNativeEffects(fixture);
        writes.Armed = false;
        var dispatched = await fixture.FlowRuntime.DispatchAsync(
            new(
                input.Context with
                {
                    Event = input.Context.Event with { OccurrenceId = Guid.NewGuid() },
                },
                new PollStartedSourceConfiguration()
            ),
            CancellationToken.None
        );
        _ = dispatched.RunIds.ShouldHaveSingleItem();
        (await fixture.NodeOutcomeCodesAsync()).ShouldContain(code);
        (await fixture.RunStatusesAsync()).ShouldBe([AutomationFlowRunStatus.Failed]);
        AssertNoNativeEffects(fixture);
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.SingleAsync()).FlowId.ShouldBe(saved.Value);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativePreparation_TemplateAndExpressionListsKeepDifferentSplitBoundaries(
        bool expression
    )
    {
        var writes = new AutomationRuntimeTests.ScenarioWriteGuard();
        await using var fixture = await NativeFixture.CreateAsync(interceptors: [writes]);
        await RecordBroadcasterGrant(fixture);
        var source = Node("poll-started", "{}");
        var action = Node(
            "start-poll",
            """{"title":"  Question  ","choices":" A \n${channel.display_name} ","duration-seconds":60,"channel-points-per-vote":500}"""
        );
        if (expression)
        {
            action = WithExpression(action, "choices", "channel.display_name");
        }
        var draft = Draft(fixture.HostId, [source, action], [Edge(source, "flow", action)]);
        var scenarios = Scenarios(fixture);
        var input = PollFixture(scenarios, draft, source, " B\nC ");
        _ = await fixture.SaveAsync(draft.Nodes, draft.Edges);
        writes.Armed = true;
        _ = (
            await scenarios.RunAsync(draft, input, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
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
        var prepared = fixture.Polls.Started.ShouldHaveSingleItem().Draft;
        prepared.Title.ShouldBe("Question");
        prepared.Choices.ShouldBe(expression ? ["B", "C"] : ["A", "B\nC"]);
        prepared.DurationSeconds.ShouldBe(60);
        prepared.ChannelPointsVotingEnabled.ShouldBeTrue();
        prepared.ChannelPointsPerVote.ShouldBe(500);
    }

    [Test]
    [Arguments("create-marker")]
    [Arguments("start-poll")]
    [Arguments("start-prediction")]
    public async Task NativePreparation_SensitiveExpandedTextCannotReachLiveOrHypotheticalSuccess(
        string type
    )
    {
        await using var fixture = await NativeFixture.CreateAsync();
        await RecordBroadcasterGrant(fixture);
        var source = Node("stream-online", "{}");
        var action = PreparedAction(type);
        var field = type == "create-marker" ? "description" : "title";
        action = WithExpression(action, field, "stream.title");
        var draft = Draft(fixture.HostId, [source, action], [Edge(source, "flow", action)]);
        var scenarios = Scenarios(fixture);
        var input = scenarios.CreateDefaultFixture(draft, source.Id);
        input = input with
        {
            Context = input.Context with
            {
                Stream = input.Context.Stream! with { Title = "private-message-never-in-trace" },
            },
        };
        var simulated = (
            await scenarios.RunAsync(draft, input, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Failed>();
        simulated.Nodes[^1].OutcomeCode.ShouldBe("sensitive-output-blocked");
        var check = fixture
            .Catalog.ValidatePersistedDefinition(action.Definition)
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>();
        var live = await fixture.Executor.ExecuteAsync(
            new(fixture.HostId),
            check.Configuration,
            action.InputBindings!.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value.Expression!
            ),
            ImmutableDictionary<AutomationConfigurationFieldId, AutomationResolvedValue>.Empty,
            input.Context,
            CancellationToken.None
        );
        live.ShouldBeOfType<AutomationActionOutcome.Failed>()
            .Code.ShouldBe("sensitive-output-blocked");
        AssertNoNativeEffects(fixture);
        await using var db = await fixture.Database.CreateDbContextAsync();
        var trace = await db
            .AutomationTraceEvents.AsNoTracking()
            .Select(row => row.EventJson)
            .ToArrayAsync();
        string.Join("", trace).ShouldNotContain("private-message-never-in-trace");
    }

    private static AutomationFlowDraftNode PreparedAction(string type) =>
        type switch
        {
            "create-marker" => Node(type, """{"description":"${channel.display_name}"}"""),
            "start-poll" => WithExpression(
                Node(type, """{"title":"Question","choices":"Yes\nNo","duration-seconds":60}"""),
                "choices",
                "channel.display_name"
            ),
            "start-prediction" => WithExpression(
                Node(type, """{"title":"Question","outcomes":"Yes\nNo","window-seconds":120}"""),
                "outcomes",
                "channel.display_name"
            ),
            "resolve-prediction" => Node(
                type,
                """{"winning-outcome-id":"${channel.display_name}"}"""
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

    private static AutomationFlowDraftNode WithExpression(
        AutomationFlowDraftNode node,
        string field,
        string expression
    ) =>
        node with
        {
            InputBindings = (
                node.InputBindings
                ?? ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>.Empty
            ).SetItem(
                new(field),
                new(
                    AutomationInputBindingMode.Expression,
                    new(AutomationExpressionLanguage.CurrentVersion, expression)
                )
            ),
        };

    private static AutomationScenarioService Scenarios(
        NativeFixture fixture,
        BotSettings? settings = null
    ) =>
        new(
            fixture.Database,
            fixture.Catalog,
            fixture.Flows,
            fixture.Clock,
            new AutomationExpressionService(),
            settings ?? BotSettings.FromOptions(new BotOptions()),
            OAuthAuthorizationScopeSet.Create(["channel:bot"])
        );

    private static AutomationScenarioFixture PollFixture(
        AutomationScenarioService scenarios,
        AutomationFlowDraft draft,
        AutomationFlowDraftNode source,
        string title
    )
    {
        var fixture = scenarios.CreateDefaultFixture(draft, source.Id);
        return fixture with
        {
            Context = fixture.Context with
            {
                Channel = fixture.Context.Channel with { DisplayName = title },
            },
        };
    }

    private static async Task RecordBroadcasterGrant(NativeFixture fixture)
    {
        await using var db = await fixture.Database.CreateDbContextAsync();
        _ = db.HostBroadcasterAuthorizations.Add(
            new()
            {
                HostId = fixture.HostId,
                TwitchUserId = "streamer-id",
                ProtectedTokenPayload = [1, 2, 3],
                AuthorizedScopes = ScopeSet.Format(
                    HostBroadcasterAuthorizationService.MilestoneScopes
                ),
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static void AssertNoNativeEffects(NativeFixture fixture)
    {
        fixture.Chat.Messages.ShouldBeEmpty();
        fixture.Shoutouts.Sent.ShouldBeEmpty();
        fixture.ClipsMarkers.Clips.ShouldBeEmpty();
        fixture.ClipsMarkers.Markers.ShouldBeEmpty();
        fixture.Polls.Started.ShouldBeEmpty();
        fixture.Polls.Ended.ShouldBeEmpty();
        fixture.Predictions.Started.ShouldBeEmpty();
        fixture.Predictions.Locked.ShouldBeEmpty();
        fixture.Predictions.Cancelled.ShouldBeEmpty();
        fixture.Predictions.Resolved.ShouldBeEmpty();
    }
}
