using BlokeBot.Core.Features.Overlays.Full;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task EditorPreviewReplacementRevokesOnlyOwnedCandidateAndCancelsPendingWork()
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
                new("Editor preview", Document() with { Widgets = [widget] }),
                _ct
            )
        );
        var id = Value(
            await runtime.Delivery.CreatePreviewAsync(
                fixture.Owner,
                created.Overlay.Draft,
                FullOverlayDataMode.Sample,
                _ct
            )
        );
        var newer = Value(
            await runtime.Delivery.CreatePreviewAsync(
                fixture.Owner,
                created.Overlay.Draft with
                {
                    Html = "NEW UNSAVED",
                },
                FullOverlayDataMode.Live,
                _ct
            )
        );
        var lease = (await runtime.Delivery.OpenPreviewAsync(id, fixture.Owner, _ct))!;
        plugin.Worker.Pause = true;
        var pending = runtime.Delivery.ProjectAsync(lease, _ct);
        await plugin.Worker.Started.Task;
        await runtime.Delivery.ReleasePreviewAsync(Session(fixture.OtherHostId), id, _ct);
        (await runtime.Delivery.MayOpenPreviewAsync(id, fixture.Owner, _ct)).ShouldBeTrue();
        await runtime.Delivery.ReleasePreviewAsync(fixture.Owner, id, _ct);
        _ = await Should.ThrowAsync<OperationCanceledException>(async () => await pending);
        (await runtime.Delivery.OpenPreviewAsync(id, fixture.Owner, _ct)).ShouldBeNull();
        (await runtime.Delivery.ProjectAsync(lease, _ct)).ShouldBeNull();
        (await runtime.Delivery.MayOpenPreviewAsync(newer, fixture.Owner, _ct)).ShouldBeTrue();
        runtime.Live.Read(fixture.HostId, created.Overlay.Id).ActiveConnectionCount.ShouldBe(0);
        FullOverlayDocuments
            .Serialize(
                Value(await fixture.Service.GetAsync(fixture.Owner, created.Overlay.Id, _ct)).Draft
            )
            .ShouldBe(FullOverlayDocuments.Serialize(created.Overlay.Draft));
        await runtime.Delivery.ReleasePreviewAsync(fixture.Owner, newer, _ct);
    }
}
