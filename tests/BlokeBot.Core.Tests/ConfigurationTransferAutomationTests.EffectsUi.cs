using System.Data.Common;
using System.Text;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Page;
using BlokeBot.Persistence.Models;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferAutomationTests
{
    [Test]
    public async Task EffectsImportUi_SectionStrategyAndFileChangesDiscardApprovalWhilePreservingChoices()
    {
        var window = new EffectsPreviewWindow();
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(window);
        var host = await SeedHostAsync(database, "effects-ui");
        await using (var db = await database.CreateDbContextAsync())
        {
            _ = await db.Hosts.ExecuteUpdateAsync(set =>
                set.SetProperty(row => row.EnabledFeatures, HostFeatureFlags.Automations)
            );
        }
        await using var context = UiTestContextFactory.Create(database, host);
        var transfer = AutomationTransfer(database);
        _ = context.Services.AddSingleton(transfer.FlowService);
        _ = context.Services.AddSingleton(transfer.Catalog);
        _ = context.Services.AddSingleton(
            new AutomationScenarioService(
                database,
                transfer.Catalog,
                transfer.FlowService,
                TimeProvider.System,
                new(),
                BotSettings.FromOptions(new BotOptions()),
                OAuthAuthorizationScopeSet.Create(["channel:bot"])
            )
        );
        _ = context.Services.AddSingleton(TestAutomationUiChanges.Create());
        _ = context.Services.AddBlokeBotConfigurationTransfer();
        _ = context.Services.AddSingleton(
            Coordinator(database, new RecordingLogger<ConfigurationTransferCoordinator>())
        );
        context
            .Services.GetRequiredService<BunitNavigationManager>()
            .NavigateTo("configuration-transfer#import");
        var page = context.Render<ConfigurationTransferPage>();
        var document = NestedDocument();
        var section = document.Sections.Automations!;
        document = document with
        {
            Sections = document.Sections with
            {
                Automations = section with { Flows = [section.Flows[0] with { Enabled = true }] },
            },
        };
        page.Find("#configuration-transfer-json")
            .Change(Encoding.UTF8.GetString(new ConfigurationDocumentCodec().Serialize(document)));
        page.Find("#configuration-transfer-preview").Click();
        page.WaitForAssertion(() =>
            page.FindComponent<ConfigurationTransferReviewPanel>()
                .Instance.Preview.Sections.Single()
                .AutomationReview!.RequiresApproval.ShouldBeTrue()
        );
        var review = page.FindComponent<ConfigurationTransferReviewPanel>();
        await page.InvokeAsync(() =>
            review.Instance.AutomationEffectsApprovedChanged.InvokeAsync(true)
        );
        page.Find("#configuration-transfer-apply").HasAttribute("disabled").ShouldBeFalse();
        await page.InvokeAsync(() =>
            review.Instance.SectionSelectionChanged.InvokeAsync(
                new(ConfigurationSectionId.Automations, false)
            )
        );
        review.Instance.AutomationEffectsApproved.ShouldBeFalse();
        await page.InvokeAsync(() =>
            review.Instance.SectionSelectionChanged.InvokeAsync(
                new(ConfigurationSectionId.Automations, true)
            )
        );
        page.Find("#configuration-transfer-apply").HasAttribute("disabled").ShouldBeTrue();
        await page.InvokeAsync(() =>
            review.Instance.AutomationEffectsApprovedChanged.InvokeAsync(true)
        );
        await page.InvokeAsync(() =>
            review.Instance.SectionStrategyChanged.InvokeAsync(
                new(ConfigurationSectionId.Automations, ImportConflictStrategy.AddMissing)
            )
        );
        review.Instance.AutomationEffectsApproved.ShouldBeFalse();
        review
            .Instance.Strategies[ConfigurationSectionId.Automations]
            .ShouldBe(ImportConflictStrategy.AddMissing);
        window.Armed = true;
        var reading = page.InvokeAsync(() =>
            review.Instance.SectionStrategyChanged.InvokeAsync(
                new(ConfigurationSectionId.Automations, ImportConflictStrategy.Merge)
            )
        );
        await window.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await page.InvokeAsync(() =>
                review.Instance.AutomationEffectsApprovedChanged.InvokeAsync(true)
            );
            review.Instance.AutomationEffectsApproved.ShouldBeFalse();
            await page.InvokeAsync(review.Instance.Cancel.InvokeAsync);
        }
        finally
        {
            _ = window.Release.TrySetResult();
        }
        await reading.WaitAsync(TimeSpan.FromSeconds(15));
        page.FindComponents<ConfigurationTransferReviewPanel>().ShouldBeEmpty();
        page.Find("#configuration-transfer-json").Change("not a configuration document");
        page.Find("#configuration-transfer-preview").Click();
        page.FindComponents<ConfigurationTransferReviewPanel>().ShouldBeEmpty();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.AutomationFlows.CountAsync()).ShouldBe(0);
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
    }

    private sealed class EffectsPreviewWindow : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed && command.CommandText.Contains("FROM \"hosts\"", StringComparison.Ordinal))
            {
                Armed = false;
                _ = Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
