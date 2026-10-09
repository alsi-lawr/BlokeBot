using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    public void RealManualRun_IsDistinctFromDraftTest_AndUnavailableUntilSavedEnabledAndUnchanged()
    {
        using var ui = new BunitContext();
        var real = 0;
        var draft = 0;
        var header = ui.Render<AutomationEditorHeader>(p =>
            p.Add(c => c.Visible, true)
                .Add(c => c.CanRun, true)
                .Add(c => c.Saved, true)
                .Add(c => c.Enabled, true)
                .Add(c => c.HasNodes, true)
                .Add(c => c.Run, () => real++)
                .Add(c => c.Test, () => draft++)
        );
        var run = header.FindAll("button").Single(b => b.TextContent == "Run saved flow");
        run.Click();
        real.ShouldBe(1);
        draft.ShouldBe(0);
        header.Find("[data-automation-test-flow]").Click();
        draft.ShouldBe(1);
        real.ShouldBe(1);
        header.Render(p => p.Add(c => c.HasChanges, true));
        header
            .FindAll("button")
            .Single(b => b.TextContent == "Run saved flow")
            .HasAttribute("disabled")
            .ShouldBeTrue();
    }

    [Test]
    public void NamedZoneConfiguration_ShowsConcreteGapSkipWarning_WithoutMovingTheLocalTime()
    {
        using var ui = new BunitContext();
        var definition = new ExpandedAutomationCatalogModule()
            .Definitions.Single(d => d.Descriptor.Id == AutomationDefinitionIds.ScheduledTimeSource)
            .Descriptor;
        var node = AutomationEditorNode.Create(definition, default);
        node.SetValue(new("zone"), "Europe/London");
        node.SetValue(new("local-time"), "2026-03-29T01:30:00");
        var inspector = ui.Render<AutomationNodeInspector>(p =>
            p.Add(c => c.Node, node).Add(c => c.Nodes, [node]).Add(c => c.Edges, [])
        );
        inspector
            .Find("[data-automation-schedule-skipped-time]")
            .TextContent.ShouldContain("2026-03-29T01:30:00 does not exist in Europe/London");
        node.Value(new("local-time")).ShouldBe("2026-03-29T01:30:00");
    }
}
