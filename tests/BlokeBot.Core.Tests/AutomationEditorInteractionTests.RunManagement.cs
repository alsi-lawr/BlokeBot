using System.Security.Claims;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    public async Task RunManagementUi_DismissalSingleCancelStopAdmissionsAndFeatureOffBulkKeepHistory()
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync(
            authored: WaitingRunEditor()
        );
        var page = f.Page;
        var (host, flow) = await RunWaitingAsync(f);
        FindButton(page, "Run details").Click();
        FindButton(page, "Refresh").HasAttribute("disabled").ShouldBeFalse();
        FindButton(page, "Hide runs").GetAttribute("aria-expanded").ShouldBe("true");
        FindButton(page, "Cancel flow runs").Click();
        FindButton(page, "Confirm cancellation").HasAttribute("disabled").ShouldBeFalse();
        FindButton(page, "Keep runs").HasAttribute("disabled").ShouldBeFalse();
        var focus = f.FocusCalls;
        FindButton(page, "Keep runs").Click();
        f.FocusCalls.ShouldBeGreaterThan(focus);
        (await Status()).ShouldBe(AutomationFlowRunStatus.Waiting);
        FindButton(page, "Cancel run").Click();
        (await Status()).ShouldBe(AutomationFlowRunStatus.Cancelled);
        _ = await f
            .Context.Services.GetRequiredService<AutomationManualRunService>()
            .RunAsync(new(host), flow, default);
        FlowStateButton(page).Click();
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (await db.AutomationFlows.SingleAsync()).IsEnabled.ShouldBeFalse();
            (
                await db.AutomationFlowRuns.CountAsync(run =>
                    run.Status == AutomationFlowRunStatus.Waiting
                )
            ).ShouldBe(1);
            var record = await db.Hosts.SingleAsync();
            record.EnabledFeatures &= ~HostFeatureFlags.Automations;
            _ = await db.SaveChangesAsync();
        }
        page.Dispose();
        page = f.Context.Render<AutomationEditorPage>();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationRunDrawer>().Instance.Visible.ShouldBeTrue()
        );
        FindButton(page, "Run details").Click();
        FindButton(page, "Cancel channel runs").Click();
        FindButton(page, "Keep runs").Click();
        FindButton(page, "Cancel channel runs").Click();
        FindButton(page, "Confirm cancellation").Click();
        await using var final = await f.Database.CreateDbContextAsync();
        (await final.AutomationFlowRuns.ToArrayAsync()).ShouldAllBe(run =>
            run.Status == AutomationFlowRunStatus.Cancelled
        );
        (await final.AutomationFlows.CountAsync()).ShouldBe(1);
        (
            await final.AutomationTraces.CountAsync(trace =>
                trace.TerminalOutcome == (int)AutomationTraceOutcome.Cancelled
            )
        ).ShouldBe(2);

        async Task<AutomationFlowRunStatus> Status()
        {
            await using var db = await f.Database.CreateDbContextAsync();
            return await db.AutomationFlowRuns.Select(run => run.Status).SingleAsync();
        }
    }

    [Test]
    public async Task RunManagementUi_ChangedSelectedHostCannotApplyCapturedBulkConfirmation()
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync(
            authored: WaitingRunEditor()
        );
        var (host, _) = await RunWaitingAsync(f);
        FindButton(f.Page, "Run details").Click();
        FindButton(f.Page, "Cancel flow runs").Click();
        int otherHost;
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var other = new BotHost
            {
                Login = "other",
                DisplayName = "Other",
                TwitchUserId = "other-id",
                CreatedAtUtc = DateTime.UtcNow,
                EnabledFeatures = HostFeatureFlags.Automations,
            };
            _ = db.Hosts.Add(other);
            _ = await db.SaveChangesAsync();
            otherHost = other.Id;
        }
        var selected = new BotHostChoice(otherHost, "other", "Other", AuthRole.Streamer);
        _ = f.Authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "streamer-id"),
            new Claim(ClaimTypes.Name, "streamer"),
            new Claim(AuthClaims.Login, "streamer"),
            new Claim(AuthClaims.Role, AuthRoleCodec.Encode(AuthRole.Streamer)),
            new Claim(AuthClaims.CanCreateHost, "false"),
            new Claim(AuthClaims.IsBotAdmin, "false"),
            new Claim(AuthClaims.IsBotAccount, "false"),
            new Claim(BotHostClaims.AvailableHost, BotHostClaimCodec.Encode(selected)),
            new Claim(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(selected))
        );
        FindButton(f.Page, "Confirm cancellation").Click();
        await using var verify = await f.Database.CreateDbContextAsync();
        (
            await verify.AutomationFlowRuns.Where(run => run.HostId == host).SingleAsync()
        ).Status.ShouldBe(AutomationFlowRunStatus.Waiting);
        (await verify.AutomationFlowRuns.CountAsync(run => run.HostId == otherHost)).ShouldBe(0);
    }

    private static AngleSharp.Dom.IElement FindButton(
        IRenderedComponent<AutomationEditorPage> page,
        string text
    ) => page.FindAll("button").Single(button => button.TextContent.Trim() == text);

    private static AutomationEditorState WaitingRunEditor()
    {
        var editor = AutomationEditorState.Create("Waiting management fixture");
        editor.IsEnabled = true;
        var source = editor.AddNode(
            new ExpandedAutomationCatalogModule()
                .Definitions.Single(definition =>
                    definition.Descriptor.Id == AutomationDefinitionIds.ManualSource
                )
                .Descriptor
        );
        var delay = editor.AddNode(
            ProductionDefinitions()
                .Single(definition => definition.Id == AutomationDefinitionIds.DelayControl)
        );
        delay.SetValue(new("duration-milliseconds"), "60000");
        var send = editor.AddNode(
            ProductionDefinitions()
                .Single(definition => definition.Id == AutomationDefinitionIds.SendChatAction)
        );
        send.SetValue(new("message"), "not yet");
        editor.Edges.Add(
            new(
                Guid.NewGuid(),
                AutomationEdgeKind.Flow,
                source.Id,
                new("flow"),
                delay.Id,
                new("flow")
            )
        );
        editor.Edges.Add(
            new(
                Guid.NewGuid(),
                AutomationEdgeKind.Flow,
                delay.Id,
                new("complete"),
                send.Id,
                new("flow")
            )
        );
        return editor;
    }

    private static async Task<(int Host, AutomationFlowId Flow)> RunWaitingAsync(
        AutomationEditorPageFixture f
    )
    {
        await using var db = await f.Database.CreateDbContextAsync();
        var host = await db.Hosts.Select(value => value.Id).SingleAsync();
        var flow = new AutomationFlowId(
            await db.AutomationFlows.Select(value => value.Id).SingleAsync()
        );
        _ = (
            await f
                .Context.Services.GetRequiredService<AutomationManualRunService>()
                .RunAsync(new(host), flow, default)
        ).ShouldBeOfType<AutomationManualRunOutcome.Dispatched>();
        (await db.AutomationFlowRuns.Select(run => run.Status).SingleAsync()).ShouldBe(
            AutomationFlowRunStatus.Waiting
        );
        return (host, flow);
    }
}
