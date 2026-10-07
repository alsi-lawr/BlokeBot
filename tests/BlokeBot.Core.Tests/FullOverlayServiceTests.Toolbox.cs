using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Plugins.Features;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task Toolbox_SearchKeepsOrdinaryAndIsolatedHtmlDistinct_AndUnreadyDeclarationsInsertable()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var plugin = new WidgetPluginRig(fixture.HostId);
        await using var environment = new WidgetEnvironment(fixture, plugin);
        plugin.Disable();
        using var context = new BunitContext();
        _ = context.Services.AddSingleton(environment.Registry);
        _ = context.Services.AddSingleton(
            new PluginWidgetCatalog(plugin.Declarations, plugin.Features, plugin.Runtime)
        );
        FullOverlayWidgetKind? added = null;
        var ordinary = 0;
        var toolbox = context.Render<FullOverlayToolbox>(parameters =>
            parameters
                .Add(component => component.HostId, fixture.HostId)
                .Add(component => component.Add, kind => added = kind)
                .Add(component => component.AddOrdinary, () => ordinary++)
        );

        toolbox.Find("input").Input("HTML");
        var rows = toolbox.FindAll(".editor-toolbox-row");
        rows.Single(row => row.TextContent.Contains("Text / HTML element")).Click();
        ordinary.ShouldBe(1);
        rows.Single(row => row.TextContent.Contains("Isolated HTML")).Click();
        added!.Value.Value.ShouldBe("html");

        toolbox.Find("input").Input("Public display");
        var declaration = toolbox.Find(".editor-toolbox-row");
        plugin.Worker.Identity.ShouldBeNull();
        declaration.Click();
        added!.Value.Value.ShouldBe($"plugin:{plugin.Manifest.Manifest.Id.Value}/display");
        _ = environment.Registry.Create(added!.Value, Guid.NewGuid()).ShouldNotBeNull();
        plugin.Worker.Identity.ShouldBeNull();
    }
}
