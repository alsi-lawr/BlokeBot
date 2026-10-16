using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.ConfigurationTransfer.Page;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.Dashboards.Page;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Core.Hosting;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using EditorPage = BlokeBot.Core.Features.Dashboards.Page.DashboardPage;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments("saved")]
    [Arguments("conflict")]
    [Arguments("revoked")]
    public async Task Dashboard340_ActualImportSelectorRealSettingsAndSave_RepairsWithoutNativeDispatch(
        string outcome
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var manual = Node(AutomationDefinitionIds.ManualSource.Value, """{"data":"saved"}""");
        var flow = await f.SaveAsync([manual], []);
        _ = (
            await f.Countdowns.ApplyAsync(
                new(f.HostId),
                new("existing", TimeSpan.FromSeconds(90), CountdownOperation.Start),
                default
            )
        ).ShouldBeOfType<AutomationCountdownApplyOutcome.Started>();
        await using var initialRead = await f.Database.CreateDbContextAsync();
        var originalTimer = await initialRead.AutomationCountdowns.AsNoTracking().SingleAsync();
        var uiFixture = UiTestContextFactory.CreateWithAuthorization(
            (SqliteBlokeBotDbFactory)f.Database,
            f.HostId
        );
        await using var uiState = uiFixture.Context;
        var authorization = uiFixture.Authorization;
        var moderator = new DashboardControlModerator();
        _ = uiState.Services.AddSingleton<IModeratorAuthorityService>(moderator);
        _ = uiState.Services.AddSingleton(f.Catalog);
        _ = uiState.Services.AddSingleton(f.Flows);
        _ = uiState.Services.AddSingleton(f.Scenarios);
        _ = uiState.Services.AddSingleton(f.UiChanges);
        _ = uiState.Services.AddSingleton<IAutomationUiChangePublisher>(f.UiChanges);
        _ = uiState.Services.AddSingleton<IPublicChatMessageSender>(f.Chat);
        _ = uiState.Services.AddSingleton(
            new AutomationManualRunService(f.Database, f.Runtime, f.Catalog, f.Clock)
        );
        _ = uiState.Services.AddSingleton(f.Queries);
        _ = uiState.Services.AddSingleton(
            new AutomationCountdownControlService(f.Database, f.Countdowns, f.Catalog)
        );
        var operations = new TransferOperationRecorder();
        _ = uiState.Services.AddSingleton<IPollDashboardOperations>(operations);
        _ = uiState.Services.AddSingleton<IPredictionDashboardOperations>(operations);
        var events = TestEventBus.Create<AppEventKind>();
        _ = uiState.Services.AddSingleton(events);
        var scheduler = new GiveawayLifecycleTestServices.Scheduler();
        var lifecycle = new AutomationFeatureLifecycle(
            () => ExpandedFixtureFor(f).Runtime,
            NullLogger<AutomationFeatureLifecycle>.Instance
        );
        _ = uiState.Services.AddSingleton(
            GiveawayLifecycleTestServices.Create(f.Database, scheduler, lifecycle)
        );
        _ = uiState.Services.AddSingleton(new GuessingDashboardService(f.Database));
        _ = uiState.Services.AddSingleton(
            new GuessingRoundService(
                f.Database,
                new(events),
                new PointBalanceService(f.Database),
                new PointsChangeNotifier(events)
            )
        );
        _ = uiState.Services.AddBlokeBotDashboards();
        var reconciliation = new TransferReconciliationRecorder();
        _ =
            uiState.Services.AddSingleton<BlokeBot.Twitch.Runtime.IEventSubChannelReconciliationTrigger>(
                reconciliation
            );
        _ = uiState.Services.AddBlokeBotConfigurationTransfer();
        var configs = new DashboardConfigurationV1[]
        {
            new DashboardConfigurationV1.Automation(
                new DashboardReferenceV1.Automation("Missing", "Chosen"),
                ""
            ),
            new DashboardConfigurationV1.Countdown(
                new DashboardReferenceV1.Countdown("existing"),
                60
            ),
            new DashboardConfigurationV1.Value(
                new DashboardReferenceV1.Value(
                    "missing",
                    CustomValueScope.Global,
                    CustomValueKind.Number
                )
            ),
            new DashboardConfigurationV1.Poll(new DashboardReferenceV1.Poll("missing")),
            new DashboardConfigurationV1.Prediction(new DashboardReferenceV1.Prediction("missing")),
            new DashboardConfigurationV1.Giveaway(new DashboardReferenceV1.Giveaway()),
            new DashboardConfigurationV1.Guessing(new DashboardReferenceV1.Guessing("missing")),
        };
        var doc = new ConfigurationDocumentV2(
            ConfigurationDocumentCodec.Format,
            2,
            DateTimeOffset.UtcNow,
            new("source", null),
            new(
                Dashboards: new([
                    new(
                        "layout",
                        "Imported",
                        configs
                            .Select(
                                (config, index) =>
                                    new DashboardWidgetV1(
                                        $"widget-{index}",
                                        $"Widget {index}",
                                        config
                                    )
                            )
                            .ToArray(),
                        new(
                            12,
                            56,
                            configs
                                .Select(
                                    (_, index) =>
                                        new DashboardWidgetPlacementV1(
                                            $"widget-{index}",
                                            new(index % 3 * 4, index / 3 * 3, 4, 3, index)
                                        )
                                )
                                .ToArray()
                        ),
                        null
                    ),
                ])
            )
        );
        var navigation = uiState.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/configuration-transfer#import");
        var transfer = uiState.Render<ConfigurationTransferPage>();
        var json = Encoding.UTF8.GetString(new ConfigurationDocumentCodec().Serialize(doc));
        foreach (var collection in new[] { "layouts", "widgets" })
        {
            var malformed = JsonNode.Parse(json)!;
            var layouts = malformed["sections"]!["dashboards"]!["layouts"]!.AsArray();
            var items = collection == "layouts" ? layouts : layouts[0]!["widgets"]!.AsArray();
            items[0] = null;
            transfer.Find("#configuration-transfer-json").Change(malformed.ToJsonString());
            await transfer.Find("#configuration-transfer-preview").ClickAsync(new());
            transfer
                .FindComponents<PageState>()
                .ShouldContain(state => state.Instance.Kind == PageStateKind.Failure);
            await using var rejectedRead = await f.Database.CreateDbContextAsync();
            (await rejectedRead.ChannelDashboards.AnyAsync()).ShouldBeFalse();
            (await rejectedRead.ConfigurationImportAudits.AnyAsync()).ShouldBeFalse();
        }
        transfer.Find("#configuration-transfer-json").Change(json);
        transfer.Find("#configuration-transfer-preview").Click();
        transfer.WaitForAssertion(() =>
            transfer.Find("#configuration-transfer-apply").HasAttribute("disabled").ShouldBeFalse()
        );
        await transfer.Find("#configuration-transfer-apply").ClickAsync(new());
        ChannelDashboard imported;
        await using (var read = await f.Database.CreateDbContextAsync())
        {
            imported = await read.ChannelDashboards.SingleAsync();
        }
        var widget = DashboardDocuments.Deserialize(imported.DocumentJson).Widgets[0];
        widget
            .Configuration.Binding.ShouldBeOfType<DashboardBinding.Unconfigured>()
            .Issue.ShouldBe(DashboardBindingIssue.Missing);
        imported.Revision.ShouldBe(1);
        transfer.Dispose();
        if (outcome == "revoked")
        {
            var choice = new BotHostChoice(f.HostId, "streamer", "Streamer", AuthRole.Moderator);
            _ = authorization.SetClaims(
                new Claim(ClaimTypes.NameIdentifier, "mod-id"),
                new Claim(ClaimTypes.Name, "moderator"),
                new Claim(AuthClaims.Login, "moderator"),
                new Claim(AuthClaims.Role, AuthRoleCodec.Encode(AuthRole.Moderator)),
                new Claim(BotHostClaims.AvailableHost, BotHostClaimCodec.Encode(choice)),
                new Claim(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(choice))
            );
        }

        var module = uiState.JSInterop.SetupModule(
            "./Features/Dashboards/Page/DashboardPage.razor.js"
        );
        var grid = module.SetupModule("create", _ => true);
        grid.Mode = JSRuntimeMode.Loose;
        _ = grid.Setup<DashboardPendingPlacement[]>("flush").SetResult([]);
        navigation.NavigateTo("/dashboards/editor");
        var page = uiState.Render<EditorPage>();
        page.Find(".dashboard-selector select").Change(imported.Id.ToString());
        await page.Find("button[aria-label='Open dashboard editor']").ClickAsync(new());
        navigation.Uri.ShouldEndWith($"/dashboards/editor/{imported.Id}");
        // bUnit does not deliver route parameters when NavigationManager changes the URI.
        page.Render(parameters => parameters.Add(component => component.Dashboard, imported.Id));
        await page.Find($"button[data-settings-for='{widget.Id.Value}']").ClickAsync(new());
        var settings = page.FindComponent<DashboardWidgetSettings>();
        settings
            .Instance.View.Widget.Configuration.Binding.ShouldBeOfType<DashboardBinding.Unconfigured>()
            .Reference.ShouldNotBeNullOrWhiteSpace();
        await settings
            .Find("select")
            .ChangeAsync(new ChangeEventArgs { Value = $"{flow.Value}/{manual.Id.Value}" });
        _ =
            settings.Instance.View.Widget.Configuration.Binding.ShouldBeOfType<DashboardBinding.Configured>();
        await using (var before = await f.Database.CreateDbContextAsync())
        {
            _ = DashboardDocuments
                .Deserialize((await before.ChannelDashboards.SingleAsync()).DocumentJson)
                .Widgets[0]
                .Configuration.Binding.ShouldBeOfType<DashboardBinding.Unconfigured>();
        }
        if (outcome == "conflict")
        {
            var service = uiState.Services.GetRequiredService<DashboardConfigurationService>();
            var current = await uiState
                .Services.GetRequiredService<BlokeBotPageContextAccessor>()
                .FromAsync(
                    uiState
                        .Services.GetRequiredService<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>()
                        .GetAuthenticationStateAsync()
                );
            _ = (
                await service.SaveAsync(
                    current.Session,
                    new(
                        new(f.HostId),
                        new(imported.Id),
                        new(1),
                        "Other operator",
                        DashboardDocuments.Deserialize(imported.DocumentJson)
                    ),
                    default
                )
            ).ShouldBeOfType<DashboardSaveOutcome.Saved>();
        }
        else if (outcome == "revoked")
        {
            moderator.Outcome = new ModeratorAuthorityOutcome.Revoked();
        }
        await page.Find("button[aria-label='Save dashboard']").ClickAsync(new());
        await using var verify = await f.Database.CreateDbContextAsync();
        var saved = await verify.ChannelDashboards.SingleAsync();
        var binding = DashboardDocuments
            .Deserialize(saved.DocumentJson)
            .Widgets[0]
            .Configuration.Binding;
        if (outcome == "saved")
        {
            var target = binding
                .ShouldBeOfType<DashboardBinding.Configured>()
                .Target.ShouldBeOfType<DashboardTarget.Automation>();
            target.FlowId.ShouldBe(flow);
            target.SourceNodeId.ShouldBe(manual.Id);
            saved.Revision.ShouldBe(2);
            page.Dispose();
            var reload = uiState.Render<EditorPage>(parameters =>
                parameters.Add(component => component.Dashboard, imported.Id)
            );
            await reload.Find($"button[data-settings-for='{widget.Id.Value}']").ClickAsync(new());
            reload
                .FindComponent<DashboardWidgetSettings>()
                .Instance.View.Widget.Configuration.Binding.ShouldBe(binding);
        }
        else
        {
            _ = binding.ShouldBeOfType<DashboardBinding.Unconfigured>();
            _ = page.FindComponent<DashboardWidgetSettings>()
                .Instance.View.Widget.Configuration.Binding.ShouldBeOfType<DashboardBinding.Configured>();
            page.Find("button[aria-label='Undo']").HasAttribute("disabled").ShouldBeFalse();
            saved.Revision.ShouldBe(outcome == "conflict" ? 2 : 1);
        }
        reconciliation.Calls.ShouldBe(0);
        operations.Calls.ShouldBe(0);
        f.Chat.Messages.ShouldBeEmpty();
        scheduler.Scheduled.ShouldBeEmpty();
        scheduler.Cancelled.ShouldBeEmpty();
        (await verify.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await verify.Rounds.CountAsync()).ShouldBe(0);
        (await verify.PointsGiveaways.CountAsync()).ShouldBe(0);
        var timer = await verify.AutomationCountdowns.SingleAsync();
        timer.IsRunning.ShouldBeTrue();
        timer.OccurrenceId.ShouldBe(originalTimer.OccurrenceId);
        timer.StartedAtUtc.ShouldBe(originalTimer.StartedAtUtc);
        timer.DeadlineUtc.ShouldBe(originalTimer.DeadlineUtc);
    }

    private sealed class TransferReconciliationRecorder
        : BlokeBot.Twitch.Runtime.IEventSubChannelReconciliationTrigger
    {
        internal int Calls { get; private set; }

        public Task ReconcileRevocationAsync(string subscriptionId, CancellationToken ct) =>
            ReconcileAsync(ct);

        public Task ReconcileAsync(CancellationToken ct)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class TransferOperationRecorder
        : IPollDashboardOperations,
            IPredictionDashboardOperations
    {
        internal int Calls { get; private set; }

        private Task<T> Unexpected<T>()
        {
            Calls++;
            return Task.FromException<T>(
                new InvalidOperationException("Editor dispatched a native operation")
            );
        }

        Task<PollControlState> IPollDashboardOperations.ReadControlAsync(
            int host,
            int template,
            CancellationToken ct
        ) => Unexpected<PollControlState>();

        Task<PollDashboardState> IPollDashboardOperations.LoadAsync(
            int host,
            CancellationToken ct
        ) => Unexpected<PollDashboardState>();

        Task<PollOperationOutcome> IPollDashboardOperations.SaveTemplateAsync(
            int host,
            PollTemplateDraft draft,
            CancellationToken ct
        ) => Unexpected<PollOperationOutcome>();

        Task<PollOperationOutcome> IPollDashboardOperations.StartAsync(
            int host,
            int template,
            CancellationToken ct
        ) => Unexpected<PollOperationOutcome>();

        Task<PollOperationOutcome> IPollDashboardOperations.EndAsync(
            int host,
            bool confirm,
            CancellationToken ct
        ) => Unexpected<PollOperationOutcome>();

        Task<PredictionControlState> IPredictionDashboardOperations.ReadControlAsync(
            int host,
            int template,
            CancellationToken ct
        ) => Unexpected<PredictionControlState>();

        Task<PredictionDashboardState> IPredictionDashboardOperations.LoadAsync(
            int host,
            CancellationToken ct
        ) => Unexpected<PredictionDashboardState>();

        Task<PredictionOperationOutcome> IPredictionDashboardOperations.SaveTemplateAsync(
            int host,
            PredictionTemplateDraft draft,
            CancellationToken ct
        ) => Unexpected<PredictionOperationOutcome>();

        Task<PredictionOperationOutcome> IPredictionDashboardOperations.DeleteTemplateAsync(
            int host,
            int template,
            CancellationToken ct
        ) => Unexpected<PredictionOperationOutcome>();

        Task<PredictionOperationOutcome> IPredictionDashboardOperations.StartAsync(
            int host,
            int template,
            CancellationToken ct
        ) => Unexpected<PredictionOperationOutcome>();

        Task<PredictionOperationOutcome> IPredictionDashboardOperations.LockAsync(
            int host,
            bool confirm,
            CancellationToken ct
        ) => Unexpected<PredictionOperationOutcome>();

        Task<PredictionOperationOutcome> IPredictionDashboardOperations.CancelAsync(
            int host,
            bool confirm,
            CancellationToken ct
        ) => Unexpected<PredictionOperationOutcome>();

        Task<PredictionOperationOutcome> IPredictionDashboardOperations.ResolveAsync(
            int host,
            string winner,
            bool confirm,
            CancellationToken ct
        ) => Unexpected<PredictionOperationOutcome>();
    }
}
