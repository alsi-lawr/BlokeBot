using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Core.Features.Plugins;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task LatePluginProjectionAfterDraftSaveIsDiscardedWithoutClosingLiveConnection()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var plugin = new WidgetPluginRig(fixture.HostId);
        await using var environment = new WidgetEnvironment(fixture, plugin);
        await using var runtime = new DeliveryRuntime(fixture, environment);
        var widget = environment.Registry.Create(
            new($"plugin:{plugin.Manifest.Manifest.Id.Value}/display"),
            Guid.NewGuid()
        )!;
        var created = Value(
            await fixture.Service.CreateAsync(
                fixture.Owner,
                new("Plugin lifetime", Document("SELECTED") with { Widgets = [widget] }),
                _ct
            )
        );
        var published = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                new(
                    created.Overlay.Id,
                    created.Overlay.Revision,
                    "Plugin lifetime",
                    created.Overlay.Draft
                ),
                _ct
            )
        ).Overlay;
        var selected = (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct))!;
        var lease = runtime.Delivery.OpenPublished(selected);
        plugin.Worker.Pause = true;
        var pending = runtime.Delivery.ProjectAsync(lease, _ct);
        await plugin.Worker.Started.Task;
        _ = Value(
            await fixture.Service.SaveAsync(fixture.Owner, Save(published, "PRIVATE DRAFT"), _ct)
        );
        _ = plugin.Worker.Release.TrySetResult();
        (await pending).ShouldBeNull();
        runtime.Live.Read(fixture.HostId, selected.OverlayId).ActiveConnectionCount.ShouldBe(1);
        var retried = (await runtime.Delivery.ProjectAsync(lease, _ct))!;
        retried.Html.ShouldBe("SELECTED");
        retried.ConnectionId.ShouldBe(lease.ConnectionId);
        retried.Widgets.Single().Kind.ShouldBe("plugin");
        _ = (
            await runtime.Delivery.PluginAssetAsync(
                lease.Id,
                widget.Id.Value,
                lease.DeclaredPlugins[widget.Id.Value].Id,
                "web/index.html",
                _ct
            )
        ).ShouldBeOfType<PluginAssetContentResolution.Available>();
        _ = (
            await runtime.Delivery.PluginAssetAsync(
                lease.Id,
                widget.Id.Value,
                lease.DeclaredPlugins[widget.Id.Value].Id,
                "private.txt",
                _ct
            )
        ).ShouldBeOfType<PluginAssetContentResolution.NotFound>();
        var oldResource = lease.DeclaredPlugins[widget.Id.Value].Id;
        plugin.Disable();
        _ = (
            await runtime.Delivery.PluginAssetAsync(
                lease.Id,
                widget.Id.Value,
                oldResource,
                "web/index.html",
                _ct
            )
        ).ShouldBeOfType<PluginAssetContentResolution.NotFound>();
        plugin.Enable();
        (await runtime.Delivery.ProjectAsync(lease, _ct))!.Widgets.Single().Kind.ShouldBe("plugin");
        lease.DeclaredPlugins[widget.Id.Value].Id.ShouldNotBe(oldResource);
        _ = (
            await runtime.Delivery.PluginAssetAsync(
                lease.Id,
                widget.Id.Value,
                oldResource,
                "web/index.html",
                _ct
            )
        ).ShouldBeOfType<PluginAssetContentResolution.NotFound>();
        runtime.Delivery.Close(lease);
        _ = (
            await runtime.Delivery.PluginAssetAsync(
                lease.Id,
                widget.Id.Value,
                lease.DeclaredPlugins[widget.Id.Value].Id,
                "web/index.html",
                _ct
            )
        ).ShouldBeOfType<PluginAssetContentResolution.NotFound>();
    }

    [Test]
    public async Task ClosingRenderCancelsPendingDependencyAndReleasesItsProductionPresence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var plugin = new WidgetPluginRig(fixture.HostId);
        await using var environment = new WidgetEnvironment(fixture, plugin);
        await using var runtime = new DeliveryRuntime(fixture, environment);
        var widget = environment.Registry.Create(
            new($"plugin:{plugin.Manifest.Manifest.Id.Value}/display"),
            Guid.NewGuid()
        )!;
        var created = Value(
            await fixture.Service.CreateAsync(
                fixture.Owner,
                new("Cancelled render", Document() with { Widgets = [widget] }),
                _ct
            )
        );
        _ = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                new(
                    created.Overlay.Id,
                    created.Overlay.Revision,
                    "Cancelled render",
                    created.Overlay.Draft
                ),
                _ct
            )
        );
        var selected = (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct))!;
        var lease = runtime.Delivery.OpenPublished(selected);
        plugin.Worker.Pause = true;
        var pending = runtime.Delivery.ProjectAsync(lease, _ct);
        await plugin.Worker.Started.Task;
        runtime.Delivery.Close(lease);
        _ = await Should.ThrowAsync<OperationCanceledException>(async () => await pending);
        runtime.Live.Read(fixture.HostId, selected.OverlayId).ActiveConnectionCount.ShouldBe(0);
        (await runtime.Delivery.ProjectAsync(lease, _ct)).ShouldBeNull();
    }

    [Test]
    public async Task PrivatePreviewHeartbeatKeepsFrozenCandidateReconnectableThenIdleReclaimsResources()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        await using var runtime = new DeliveryRuntime(fixture, environment);
        var created = await fixture.CreateOverlayAsync();
        var id = Value(
            await runtime.Delivery.CreatePreviewAsync(
                fixture.Owner,
                created.Overlay.Draft,
                FullOverlayDataMode.Sample,
                _ct
            )
        );
        var lease = (await runtime.Delivery.OpenPreviewAsync(id, fixture.Owner, _ct))!;
        environment.Clock.Advance(TimeSpan.FromSeconds(100));
        (await runtime.Delivery.RefreshAuthorityAsync(lease, _ct)).ShouldBeTrue();
        environment.Clock.Advance(TimeSpan.FromSeconds(100));
        (await runtime.Delivery.MayOpenPreviewAsync(id, fixture.Owner, _ct)).ShouldBeTrue();
        var reconnect = (await runtime.Delivery.OpenPreviewAsync(id, fixture.Owner, _ct))!;
        runtime.Delivery.Close(lease);
        environment.Clock.Advance(TimeSpan.FromSeconds(121));
        (await runtime.Delivery.ProjectAsync(reconnect, _ct)).ShouldBeNull();
        (await runtime.Delivery.MayOpenPreviewAsync(id, fixture.Owner, _ct)).ShouldBeFalse();
        runtime.Delivery.Close(reconnect);
    }
}
