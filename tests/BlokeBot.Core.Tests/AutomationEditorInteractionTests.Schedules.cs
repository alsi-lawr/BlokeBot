using System.Data.Common;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    public async Task ScheduleEditor_HeldHistoryCannotPopulateAnotherSelectedSavedSource()
    {
        var editor = AutomationEditorState.Create("Scoped schedules");
        var definition = new ExpandedAutomationCatalogModule()
            .Definitions.Single(value =>
                value.Descriptor.Id == AutomationDefinitionIds.ScheduledTimeSource
            )
            .Descriptor;
        var first = editor.AddNode(definition);
        var second = editor.AddNode(definition);
        var gate = new ScheduleReadGate();
        await using var f = await AutomationEditorPageFixture.CreateAsync(
            authored: editor,
            interceptors: [gate]
        );
        var page = f.Page;
        var observed = DateTimeOffset.UtcNow;
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var host = new AutomationHostId(await db.Hosts.Select(host => host.Id).SingleAsync());
            var flow = new AutomationFlowId(
                await db.AutomationFlows.Select(flow => flow.Id).SingleAsync()
            );
            var now = observed;
            await AutomationScheduleSkipStore.AppendAsync(
                db,
                host,
                flow,
                first.Id,
                AutomationScheduleSkipReason.Late,
                now.AddSeconds(-4),
                now,
                default
            );
            await AutomationScheduleSkipStore.AppendAsync(
                db,
                host,
                flow,
                second.Id,
                AutomationScheduleSkipReason.Offline,
                now.AddSeconds(-2),
                now,
                default
            );
        }
        gate.Armed = true;
        var opening = DiscloseAsync(page, first.Id);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await DiscloseAsync(page, second.Id);
        }
        finally
        {
            _ = gate.Release.TrySetResult();
        }
        await opening;
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationNodeInspector>().Instance.Node!.Id.ShouldBe(second.Id)
        );
        page.WaitForAssertion(() =>
        {
            var content = page.Find(".automation-binding-card").TextContent;
            content.ShouldContain(
                observed
                    .AddSeconds(-2)
                    .UtcDateTime.ToString(
                        "HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture
                    )
            );
            content.ShouldNotContain(
                observed
                    .AddSeconds(-4)
                    .UtcDateTime.ToString(
                        "HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture
                    )
            );
        });
    }

    private sealed class ScheduleReadGate : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Armed
                && command.CommandText.Contains(
                    "automation_schedule_skips",
                    StringComparison.Ordinal
                )
            )
            {
                Armed = false;
                _ = Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    [Test]
    public async Task ScheduleEditor_WeeklyClockKindChangesHistoryAndReopenRetainHiddenLegacyValues()
    {
        var editor = AutomationEditorState.Create("Schedule roundtrip");
        var source = editor.AddNode(
            new ExpandedAutomationCatalogModule()
                .Definitions.Single(definition =>
                    definition.Descriptor.Id == AutomationDefinitionIds.ScheduledTimeSource
                )
                .Descriptor
        );
        source.SetValue(new("kind"), "Interval");
        source.SetValue(new("zone"), "Europe/London");
        source.SetValue(new("local-time"), "2027-08-17T18:31:42");
        source.SetValue(new("day"), "Friday");
        source.SetValue(new("interval-seconds"), "37");
        await using var f = await AutomationEditorPageFixture.CreateAsync(authored: editor);
        var page = f.Page;
        await DiscloseAsync(page, source.Id);
        page.Find("select[id$='-kind']").Change("Weekly");
        page.Find("input[id$='-local-time']").Input("19:12:34");
        page.Find("select[id$='-kind']").Change("Once");
        page.Find("input[id$='-local-time']").GetAttribute("value").ShouldBe("2027-08-17T19:12:34");
        page.Find("select[id$='-kind']").Change("Interval");
        page.Find("input[id$='-interval-seconds']").GetAttribute("value").ShouldBe("37");
        page.Find("[data-automation-save-flow]").Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationEditorHeader>().Instance.HasChanges.ShouldBeFalse()
        );
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var saved = await db.AutomationFlowNodes.SingleAsync();
            using var json = JsonDocument.Parse(saved.ConfigurationJson);
            json.RootElement.GetProperty("local-time").GetString().ShouldBe("2027-08-17T19:12:34");
            json.RootElement.GetProperty("day").GetString().ShouldBe("Friday");
            json.RootElement.GetProperty("interval-seconds").GetInt32().ShouldBe(37);
            var now = DateTimeOffset.UtcNow;
            await AutomationScheduleSkipStore.AppendAsync(
                db,
                new(await db.Hosts.Select(host => host.Id).SingleAsync()),
                new(saved.FlowId),
                source.Id,
                AutomationScheduleSkipReason.Late,
                now.AddSeconds(-3),
                now,
                default
            );
        }
        var rail = page.FindComponent<AutomationFlowRail>();
        await page.InvokeAsync(() =>
            rail.Instance.Select.InvokeAsync(rail.Instance.Flows.Single())
        );
        await DiscloseAsync(page, source.Id);
        page.Find("select[id$='-kind']").Change("Weekly");
        page.Find("select[id$='-day']").GetAttribute("value").ShouldBe("Friday");
        page.Find("input[id$='-local-time']").GetAttribute("value").ShouldBe("19:12:34");
        page.WaitForAssertion(() =>
            page.FindAll(".automation-binding-card")
                .ShouldContain(card => card.TextContent.Contains("UTC"))
        );
        // An unsaved source must not inherit the selected saved source's facts.
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationWorkspaceToolbar>().Instance.ToggleToolbox.InvokeAsync()
        );
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationToolbox>().Instance.Add.InvokeAsync(source.Definition)
        );
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationNodeInspector>().Instance.Node!.Id.ShouldNotBe(source.Id)
        );
        page.FindAll(".automation-binding-card")
            .ShouldNotContain(card => card.TextContent.Contains("UTC"));
    }
}
