using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Core.Features.Plugins;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task PluginWidgetAndAssets_RejectForgedDocumentAndLateWorkAfterDraftRevisionWithoutChangingSelectedContent()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var plugin = new WidgetPluginRig(fixture.HostId);
        await using var environment = new WidgetEnvironment(fixture, plugin);
        var widget = environment.Registry.Create(
            new($"plugin:{plugin.Manifest.Manifest.Id.Value}/display"),
            Guid.NewGuid()
        )!;
        var created = Value(
            await fixture.Service.CreateAsync(
                fixture.Owner,
                new("Fence", Document() with { Widgets = [widget] }),
                _ct
            )
        );
        var published = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                new(
                    created.Overlay.Id,
                    created.Overlay.Revision,
                    created.Overlay.Name,
                    created.Overlay.Draft
                ),
                _ct
            )
        ).Overlay;
        var selected = (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct))!;
        var context = new FullOverlayRenderContext(
            selected.HostId,
            selected.OverlayId,
            selected.Version,
            selected.Revision,
            selected.Document,
            FullOverlayDataMode.Live,
            []
        );
        _ = (
            await environment.Registry.ResolvePluginAssetAsync(
                context with
                {
                    Document = context.Document with { Id = Guid.NewGuid() },
                },
                widget.Id,
                "web/index.html",
                _ct
            )
        ).ShouldBeOfType<PluginAssetContentResolution.NotFound>();
        _ = (
            await environment.Registry.ResolvePluginAssetAsync(
                context with
                {
                    SelectionRevision = null,
                },
                widget.Id,
                "web/index.html",
                _ct
            )
        ).ShouldBeOfType<PluginAssetContentResolution.NotFound>();
        plugin.Worker.Pause = true;
        var pending = environment.Registry.ProjectAsync(context, _ct);
        await plugin.Worker.Started.Task;
        var saved = Value(
            await fixture.Service.SaveAsync(
                fixture.Owner,
                new(
                    published.Id,
                    published.Revision,
                    published.Name,
                    published.Draft with
                    {
                        Html = "PRIVATE DRAFT",
                    }
                ),
                _ct
            )
        );
        _ = plugin.Worker.Release.TrySetResult();
        _ = (await pending)[0].Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>();
        _ = (
            await environment.Registry.ResolvePluginAssetAsync(
                context,
                widget.Id,
                "web/index.html",
                _ct
            )
        ).ShouldBeOfType<PluginAssetContentResolution.NotFound>();
        var current = (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct))!;
        current.Version.ShouldBe(selected.Version);
        current.Document.Html.ShouldBe(selected.Document.Html);
        current.Document.Html.ShouldNotContain("PRIVATE DRAFT");
        current.Revision.ShouldBe(saved.Revision);
        plugin.Worker.Pause = false;
        _ = (
            await environment.Registry.ProjectAsync(
                context with
                {
                    SelectionRevision = current.Revision,
                },
                _ct
            )
        )[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Plugin>();
    }
}
