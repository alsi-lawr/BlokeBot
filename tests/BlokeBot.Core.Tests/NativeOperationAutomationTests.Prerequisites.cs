using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class NativeOperationAutomationTests
{
    [Test]
    [Arguments("absent", false)]
    [Arguments("payload", false)]
    [Arguments("scope", false)]
    [Arguments("subject", false)]
    [Arguments("unrecorded-subject", true)]
    [Arguments("complete", true)]
    public async Task NativePrerequisites_BroadcasterUsesWholeMilestoneGrantWithoutClaimingLiveValidity(
        string grant,
        bool hypothetical
    )
    {
        var writes = new AutomationRuntimeTests.ScenarioWriteGuard();
        await using var fixture = await NativeFixture.CreateAsync(interceptors: [writes]);
        if (grant != "absent")
        {
            await RecordBroadcasterGrant(fixture);
            await using var edit = await fixture.Database.CreateDbContextAsync();
            var row = await edit.HostBroadcasterAuthorizations.SingleAsync();
            if (grant == "payload")
            {
                row.ProtectedTokenPayload = [];
            }
            if (grant == "scope")
            {
                row.AuthorizedScopes = ScopeSet.Format(
                    HostBroadcasterAuthorizationService.MilestoneScopes.Where(scope =>
                        scope != "channel:read:polls"
                    )
                );
            }
            if (grant == "subject")
            {
                row.TwitchUserId = "other-host";
            }
            if (grant == "unrecorded-subject")
            {
                row.TwitchUserId = null;
            }
            _ = await edit.SaveChangesAsync();
        }
        var source = Node("poll-started", "{}");
        var marker = Node("create-marker", """{"description":"valid"}""");
        var draft = Draft(fixture.HostId, [source, marker], [Edge(source, "flow", marker)]);
        writes.Armed = true;
        var result = await Scenarios(fixture)
            .RunDefaultAsync(draft, source.Id, CancellationToken.None);
        if (hypothetical)
        {
            result
                .ShouldBeOfType<AutomationScenarioRunOutcome.Completed>()
                .Nodes[^1]
                .OutcomeCode.ShouldBe("action-simulated");
        }
        else
        {
            result
                .ShouldBeOfType<AutomationScenarioRunOutcome.Failed>()
                .Nodes[^1]
                .OutcomeCode.ShouldBe("broadcaster-authorization-missing");
        }
        writes.Writes.ShouldBe(0);
        AssertNoNativeEffects(fixture);
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await db.TwitchStreamMarkers.CountAsync()).ShouldBe(0);
    }

    [Test]
    [Arguments("main", true)]
    [Arguments("disabled-override", true)]
    [Arguments("custom", true)]
    [Arguments("custom-payload", false)]
    [Arguments("configured-scope", false)]
    [Arguments("channel-grant", false)]
    [Arguments("channel-scope", false)]
    [Arguments("unknown-destination", true)]
    public async Task NativePrerequisites_ChatUsesNormalizedDestinationCustomIdentityAndConfiguredScopes(
        string state,
        bool hypothetical
    )
    {
        var writes = new AutomationRuntimeTests.ScenarioWriteGuard();
        await using var fixture = await NativeFixture.CreateAsync(interceptors: [writes]);
        var destination = await fixture.SeedHostAsync("destination", NativeFixture.AllFeatures);
        var settings = BotSettings.FromOptions(
            new BotOptions { Identity = new() { Scopes = ["chat:edit", "user:read:chat"] } }
        );
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(row => row.Id == destination);
            host.ChannelBotAuthorizedAtUtc = state == "channel-grant" ? null : _start.UtcDateTime;
            host.ChannelBotAuthorizedScopes =
                state == "channel-scope" ? "unrelated" : "channel:bot";
            if (state is not ("main" or "unknown-destination"))
            {
                _ = db.HostBotAccountSettings.Add(
                    new()
                    {
                        HostId = destination,
                        OverrideEnabled = state != "disabled-override",
                        TwitchUserId = "independent-bot-id",
                        Login = "independent-bot",
                        ProtectedTokenPayload = state is "custom-payload" or "disabled-override"
                            ? null
                            : [9],
                        AuthorizedScopes =
                            state == "configured-scope"
                                ? "chat:edit"
                                : ScopeSet.Format(settings.Identity.Scopes),
                    }
                );
            }
            _ = await db.SaveChangesAsync();
        }
        var source = Node("poll-started", "{}");
        var chat = Node("send-chat", """{"message":"hello"}""");
        var draft = Draft(fixture.HostId, [source, chat], [Edge(source, "flow", chat)]);
        var scenarios = Scenarios(fixture, settings);
        var input = scenarios.CreateDefaultFixture(draft, source.Id);
        input = input with
        {
            Context = input.Context with
            {
                Channel = input.Context.Channel with
                {
                    Login = state == "unknown-destination" ? "unrecorded" : " #DESTINATION ",
                },
            },
        };
        writes.Armed = true;
        var result = await scenarios.RunAsync(draft, input, CancellationToken.None);
        if (hypothetical)
        {
            result
                .ShouldBeOfType<AutomationScenarioRunOutcome.Completed>()
                .Nodes[^1]
                .OutcomeCode.ShouldBe("action-simulated");
        }
        else
        {
            result
                .ShouldBeOfType<AutomationScenarioRunOutcome.Failed>()
                .Nodes[^1]
                .OutcomeCode.ShouldBe("chat-rejected");
        }
        AssertNoNativeEffects(fixture);
        writes.Writes.ShouldBe(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativePrerequisites_ShoutoutUsesStoredHostAccountAndAllThreeOwnedScopes(
        bool missingScope
    )
    {
        await using var fixture = await NativeFixture.CreateAsync();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            _ = db.HostBotAccountSettings.Add(
                new()
                {
                    HostId = fixture.HostId,
                    OverrideEnabled = true,
                    ProtectedTokenPayload = [9],
                    AuthorizedScopes = ScopeSet.Format(
                        missingScope
                            ? ShoutoutService.RequiredScopes.Where(scope =>
                                scope != Scopes.ModeratorReadShoutouts
                            )
                            : ShoutoutService.RequiredScopes
                    ),
                    TwitchUserId = "custom-bot-id",
                    Login = "custom-bot",
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var source = Node("follow", "{}");
        var action = Node("send-shoutout", "{}");
        var draft = Draft(fixture.HostId, [source, action], [Edge(source, "flow", action)]);
        var scenarios = Scenarios(fixture);
        var input = scenarios.CreateDefaultFixture(draft, source.Id);
        input = input with
        {
            Context = input.Context with
            {
                Channel = input.Context.Channel with { Login = "unrecorded-destination" },
            },
        };
        var result = await scenarios.RunAsync(draft, input, CancellationToken.None);
        if (missingScope)
        {
            result
                .ShouldBeOfType<AutomationScenarioRunOutcome.Failed>()
                .Nodes[^1]
                .OutcomeCode.ShouldBe("shoutout-not-ready");
        }
        else
        {
            result
                .ShouldBeOfType<AutomationScenarioRunOutcome.Completed>()
                .Nodes[^1]
                .OutcomeCode.ShouldBe("action-simulated");
        }
        AssertNoNativeEffects(fixture);
    }
}
