using System.Text.Json;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task EventFeed_ExistingHostedSchedulerAdvancesFullQueueWithoutBrowserReadOrNewAdmission()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        var widget = environment.Registry.Create(new("event-feed"), Guid.NewGuid())!;
        var created = Value(
            await fixture.Service.CreateAsync(
                fixture.Owner,
                new("Timed feed", Document() with { Widgets = [widget] }),
                _ct
            )
        ).Overlay;
        _ = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                new(created.Id, created.Revision, created.Name, created.Draft),
                _ct
            )
        );
        FullOverlayEventFeeds.TryConfiguration(widget, out var configuration).ShouldBeTrue();
        var identity = new FullOverlayEventFeedIdentity(
            fixture.HostId,
            created.Id,
            configuration.BindingId
        );
        var documentChanges = 0;
        using var documentSubscription = fixture.Events.Subscribe(
            AppEventKind.OverlaysChanged,
            ObserverIdentity.Named("simple-connection-invalidation-proof"),
            (_, _) =>
            {
                _ = Interlocked.Increment(ref documentChanges);
                return ValueTask.CompletedTask;
            }
        );
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (
            environment.FeedChanges.Subscribe(
                identity,
                ObserverIdentity.Named("full-feed-binding-readiness"),
                (_, _) =>
                {
                    _ = ready.TrySetResult();
                    return ValueTask.CompletedTask;
                }
            )
        )
        {
            await environment.Events.StartAsync(_ct);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        var state = await environment.Events.ProjectFullAsync(
            fixture.HostId,
            created.Id,
            configuration.BindingId,
            _ct
        );
        _ = state.ShouldNotBeNull();
        state.Active.ShouldBeNull();
        await environment.PointAsync("first");
        await environment.PointAsync("second");
        var advanced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = environment.FeedChanges.Subscribe(
            identity,
            ObserverIdentity.Named("full-feed-scheduler-proof"),
            (_, _) =>
            {
                _ = advanced.TrySetResult();
                return ValueTask.CompletedTask;
            }
        );
        environment.Clock.Advance(TimeSpan.FromSeconds(40));
        await advanced.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await environment.Events.StopAsync(_ct);
        documentChanges.ShouldBe(0);
        await using var db = fixture.Database.CreateDbContext();
        (
            await db.OverlayEventFeedItems.SingleAsync(item => item.SourceKey == "first")
        ).Lifecycle.ShouldBe(OverlayEventFeedLifecycle.Consumed);
        (
            await db.OverlayEventFeedItems.SingleAsync(item => item.SourceKey == "second")
        ).Lifecycle.ShouldBe(OverlayEventFeedLifecycle.Active);
        (await db.FullOverlayEventFeedBindings.CountAsync()).ShouldBe(1);
        (await db.OverlayInstances.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task EventFeed_SharedAdmissionConflictPreservesBindingsAndExplicitSeparateBindingsOwnSeparateQueues()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        var widget = environment.Registry.Create(new("event-feed"), Guid.NewGuid())!;
        FullOverlayEventFeeds.TryConfiguration(widget, out var configuration).ShouldBeTrue();
        FullOverlayWidget Configure(Guid bindingId, int capacity) =>
            widget with
            {
                Id = new(Guid.NewGuid()),
                Configuration = JsonSerializer.SerializeToElement(
                    new
                    {
                        bindingId,
                        feed = Config(
                            new OverlayConfiguration.EventFeedV1(
                                capacity,
                                configuration.Feed.OverflowPolicy,
                                configuration.Feed.Kinds
                            )
                        ),
                    }
                ),
            };
        var conflicting = Configure(configuration.BindingId, 3);
        var created = Value(
            await fixture.Service.CreateAsync(
                fixture.Owner,
                new("Conflict", Document() with { Widgets = [widget, conflicting] }),
                _ct
            )
        ).Overlay;
        var live = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                new(created.Id, created.Revision, created.Name, created.Draft),
                _ct
            )
        ).Overlay;
        await environment.PointAsync("conflicted");
        var context = new FullOverlayRenderContext(
            fixture.HostId,
            live.Id,
            live.PublishedVersion,
            live.Revision,
            live.Draft,
            FullOverlayDataMode.Live,
            []
        );
        var invalid = await environment.Registry.ProjectAsync(context, _ct);
        invalid.ShouldAllBe(projection => projection.Output is FullOverlayWidgetOutput.Unavailable);
        invalid[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>()
            .Diagnostic.Code.ShouldBe("conflicting-feed-configuration");
        await using (var db = fixture.Database.CreateDbContext())
        {
            (await db.FullOverlayEventFeedBindings.CountAsync()).ShouldBe(0);
            (await db.OverlayEventFeedItems.CountAsync()).ShouldBe(0);
        }
        var separated = Configure(Guid.NewGuid(), 3);
        var published = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                new(
                    live.Id,
                    live.Revision,
                    live.Name,
                    live.Draft with
                    {
                        Widgets = [widget, separated],
                    }
                ),
                _ct
            )
        ).Overlay;
        await environment.PointAsync("same-source");
        var outputs = await environment.Registry.ProjectAsync(
            context with
            {
                Version = published.PublishedVersion,
                SelectionRevision = published.Revision,
                Document = published.Draft,
            },
            _ct
        );
        _ = Feed(outputs[0]).Active.ShouldNotBeNull();
        _ = Feed(outputs[1]).Active.ShouldNotBeNull();
        Feed(outputs[0]).Active!.Id.ShouldNotBe(Feed(outputs[1]).Active!.Id);
        await using var final = fixture.Database.CreateDbContext();
        (await final.FullOverlayEventFeedBindings.CountAsync()).ShouldBe(2);
        (await final.OverlayEventFeedItems.CountAsync()).ShouldBe(2);
        (await final.OverlayInstances.CountAsync()).ShouldBe(0);
        published
            .Draft.Widgets[1]
            .Configuration.GetProperty("bindingId")
            .GetGuid()
            .ShouldBe(separated.Configuration.GetProperty("bindingId").GetGuid());
    }
}
