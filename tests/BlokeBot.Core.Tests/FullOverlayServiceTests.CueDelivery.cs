using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task RepeatedCueWidgetsShareOneRunAndPrivatePreviewCannotAdvanceProductionQueue()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        await using var runtime = new DeliveryRuntime(fixture, environment);
        var first = environment.Registry.Create(new("cue-player"), Guid.NewGuid())! with
        {
            Audio = new(false, .25),
        };
        var second = first with { Id = new(Guid.NewGuid()), Audio = new(true, .75) };
        var created = Value(
            await fixture.Service.CreateAsync(
                fixture.Owner,
                new("Repeated cues", Document() with { Widgets = [first, second] }),
                _ct
            )
        );
        _ = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                new(
                    created.Overlay.Id,
                    created.Overlay.Revision,
                    "Repeated cues",
                    created.Overlay.Draft
                ),
                _ct
            )
        );
        var selected = (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct))!;
        var cue = await SeedDeliveryCueAsync(fixture);
        (await runtime.Cues.QueryCatalogAsync(fixture.HostId, _ct)).Targets.ShouldContain(target =>
            target.Id == selected.OverlayId
        );
        var request = new OverlayCueAdmissionRequest(
            fixture.HostId,
            selected.OverlayId,
            cue,
            OverlayCueQueuePolicy.Enqueue,
            OverlayCueAdmissionOrigin.Command,
            OverlayCueSafeContext.Empty
        );
        var disconnected = (
            await runtime.Cues.AdmitAsync(request, _ct)
        ).ShouldBeOfType<OverlayCueAdmissionOutcome.Disconnected>();
        var previewId = Value(
            await runtime.Delivery.CreatePreviewAsync(
                fixture.Owner,
                selected.Document with
                {
                    Html = "UNSAVED",
                },
                FullOverlayDataMode.Sample,
                _ct
            )
        );
        var preview = (await runtime.Delivery.OpenPreviewAsync(previewId, fixture.Owner, _ct))!;
        var sample = (await runtime.Delivery.ProjectAsync(preview, _ct))!;
        sample.Widgets[0].Content.GetArrayLength().ShouldBe(1);
        runtime.Cues.ProjectFullPlans(selected).ShouldBeEmpty();
        runtime.Live.Read(fixture.HostId, selected.OverlayId).ActiveConnectionCount.ShouldBe(0);
        (
            await runtime.Delivery.CompleteAsync(
                selected,
                preview.ConnectionId,
                disconnected.RunId,
                _ct
            )
        ).ShouldBeFalse();
        runtime.Delivery.Close(preview);

        var lease = runtime.Delivery.OpenPublished(selected);
        await runtime.Cues.StartAsync(_ct);
        await WaitUntilAsync(() => runtime.Cues.ProjectFullPlans(selected).Length == 1);
        var running = runtime.Cues.ProjectFullPlans(selected).Single();
        running.RunId.ShouldBe(disconnected.RunId);
        var queued = (
            await runtime.Cues.AdmitAsync(request, _ct)
        ).ShouldBeOfType<OverlayCueAdmissionOutcome.Queued>();
        var frame = (await runtime.Delivery.ProjectAsync(lease, _ct))!;
        frame.Widgets[0].Content[0].GetProperty("runId").GetGuid().ShouldBe(running.RunId);
        frame.Widgets[1].Content[0].GetProperty("runId").GetGuid().ShouldBe(running.RunId);
        frame.Widgets[0].Audio.ShouldBe(first.Audio);
        frame.Widgets[1].Audio.ShouldBe(second.Audio);
        runtime.Delivery.Close(lease);
        var replacement = runtime.Delivery.OpenPublished(selected);
        (
            await runtime.Delivery.CompleteAsync(selected, lease.ConnectionId, running.RunId, _ct)
        ).ShouldBeFalse();
        runtime.Cues.ProjectFullPlans(selected).Single().RunId.ShouldBe(running.RunId);
        (
            await runtime.Delivery.CompleteAsync(
                selected,
                replacement.ConnectionId,
                running.RunId,
                _ct
            )
        ).ShouldBeTrue();
        runtime.Cues.ProjectFullPlans(selected).Single().RunId.ShouldBe(queued.RunId);
        _ = (
            await runtime.Cues.CompleteFullAsync(
                fixture.HostId,
                selected.OverlayId,
                selected.Generation,
                running.RunId,
                _ct
            )
        ).ShouldBeOfType<OverlayCueAdmissionOutcome.Missing>();
        runtime.Delivery.Close(replacement);
    }

    [Test]
    public async Task NewSelectionCancelsOldCueRunAndOldCompletionCannotAffectNewRun()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        await using var runtime = new DeliveryRuntime(fixture, environment);
        var widget = environment.Registry.Create(new("cue-player"), Guid.NewGuid())!;
        var created = Value(
            await fixture.Service.CreateAsync(
                fixture.Owner,
                new("Cue fence", Document("A") with { Widgets = [widget] }),
                _ct
            )
        );
        var a = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                new(
                    created.Overlay.Id,
                    created.Overlay.Revision,
                    "Cue fence",
                    created.Overlay.Draft
                ),
                _ct
            )
        ).Overlay;
        var first = (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct))!;
        var oldLease = runtime.Delivery.OpenPublished(first);
        var cue = await SeedDeliveryCueAsync(fixture);
        var request = new OverlayCueAdmissionRequest(
            fixture.HostId,
            first.OverlayId,
            cue,
            OverlayCueQueuePolicy.Enqueue,
            OverlayCueAdmissionOrigin.Command,
            OverlayCueSafeContext.Empty
        );
        var oldRun = (
            await runtime.Cues.AdmitAsync(request, _ct)
        ).ShouldBeOfType<OverlayCueAdmissionOutcome.Running>();
        _ = Value(await fixture.Service.SaveAndPublishAsync(fixture.Owner, Save(a, "B"), _ct));
        var next = (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct))!;
        var newLease = runtime.Delivery.OpenPublished(next);
        var newRun = (
            await runtime.Cues.AdmitAsync(request, _ct)
        ).ShouldBeOfType<OverlayCueAdmissionOutcome.Running>();
        (
            await runtime.Delivery.CompleteAsync(next, oldLease.ConnectionId, oldRun.RunId, _ct)
        ).ShouldBeFalse();
        _ = (
            await runtime.Cues.CompleteFullAsync(
                fixture.HostId,
                first.OverlayId,
                first.Generation,
                oldRun.RunId,
                _ct
            )
        ).ShouldBeOfType<OverlayCueAdmissionOutcome.Missing>();
        runtime.Cues.ProjectFullPlans(next).Single().RunId.ShouldBe(newRun.RunId);
        runtime.Cues.ProjectFullPlans(first).ShouldBeEmpty();
        runtime.Delivery.Close(oldLease);
        runtime.Delivery.Close(newLease);
    }

    private static async Task<Guid> SeedDeliveryCueAsync(Fixture fixture)
    {
        await using var db = fixture.Database.CreateDbContext();
        var cue = new OverlayCue
        {
            PublicId = Guid.NewGuid(),
            HostId = fixture.HostId,
            Name = "Public cue",
            IsEnabled = true,
            DurationMilliseconds = 10000,
            QueuePolicy = OverlayCueQueuePolicy.Enqueue,
            Revision = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            ConfigurationJson =
                """{"schemaVersion":1,"layers":[{"type":"externalWeb","url":"https://example.test/","startOffsetMilliseconds":0,"durationMilliseconds":10000,"zIndex":0,"rectangle":{"xPercent":0,"yPercent":0,"widthPercent":100,"heightPercent":100}}]}""",
        };
        _ = db.Add(cue);
        _ = await db.SaveChangesAsync();
        return cue.PublicId;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }
}
