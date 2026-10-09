using System.Text.Json;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task FrozenUnsavedPreviewUsesPublicSourceDtoWithoutPresenceOrEventConsumptionAndRejectsAnotherHost()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        await using var runtime = new DeliveryRuntime(fixture, environment);
        var guessing = environment.Registry.Create(new("guessing"), Guid.NewGuid())!;
        var feed = environment.Registry.Create(new("event-feed"), Guid.NewGuid())!;
        var document = Document("PUBLIC LIVE") with { Widgets = [guessing, feed] };
        var created = Value(
            await fixture.Service.CreateAsync(fixture.Owner, new("Preview", document), _ct)
        );
        var published = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                new(created.Overlay.Id, created.Overlay.Revision, "Preview", created.Overlay.Draft),
                _ct
            )
        ).Overlay;
        await environment.PointAsync("retained-event");
        var preview = Value(
            await runtime.Delivery.CreatePreviewAsync(
                fixture.Owner,
                published.Draft with
                {
                    Html = "UNSAVED PRIVATE CANDIDATE",
                },
                FullOverlayDataMode.Sample,
                _ct
            )
        );
        (
            await runtime.Delivery.MayOpenPreviewAsync(preview, Session(fixture.OtherHostId), _ct)
        ).ShouldBeFalse();
        var lease = (await runtime.Delivery.OpenPreviewAsync(preview, fixture.Owner, _ct))!;
        var before = await EventItemsAsync(fixture);
        var frame = (await runtime.Delivery.ProjectAsync(lease, _ct))!;
        frame.Html.ShouldBe("UNSAVED PRIVATE CANDIDATE");
        frame
            .Widgets[0]
            .Content.GetProperty("state")
            .GetProperty("guessCount")
            .GetInt32()
            .ShouldBe(42);
        var serialized = JsonSerializer.Serialize(frame);
        serialized.ShouldContain("guessCount");
        serialized.ShouldNotContain(created.PrivateAccess.AccessKey);
        serialized.ShouldNotContain("author-id");
        runtime.Live.Read(fixture.HostId, published.Id).ActiveConnectionCount.ShouldBe(0);
        (await EventItemsAsync(fixture)).ShouldBe(before);
        (
            await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct)
        )!.Document.Html.ShouldBe("PUBLIC LIVE");
        Value(await fixture.Service.GetAsync(fixture.Owner, published.Id, _ct))
            .Draft.Html.ShouldBe("PUBLIC LIVE");
        runtime.Delivery.Close(lease);
        (await runtime.Delivery.ProjectAsync(lease, _ct)).ShouldBeNull();
    }

    [Test]
    public async Task DraftSaveKeepsLiveConnectionAndSelectionWhileRollbackAbaRevokesOldRenderResources()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        await using var runtime = new DeliveryRuntime(fixture, environment);
        var created = await fixture.CreateOverlayAsync();
        var a = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                Save(created.Overlay, "A"),
                _ct
            )
        ).Overlay;
        var first = (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct))!;
        var lease = runtime.Delivery.OpenPublished(first);
        (await runtime.Delivery.ProjectAsync(lease, _ct))!.Html.ShouldBe("A");
        var saved = Value(
            await fixture.Service.SaveAsync(fixture.Owner, Save(a, "PRIVATE NEWER DRAFT"), _ct)
        );
        var unchanged = (await runtime.Delivery.ProjectAsync(lease, _ct))!;
        unchanged.ConnectionId.ShouldBe(lease.ConnectionId);
        unchanged.Generation.ShouldBe(first.Generation.Value);
        unchanged.Html.ShouldBe("A");
        runtime.Live.Read(fixture.HostId, a.Id).ActiveConnectionCount.ShouldBe(1);
        var b = Value(
            await fixture.Service.SaveAndPublishAsync(fixture.Owner, Save(saved, "B"), _ct)
        ).Overlay;
        _ = Value(
            await fixture.Service.RollbackAsync(
                fixture.Owner,
                new(b.Id, b.Revision, a.PublishedVersion!.Value),
                _ct
            )
        );
        var again = (await fixture.Reader.ResolveAsync(created.PrivateAccess.AccessKey, _ct))!;
        again.Version.ShouldBe(first.Version);
        again.Generation.Value.ShouldBeGreaterThan(first.Generation.Value);
        (await runtime.Delivery.ProjectAsync(lease, _ct)).ShouldBeNull();
        (await runtime.Delivery.MediaAsync(lease.Id, Guid.NewGuid(), 1, _ct)).ShouldBeNull();
        runtime.Delivery.Close(lease);
        runtime.Live.Read(fixture.HostId, a.Id).ActiveConnectionCount.ShouldBe(0);
        var replacement = runtime.Delivery.OpenPublished(again);
        replacement.ConnectionId.ShouldNotBe(lease.ConnectionId);
        (await runtime.Delivery.ProjectAsync(replacement, _ct))!.Html.ShouldBe("A");
        runtime.Delivery.Close(replacement);
    }

    private static async Task<string[]> EventItemsAsync(Fixture fixture)
    {
        await using var db = fixture.Database.CreateDbContext();
        return await db
            .OverlayEventFeedItems.OrderBy(item => item.Id)
            .Select(item => item.SourceKey + ":" + item.Lifecycle)
            .ToArrayAsync();
    }

    private sealed class DeliveryRuntime : IAsyncDisposable
    {
        internal OverlayLiveCoordinator Live { get; }
        internal OverlayCuePlaybackService Cues { get; }
        internal FullOverlayDelivery Delivery { get; }

        internal DeliveryRuntime(Fixture fixture, WidgetEnvironment environment)
        {
            Live = new(
                new(),
                environment.Sources,
                environment.Clock,
                fixture.Events,
                NullLogger<OverlayLiveCoordinator>.Instance
            );
            Cues = new(
                fixture.Database,
                environment.Urls,
                Live,
                Live,
                Options.Create(new BlokeBotOptions()),
                fixture.Events,
                environment.Clock,
                NullLogger<OverlayCuePlaybackService>.Instance
            );
            Delivery = new(
                fixture.Reader,
                fixture.Service,
                new(fixture.Database, fixture.Moderator),
                environment.Registry,
                Cues,
                environment.Media,
                Live,
                environment.FeedChanges,
                environment.Clock,
                NullLogger<FullOverlayDelivery>.Instance
            );
        }

        public async ValueTask DisposeAsync()
        {
            await Delivery.DisposeAsync();
            await Cues.DisposeAsync();
            await Live.DisposeAsync();
        }
    }
}
