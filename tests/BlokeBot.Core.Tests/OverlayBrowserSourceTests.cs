using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Hosting;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class OverlayBrowserSourceTests
{
    [Test]
    public async Task EmptyV1Projection_IsTypedCompleteAndPresentationSafe()
    {
        var time = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 30, 14, 15, 0, TimeSpan.Zero)
        );
        var epoch = new OverlayServerEpoch();
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        IOverlayStateProvider provider = new OverlayStateProvider(database, epoch, time);
        var instance = new ResolvedOverlayInstance(
            91,
            Guid.NewGuid(),
            OverlayType.Empty,
            new OverlayConfiguration.EmptyV1(),
            new OverlayRevision(12)
        );

        var projected = await provider.ProjectAsync(instance, CancellationToken.None);

        var snapshot = projected.ShouldBeOfType<OverlaySnapshotProjection.EmptyV1>().Snapshot;
        snapshot.OverlayType.ShouldBe("empty");
        snapshot.SchemaVersion.ShouldBe(1);
        snapshot.ServerEpoch.ShouldBe(epoch.Value);
        snapshot.Sequence.ShouldBe(12);
        snapshot.GeneratedAtUtc.ShouldBe(time.GetUtcNow());
        _ = snapshot.State.ShouldBeOfType<EmptyV1OverlayPresentationState>();
        var json = JsonSerializer.Serialize(
            snapshot,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
        json.ShouldContain("\"state\":{}");
        using var document = JsonDocument.Parse(json);
        document
            .RootElement.EnumerateObject()
            .Select(static property => property.Name)
            .ShouldBe([
                "overlayType",
                "schemaVersion",
                "serverEpoch",
                "sequence",
                "generatedAtUtc",
                "state",
            ]);
        json.ShouldNotContain(instance.OverlayId.ToString(), Case.Insensitive);
        json.ShouldNotContain("configuration", Case.Insensitive);
        json.ShouldNotContain("access", Case.Insensitive);
    }

    [Test]
    public async Task PublicEndpoints_ReturnCurrentStateWithoutAuthenticationOrCallerAuthority()
    {
        await using var host = await BrowserSourceHost.StartAsync();
        var seed = await host.SeedAsync("owner", revision: 3);
        var other = await host.SeedAsync("other", revision: 41);

        using var documentResponse = await host.Client.GetAsync($"/overlay/{seed.AccessKey}");
        var document = await documentResponse.Content.ReadAsStringAsync();
        using var stateResponse = await host.Client.GetAsync(
            $"/overlay/{seed.AccessKey}/state?hostId={other.HostId}&overlayId={other.OverlayId}"
        );
        var state = await stateResponse.Content.ReadAsStringAsync();

        documentResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        documentResponse.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        document.ShouldContain("id=\"overlay-canvas\"");
        document.ShouldContain($"data-state-url=\"/overlay/{seed.AccessKey}/state\"");
        document.ShouldNotContain("dashboard", Case.Insensitive);
        document.ShouldNotContain("navigation", Case.Insensitive);
        stateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        stateResponse.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        using var parsed = JsonDocument.Parse(state);
        var root = parsed.RootElement;
        root.EnumerateObject()
            .Select(static property => property.Name)
            .ShouldBe([
                "overlayType",
                "schemaVersion",
                "serverEpoch",
                "sequence",
                "generatedAtUtc",
                "state",
            ]);
        root.GetProperty("overlayType").GetString().ShouldBe("empty");
        root.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        root.GetProperty("sequence").GetInt64().ShouldBe(3);
        root.GetProperty("serverEpoch").GetGuid().ShouldNotBe(Guid.Empty);
        root.GetProperty("generatedAtUtc").GetDateTimeOffset().ShouldBe(host.Time.GetUtcNow());
        root.GetProperty("state").EnumerateObject().ShouldBeEmpty();
        state.ShouldNotContain(seed.OverlayId.ToString(), Case.Insensitive);
        state.ShouldNotContain(other.OverlayId.ToString(), Case.Insensitive);
        state.ShouldNotContain(seed.AccessKey);
    }

    [Test]
    public async Task Refresh_ProjectsAuthoritativeCurrentRevisionWithStableServerEpoch()
    {
        await using var host = await BrowserSourceHost.StartAsync();
        var seed = await host.SeedAsync("refresh", revision: 1);

        var first = await host.GetStateAsync(seed.AccessKey);
        host.Time.Advance(TimeSpan.FromMinutes(2));
        await host.SetRevisionAsync(seed.OverlayId, 7);
        var refreshed = await host.GetStateAsync(seed.AccessKey);

        first.GetProperty("sequence").GetInt64().ShouldBe(1);
        refreshed.GetProperty("sequence").GetInt64().ShouldBe(7);
        refreshed
            .GetProperty("serverEpoch")
            .GetGuid()
            .ShouldBe(first.GetProperty("serverEpoch").GetGuid());
        refreshed
            .GetProperty("generatedAtUtc")
            .GetDateTimeOffset()
            .ShouldBe(first.GetProperty("generatedAtUtc").GetDateTimeOffset().AddMinutes(2));
    }

    [Test]
    public async Task ConnectedConfigurationChange_ResyncsTheVersionedAppearanceStylesheet()
    {
        await using var host = await BrowserSourceHost.StartAsync();
        var seed = await host.SeedGuessingAsync("appearance-resync");
        await using var stream = await host.OpenLiveAsync(seed.AccessKey);
        _ = await stream.ReadEnvelopeAsync();

        using var documentResponse = await host.Client.GetAsync($"/overlay/{seed.AccessKey}");
        var document = await documentResponse.Content.ReadAsStringAsync();
        document.ShouldContain(
            $"id=\"overlay-appearance-style\" rel=\"stylesheet\" href=\"/overlay/{seed.AccessKey}/appearance.css\""
        );
        using var initialStylesheet = await host.Client.GetAsync(
            $"/overlay/{seed.AccessKey}/appearance.css"
        );
        var initialCss = await initialStylesheet.Content.ReadAsStringAsync();
        initialCss.ShouldNotContain("#123456");

        await host.SetConfigurationAsync(
            seed.OverlayId,
            new OverlayConfiguration.GuessingV1(
                true,
                8,
                new OverlayAppearance(160, 690, 1600, 270, ".card { fill: #123456; }")
            ),
            revision: 2
        );
        _ = await host.Events.PublishAsync(AppEventKind.OverlaysChanged, CancellationToken.None);

        var invalidation = await stream.ReadEnvelopeAsync();
        invalidation.GetProperty("eventType").GetString().ShouldBe("reauthenticate");
        var refreshedState = await host.GetStateAsync(seed.AccessKey);
        var refreshedRevision = refreshedState.GetProperty("sequence").GetInt64();
        refreshedRevision.ShouldBe(2);
        using var refreshedStylesheet = await host.Client.GetAsync(
            $"/overlay/{seed.AccessKey}/appearance.css?revision={refreshedRevision}"
        );
        var refreshedCss = await refreshedStylesheet.Content.ReadAsStringAsync();
        refreshedCss.ShouldContain("#overlay-root .card");
        refreshedCss.ShouldContain("#123456");
        refreshedStylesheet.Headers.CacheControl?.NoStore.ShouldBeTrue();
    }

    [Test]
    public async Task InvalidDisabledRotatedAndDeletedKeys_ReturnUniformNonSensitiveFailure()
    {
        await using var host = await BrowserSourceHost.StartAsync();
        var disabled = await host.SeedAsync("disabled", enabled: false);
        var rotated = await host.SeedAsync("rotated");
        var replacementKey = BrowserSourceHost.AccessKey('z');
        await host.SetAccessKeyAsync(rotated.OverlayId, replacementKey);
        var deleted = BrowserSourceHost.AccessKey('d');
        var invalid = BrowserSourceHost.AccessKey('i');

        var paths = new[]
        {
            $"/overlay/{disabled.AccessKey}",
            $"/overlay/{rotated.AccessKey}",
            $"/overlay/{deleted}",
            $"/overlay/{invalid}",
        };
        var failures = new List<(HttpStatusCode Status, string Body)>();
        foreach (var path in paths)
        {
            using var response = await host.Client.GetAsync(path);
            failures.Add((response.StatusCode, await response.Content.ReadAsStringAsync()));
        }

        failures.Select(value => value.Status).Distinct().ShouldBe([HttpStatusCode.NotFound]);
        failures.Select(value => value.Body).Distinct().ShouldBe(["Overlay unavailable."]);
        foreach (var key in new[] { disabled.AccessKey, rotated.AccessKey, deleted, invalid })
        {
            failures.ShouldAllBe(failure => !failure.Body.Contains(key, StringComparison.Ordinal));
        }

        using var stateFailure = await host.Client.GetAsync($"/overlay/{invalid}/state");
        stateFailure.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stateFailure.Content.ReadAsStringAsync()).ShouldBe("Overlay unavailable.");
        using var liveFailure = await host.GetLiveResponseAsync(disabled.AccessKey);
        liveFailure.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await liveFailure.Content.ReadAsStringAsync()).ShouldBe("Overlay unavailable.");
        host.ObservedCompletedPaths.ShouldContain("/overlay/[redacted]");
        host.ObservedCompletedPaths.ShouldContain("/overlay/[redacted]/state");
        host.ObservedCompletedPaths.ShouldNotContain(path => path.Contains(invalid));
    }

    [Test]
    public async Task PathBase_ProducesPrefixedStateAndBundledAssetUrls()
    {
        await using var host = await BrowserSourceHost.StartAsync("/blokebot");
        var seed = await host.SeedAsync("path-base");

        using var documentResponse = await host.Client.GetAsync(
            $"/blokebot/overlay/{seed.AccessKey}"
        );
        var document = await documentResponse.Content.ReadAsStringAsync();
        using var stylesheet = await host.Client.GetAsync(
            "/blokebot/overlay/assets/blokebot-overlay.css"
        );
        using var script = await host.Client.GetAsync(
            "/blokebot/overlay/assets/blokebot-overlay.js"
        );
        using var state = await host.Client.GetAsync($"/blokebot/overlay/{seed.AccessKey}/state");

        documentResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        document.ShouldContain("href=\"/blokebot/overlay/assets/blokebot-overlay.css\"");
        document.ShouldContain("src=\"/blokebot/overlay/assets/blokebot-overlay.js\"");
        document.ShouldContain($"data-state-url=\"/blokebot/overlay/{seed.AccessKey}/state\"");
        document.ShouldContain($"data-live-url=\"/blokebot/overlay/{seed.AccessKey}/events\"");
        stylesheet.StatusCode.ShouldBe(HttpStatusCode.OK);
        stylesheet.Content.Headers.ContentType?.MediaType.ShouldBe("text/css");
        script.StatusCode.ShouldBe(HttpStatusCode.OK);
        script.Content.Headers.ContentType?.MediaType.ShouldBe("text/javascript");
        state.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task UploadedMedia_PrivateRouteSupportsRangesCacheAndHostIsolation()
    {
        await using var host = await BrowserSourceHost.StartAsync();
        var owner = await host.SeedCuePlayerAsync("media-owner");
        var other = await host.SeedAsync("media-other");
        var asset = await host.UploadMp4Async(owner, "Range sample");
        using var rangeRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/overlay/{owner.AccessKey}/media/{asset.Id:D}/{asset.ContentRevision}"
        );
        rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(4, 7);

        using var response = await host.Client.SendAsync(rangeRequest);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var crossHost = await host.Client.GetAsync(
            $"/overlay/{other.AccessKey}/media/{asset.Id:D}/{asset.ContentRevision}"
        );
        await host.SetFeaturesAsync(owner.HostId, HostFeatureFlags.None);
        using var disabled = await host.Client.GetAsync(
            $"/overlay/{owner.AccessKey}/media/{asset.Id:D}/{asset.ContentRevision}"
        );

        response.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("video/mp4");
        response.Content.Headers.ContentRange?.From.ShouldBe(4);
        response.Content.Headers.ContentRange?.To.ShouldBe(7);
        bytes.ShouldBe("ftyp"u8.ToArray());
        response.Headers.CacheControl?.Private.ShouldBeTrue();
        response.Headers.CacheControl?.MaxAge.ShouldBe(TimeSpan.FromDays(365));
        response.Headers.CacheControl?.Extensions.ShouldContain(static value =>
            value.Name == "immutable"
        );
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response
            .Headers.GetValues("Content-Security-Policy")
            .Single()
            .ShouldBe("sandbox; default-src 'none'");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        crossHost.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        disabled.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task CuePlayer_AdmissionPublishesTypedPlanAndCompletionThroughRealTransport()
    {
        await using var host = await BrowserSourceHost.StartAsync();
        var target = await host.SeedCuePlayerAsync("cue-live");
        var cueId = await host.SeedExternalCueAsync(target.HostId);
        await using var live = await host.OpenLiveAsync(target.AccessKey);
        var baseline = await live.ReadEnvelopeAsync();
        var outcome = await host.Playback.AdmitAsync(
            new(
                target.HostId,
                target.OverlayId,
                cueId,
                OverlayCueQueuePolicy.Enqueue,
                OverlayCueAdmissionOrigin.Command,
                new("viewer", "Viewer")
            ),
            CancellationToken.None
        );
        var cue = await live.ReadEnvelopeAsync();
        var runId = cue.GetProperty("payload").GetProperty("runId").GetGuid();
        using var completion = await host.Client.PostAsync(
            $"/overlay/{target.AccessKey}/cue-complete/{runId:D}",
            content: null
        );
        var stopped = await live.ReadEnvelopeAsync();

        baseline.GetProperty("eventType").GetString().ShouldBe("baseline");
        baseline
            .GetProperty("payload")
            .GetProperty("overlayType")
            .GetString()
            .ShouldBe("cuePlayer");
        outcome.ShouldBeOfType<OverlayCueAdmissionOutcome.Running>().RunId.ShouldBe(runId);
        cue.GetProperty("eventType").GetString().ShouldBe("cue");
        cue.GetProperty("payload").GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        cue.GetProperty("payload")
            .GetProperty("layers")[0]
            .GetProperty("kind")
            .GetString()
            .ShouldBe("externalWeb");
        cue.ToString().ShouldNotContain("viewer", Case.Insensitive);
        cue.ToString().ShouldNotContain(target.OverlayId.ToString(), Case.Insensitive);
        cue.ToString().ShouldNotContain(cueId.ToString(), Case.Insensitive);
        completion.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        stopped.GetProperty("eventType").GetString().ShouldBe("cueStop");
        stopped.GetProperty("runId").GetGuid().ShouldBe(runId);
    }

    [Test]
    public async Task LiveStreams_AreKeyResolvedIsolatedSequencedAndPresentationSafe()
    {
        await using var host = await BrowserSourceHost.StartAsync();
        var owner = await host.SeedAsync("live-owner", revision: 12);
        var other = await host.SeedAsync("live-other", revision: 34);
        await using var ownerStream = await host.OpenLiveAsync(
            owner.AccessKey,
            $"?hostId={other.HostId}&overlayId={other.OverlayId}"
        );
        await using var otherStream = await host.OpenLiveAsync(other.AccessKey);

        var ownerBaseline = await ownerStream.ReadEnvelopeAsync();
        var otherBaseline = await otherStream.ReadEnvelopeAsync();

        ownerBaseline.GetProperty("eventType").GetString().ShouldBe("baseline");
        ownerBaseline.GetProperty("sequence").GetInt64().ShouldBe(0);
        otherBaseline.GetProperty("sequence").GetInt64().ShouldBe(0);
        ownerBaseline
            .GetProperty("serverEpoch")
            .GetGuid()
            .ShouldBe(otherBaseline.GetProperty("serverEpoch").GetGuid());
        AssertPresentationSafeEnvelope(ownerBaseline, owner, other);
        host.Presence.Read(owner.HostId, owner.OverlayId).ActiveConnectionCount.ShouldBe(1);
        host.Presence.Read(other.HostId, other.OverlayId).ActiveConnectionCount.ShouldBe(1);
        host.Presence.Read(other.HostId, owner.OverlayId).ActiveConnectionCount.ShouldBe(0);

        host.Live.PublishState(await host.ResolveAsync(owner.AccessKey));
        host.Live.PublishTest(await host.ResolveAsync(other.AccessKey));

        var ownerEvent = await ownerStream.ReadEnvelopeAsync();
        var otherEvent = await otherStream.ReadEnvelopeAsync();
        ownerEvent.GetProperty("eventType").GetString().ShouldBe("state");
        otherEvent.GetProperty("eventType").GetString().ShouldBe("test");
        ownerEvent.GetProperty("sequence").GetInt64().ShouldBe(1);
        otherEvent.GetProperty("sequence").GetInt64().ShouldBe(1);
        AssertPresentationSafeEnvelope(ownerEvent, owner, other);
        AssertPresentationSafeEnvelope(otherEvent, other, owner);
    }

    [Test]
    public async Task OverlayChanges_RevokeLiveMembershipAndRequireFreshKeyResolution()
    {
        await using var host = await BrowserSourceHost.StartAsync();
        var seed = await host.SeedAsync("revoked-live");
        await using var oldStream = await host.OpenLiveAsync(seed.AccessKey);
        _ = await oldStream.ReadEnvelopeAsync();
        host.Presence.Read(seed.HostId, seed.OverlayId).ActiveConnectionCount.ShouldBe(1);
        var replacementKey = BrowserSourceHost.AccessKey('r');
        await host.SetAccessKeyAsync(seed.OverlayId, replacementKey);

        _ = await host.Events.PublishAsync(AppEventKind.OverlaysChanged, CancellationToken.None);

        host.Presence.Read(seed.HostId, seed.OverlayId).ActiveConnectionCount.ShouldBe(0);
        host.Presence.Read(seed.HostId, seed.OverlayId)
            .MostRecentDisconnectedAtUtc.ShouldBe(host.Time.GetUtcNow());
        var terminal = await oldStream.ReadEnvelopeAsync();
        terminal.GetProperty("eventType").GetString().ShouldBe("reauthenticate");
        using var oldKeyFailure = await host.GetLiveResponseAsync(seed.AccessKey);
        oldKeyFailure.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await oldKeyFailure.Content.ReadAsStringAsync()).ShouldBe("Overlay unavailable.");

        await using var replacementStream = await host.OpenLiveAsync(replacementKey);
        var replacementBaseline = await replacementStream.ReadEnvelopeAsync();
        replacementBaseline.GetProperty("eventType").GetString().ShouldBe("baseline");
        replacementBaseline.GetProperty("sequence").GetInt64().ShouldBe(0);
        host.ObservedCompletedPaths.ShouldContain("/overlay/[redacted]/events");
        host.ObservedCompletedPaths.ShouldNotContain(path => path.Contains(seed.AccessKey));
    }

    [Test]
    public async Task ClientCancellation_RemovesPresenceAndReconnectionReceivesCurrentBaseline()
    {
        await using var host = await BrowserSourceHost.StartAsync();
        var seed = await host.SeedAsync("reconnect-live");
        var instance = await host.ResolveAsync(seed.AccessKey);
        var firstStream = await host.OpenLiveAsync(seed.AccessKey);
        _ = await firstStream.ReadEnvelopeAsync();
        host.Live.PublishTest(instance);
        (await firstStream.ReadEnvelopeAsync()).GetProperty("sequence").GetInt64().ShouldBe(1);

        await firstStream.DisposeAsync();
        await host.WaitForPresenceAsync(seed, activeConnectionCount: 0);
        host.Presence.Read(seed.HostId, seed.OverlayId).ActiveConnectionCount.ShouldBe(0);

        await using var reconnected = await host.OpenLiveAsync(seed.AccessKey);
        var baseline = await reconnected.ReadEnvelopeAsync();
        baseline.GetProperty("eventType").GetString().ShouldBe("baseline");
        baseline.GetProperty("sequence").GetInt64().ShouldBe(1);
        host.Live.PublishTest(instance);
        (await reconnected.ReadEnvelopeAsync()).GetProperty("sequence").GetInt64().ShouldBe(2);
    }

    [Test]
    public async Task ServerRestart_ChangesLiveEpochBeforeSequenceRestarts()
    {
        await using var firstHost = await BrowserSourceHost.StartAsync();
        var firstSeed = await firstHost.SeedAsync("first-epoch");
        await using var firstStream = await firstHost.OpenLiveAsync(firstSeed.AccessKey);
        var firstBaseline = await firstStream.ReadEnvelopeAsync();

        await using var secondHost = await BrowserSourceHost.StartAsync();
        var secondSeed = await secondHost.SeedAsync("second-epoch");
        await using var secondStream = await secondHost.OpenLiveAsync(secondSeed.AccessKey);
        var secondBaseline = await secondStream.ReadEnvelopeAsync();

        firstBaseline.GetProperty("sequence").GetInt64().ShouldBe(0);
        secondBaseline.GetProperty("sequence").GetInt64().ShouldBe(0);
        firstBaseline
            .GetProperty("serverEpoch")
            .GetGuid()
            .ShouldNotBe(secondBaseline.GetProperty("serverEpoch").GetGuid());
    }

    [Test]
    public async Task BrowserSource_UsesRestrictiveHeadersTransparentCanvasAndSnapshotFirstLiveClient()
    {
        await using var host = await BrowserSourceHost.StartAsync();
        var seed = await host.SeedAsync("security");

        using var document = await host.Client.GetAsync($"/overlay/{seed.AccessKey}");
        using var state = await host.Client.GetAsync($"/overlay/{seed.AccessKey}/state");
        using var stylesheet = await host.Client.GetAsync("/overlay/assets/blokebot-overlay.css");
        using var script = await host.Client.GetAsync("/overlay/assets/blokebot-overlay.js");
        var css = await stylesheet.Content.ReadAsStringAsync();
        var javascript = await script.Content.ReadAsStringAsync();

        AssertPrivateHeaders(document);
        AssertPrivateHeaders(state);
        AssertPrivateHeaders(stylesheet);
        AssertPrivateHeaders(script);
        var html = await document.Content.ReadAsStringAsync();
        html.ShouldContain("viewBox=\"0 0 1920 1080\"");
        html.ShouldContain("preserveAspectRatio=\"xMidYMid meet\"");
        css.ShouldContain("width: 100%");
        css.ShouldContain("height: 100%");
        css.ShouldContain("overflow: hidden");
        css.ShouldContain("background: transparent");
        javascript.ShouldContain("await loadCurrentState(pageLifetime.signal)");
        javascript.ShouldContain("refreshAppearanceStylesheet(snapshot.sequence)");
        javascript.ShouldContain("fetch(root.dataset.stateUrl");
        javascript.ShouldContain("fetch(root.dataset.liveUrl");
        javascript.ShouldContain("headers: { Accept: \"text/event-stream\" }");
        javascript.ShouldContain("canvas.replaceChildren()");
        javascript.ShouldContain("envelope.sequence <= liveSequence");
        javascript.ShouldContain("envelope.sequence !== liveSequence + 1");
        javascript.ShouldContain("envelope.serverEpoch !== liveEpoch");
        javascript.ShouldContain("const reconnectDelay = (attempt, randomValue)");
        javascript.ShouldContain("maximumRetryDelayMilliseconds");
        javascript.ShouldContain("Math.random()");
        javascript.ShouldContain("while (!pageLifetime.signal.aborted)");
        javascript.ShouldNotContain("WebSocket");
        javascript.ShouldNotContain("EventSource");
        javascript.ShouldNotContain("setInterval");
        javascript.ShouldNotContain("style.setProperty");
        javascript.ShouldNotContain("http://");
        javascript.ShouldNotContain("https://");
        await AssertDelayLifecycleAsync(javascript);
    }

    [Test]
    public async Task GuessingPreview_AllSamplesRequireBothParentsAndNeverExposeSuppressedState()
    {
        await using var host = await BrowserSourceHost.StartAsync();
        var seed = await host.SeedGuessingAsync("preview");

        using (
            var previewDocument = await host.Client.GetAsync(
                $"/overlays/preview/{seed.OverlayId:D}"
            )
        )
        using (var privateDocument = await host.Client.GetAsync($"/overlay/{seed.AccessKey}"))
        {
            previewDocument
                .Headers.GetValues("Content-Security-Policy")
                .Single()
                .ShouldContain("sandbox allow-scripts allow-same-origin;");
            previewDocument
                .Headers.GetValues("Content-Security-Policy")
                .Single()
                .ShouldContain("frame-ancestors 'self'");
            previewDocument
                .Headers.GetValues("Content-Security-Policy")
                .Single()
                .ShouldContain("script-src 'self'");
            privateDocument
                .Headers.GetValues("Content-Security-Policy")
                .Single()
                .ShouldNotContain("sandbox");
        }

        foreach (var sample in new[] { "no-round", "open", "closed", "completed" })
        {
            using var response = await host.Client.GetAsync(
                $"/overlays/preview/{seed.OverlayId:D}/state?mode=representative&sample={sample}"
            );
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            document.RootElement.GetProperty("overlayType").GetString().ShouldBe("guessing");
        }

        await host.SetFeaturesAsync(seed.HostId, HostFeatureFlags.Overlays);
        foreach (
            var path in new[]
            {
                $"/overlays/preview/{seed.OverlayId:D}?mode=representative&sample=completed",
                $"/overlays/preview/{seed.OverlayId:D}/state?mode=representative&sample=completed",
                $"/overlays/preview/{seed.OverlayId:D}/events",
            }
        )
        {
            using var response = await host.Client.GetAsync(path);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).ShouldBe("Overlay unavailable.");
        }

        await host.SetFeaturesAsync(seed.HostId, HostFeatureFlags.Guessing);
        using var overlaysOff = await host.Client.GetAsync(
            $"/overlays/preview/{seed.OverlayId:D}/state?mode=representative&sample=completed"
        );
        overlaysOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await host.SetFeaturesAsync(
            seed.HostId,
            HostFeatureFlags.Overlays | HostFeatureFlags.Guessing
        );
        using var restored = await host.Client.GetAsync(
            $"/overlays/preview/{seed.OverlayId:D}/state?mode=representative&sample=completed"
        );
        restored.StatusCode.ShouldBe(HttpStatusCode.OK);
        var restoredJson = await restored.Content.ReadAsStringAsync();
        restoredJson.ShouldContain("\"phase\":\"completed\"");
        restoredJson.ShouldNotContain("\"animation\"");
    }

    [Test]
    public async Task GiveawayPreview_AllSamplesRequireBothParentsAndExposeNoPrivateEntrants()
    {
        await using var host = await BrowserSourceHost.StartAsync();
        var seed = await host.SeedGiveawayAsync("giveaway-preview");

        foreach (var sample in new[] { "idle", "open", "ending", "completed", "cancelled" })
        {
            using var documentResponse = await host.Client.GetAsync(
                $"/overlays/preview/{seed.OverlayId:D}?mode=representative&sample={sample}"
            );
            documentResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await documentResponse.Content.ReadAsStringAsync()).ShouldContain(
                $"data-state-url=\"/overlays/preview/{seed.OverlayId:D}/state?mode=representative&amp;sample={sample}\""
            );

            using var response = await host.Client.GetAsync(
                $"/overlays/preview/{seed.OverlayId:D}/state?mode=representative&sample={sample}"
            );
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var json = await response.Content.ReadAsStringAsync();
            json.ShouldContain("\"overlayType\":\"giveaway\"");
            json.ShouldNotContain("eligibility", Case.Insensitive);
            json.ShouldNotContain("private-entrant", Case.Insensitive);
        }

        await host.SetFeaturesAsync(seed.HostId, HostFeatureFlags.Overlays);
        using var pointsOff = await host.Client.GetAsync(
            $"/overlays/preview/{seed.OverlayId:D}/state?mode=representative&sample=completed"
        );
        pointsOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await host.SetFeaturesAsync(seed.HostId, HostFeatureFlags.Points);
        using var overlaysOff = await host.Client.GetAsync($"/overlay/{seed.AccessKey}/state");
        overlaysOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await host.SetFeaturesAsync(
            seed.HostId,
            HostFeatureFlags.Overlays | HostFeatureFlags.Points
        );
        using var restored = await host.Client.GetAsync(
            $"/overlays/preview/{seed.OverlayId:D}/state?mode=representative&sample=completed"
        );
        restored.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await restored.Content.ReadAsStringAsync()).ShouldNotContain("\"animation\"");
    }

    [Test]
    public async Task EventFeedBrowserSource_LiveReconnectAndSamplesExposeOnlyDecodedSafeFields()
    {
        await using var host = await BrowserSourceHost.StartAsync();
        var seed = await host.SeedEventFeedAsync("event-feed");
        await host.PresentPointEventAsync(seed, "private-ledger-key", "<b>viewer & friend</b>");

        using (var documentResponse = await host.Client.GetAsync($"/overlay/{seed.AccessKey}"))
        using (
            var stylesheetResponse = await host.Client.GetAsync(
                "/overlay/assets/blokebot-overlay.css"
            )
        )
        using (
            var scriptResponse = await host.Client.GetAsync("/overlay/assets/blokebot-overlay.js")
        )
        {
            var html = await documentResponse.Content.ReadAsStringAsync();
            html.ShouldContain("viewBox=\"0 0 1920 1080\"");
            var stylesheet = await stylesheetResponse.Content.ReadAsStringAsync();
            stylesheet.ShouldContain("white-space: pre-wrap");
            stylesheet.ShouldContain("overflow-wrap: anywhere");
            var script = await scriptResponse.Content.ReadAsStringAsync();
            script.ShouldContain("svgElement(\"foreignObject\"");
            script.ShouldContain("body.textContent = text");
            script.ShouldNotContain("Intl.Segmenter");
            script.ShouldNotContain("\"tspan\"");
            script.ShouldNotContain("innerHTML");
            script.ShouldNotContain("outerHTML");
        }

        using (var response = await host.Client.GetAsync($"/overlay/{seed.AccessKey}/state"))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            root.GetProperty("overlayType").GetString().ShouldBe("eventFeed");
            root.GetProperty("state")
                .GetProperty("active")
                .GetProperty("body")
                .GetString()
                .ShouldBe("<b>viewer & friend</b> received 5 points");
            root.GetRawText().ShouldNotContain("private-ledger-key");
            root.GetRawText().ShouldNotContain("hostId", Case.Insensitive);
            root.GetRawText().ShouldNotContain("sourceKey", Case.Insensitive);
        }

        await using (var live = await host.OpenLiveAsync(seed.AccessKey))
        {
            var baseline = await live.ReadEnvelopeAsync();
            baseline.GetProperty("eventType").GetString().ShouldBe("baseline");
            baseline
                .GetProperty("payload")
                .GetProperty("state")
                .GetProperty("active")
                .GetProperty("body")
                .GetString()
                .ShouldNotBeNull()
                .ShouldContain("<b>viewer & friend</b>");
        }

        foreach (var sample in new[] { "point-award", "guessing-winner", "giveaway-winner" })
        {
            using var response = await host.Client.GetAsync(
                $"/overlays/preview/{seed.OverlayId:D}/state?mode=representative&sample={sample}"
            );
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            root.GetProperty("overlayType").GetString().ShouldBe("eventFeed");
            root.GetProperty("animation").GetString().ShouldBe("sample");
            root.GetProperty("state")
                .GetProperty("active")
                .GetProperty("body")
                .GetString()
                .ShouldNotBeNull()
                .ShouldNotContain("&lt;");
        }

        await host.SetFeaturesAsync(seed.HostId, HostFeatureFlags.Overlays);
        using (var pointsOff = await host.Client.GetAsync($"/overlay/{seed.AccessKey}/state"))
        {
            pointsOff.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await pointsOff.Content.ReadAsStringAsync());
            document
                .RootElement.GetProperty("state")
                .GetProperty("active")
                .ValueKind.ShouldBe(JsonValueKind.Null);
        }
        await host.SetFeaturesAsync(
            seed.HostId,
            HostFeatureFlags.Overlays | HostFeatureFlags.Points | HostFeatureFlags.Guessing
        );
        using (var restored = await host.Client.GetAsync($"/overlay/{seed.AccessKey}/state"))
        {
            using var document = JsonDocument.Parse(await restored.Content.ReadAsStringAsync());
            document
                .RootElement.GetProperty("state")
                .GetProperty("active")
                .ValueKind.ShouldBe(JsonValueKind.Null);
        }
        await host.SetFeaturesAsync(seed.HostId, HostFeatureFlags.Points);
        using var overlaysOff = await host.Client.GetAsync($"/overlay/{seed.AccessKey}/state");
        overlaysOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task AssertDelayLifecycleAsync(string javascript)
    {
        const string DelayStart = "const delay =";
        const string DelayEnd = "\n\n  const reconnectDelay";
        var start = javascript.IndexOf(DelayStart, StringComparison.Ordinal);
        var end = javascript.IndexOf(DelayEnd, start, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        end.ShouldBeGreaterThan(start);
        var delaySource = javascript[start..end];
        var harness = $$"""
            const assert = require("node:assert/strict");

            class FakeSignal {
              constructor() {
                this.aborted = false;
                this.listeners = new Set();
              }

              addEventListener(type, listener, options) {
                assert.equal(type, "abort");
                assert.equal(options.once, true);
                this.listeners.add(listener);
              }

              removeEventListener(type, listener) {
                assert.equal(type, "abort");
                this.listeners.delete(listener);
              }

              abort() {
                this.aborted = true;
                for (const listener of [...this.listeners]) {
                  listener();
                }
              }
            }

            const timerCallbacks = [];
            const clearedTimers = [];
            global.window = {
              setTimeout(callback) {
                timerCallbacks.push(callback);
                return timerCallbacks.length;
              },
              clearTimeout(timer) {
                clearedTimers.push(timer);
              },
            };

            {{delaySource}}

            (async () => {
              const normalSignal = new FakeSignal();
              let normalResolved = false;
              const normal = delay(500, normalSignal).then(() => {
                normalResolved = true;
              });
              assert.equal(normalSignal.listeners.size, 1);
              timerCallbacks[0]();
              await normal;
              assert.equal(normalResolved, true);
              assert.equal(normalSignal.listeners.size, 0);
              assert.deepEqual(clearedTimers, []);

              const abortSignal = new FakeSignal();
              let abortResolved = false;
              const aborted = delay(500, abortSignal).then(() => {
                abortResolved = true;
              });
              assert.equal(abortSignal.listeners.size, 1);
              abortSignal.abort();
              await aborted;
              assert.equal(abortResolved, true);
              assert.equal(abortSignal.listeners.size, 0);
              assert.deepEqual(clearedTimers, [2]);
            })().catch((error) => {
              console.error(error);
              process.exitCode = 1;
            });
            """;

        var startInfo = new ProcessStartInfo("node")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--eval");
        startInfo.ArgumentList.Add(harness);
        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Node.js.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(timeout.Token);
        process.ExitCode.ShouldBe(0, $"{await output}\n{await error}");
    }

    private static void AssertPresentationSafeEnvelope(
        JsonElement envelope,
        OverlaySeed seed,
        OverlaySeed other
    )
    {
        envelope.GetProperty("protocolVersion").GetInt32().ShouldBe(1);
        envelope.GetProperty("payload").GetProperty("overlayType").GetString().ShouldBe("empty");
        envelope.GetProperty("payload").GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        envelope.GetProperty("payload").GetProperty("state").EnumerateObject().ShouldBeEmpty();
        var json = envelope.GetRawText();
        json.ShouldNotContain(seed.AccessKey);
        json.ShouldNotContain(seed.OverlayId.ToString(), Case.Insensitive);
        json.ShouldNotContain(other.AccessKey);
        json.ShouldNotContain(other.OverlayId.ToString(), Case.Insensitive);
    }

    private static void AssertPrivateHeaders(HttpResponseMessage response)
    {
        response
            .Headers.GetValues("X-Robots-Tag")
            .Single()
            .ShouldBe("noindex, nofollow, noarchive");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        response
            .Headers.GetValues("Content-Security-Policy")
            .Single()
            .ShouldContain("default-src 'none'");
        response
            .Headers.GetValues("Content-Security-Policy")
            .Single()
            .ShouldContain("connect-src 'self'");
    }

    private sealed class BrowserSourceHost(
        WebApplication app,
        HttpClient client,
        SqliteBlokeBotDbFactory database,
        MutableTimeProvider time,
        ConcurrentQueue<string> observedCompletedPaths,
        string mediaRoot
    ) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        internal MutableTimeProvider Time { get; } = time;

        internal IReadOnlyList<string> ObservedCompletedPaths => observedCompletedPaths.ToArray();

        internal IOverlayLivePublisher Live =>
            app.Services.GetRequiredService<IOverlayLivePublisher>();

        internal IOverlayLivePresence Presence =>
            app.Services.GetRequiredService<IOverlayLivePresence>();

        internal EventBus<AppEventKind> Events =>
            app.Services.GetRequiredService<EventBus<AppEventKind>>();

        internal OverlayCuePlaybackService Playback =>
            app.Services.GetRequiredService<OverlayCuePlaybackService>();

        private PreviewAuthenticationSettings _authentication =>
            app.Services.GetRequiredService<PreviewAuthenticationSettings>();

        internal static async Task<BrowserSourceHost> StartAsync(string? pathBase = null)
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            var time = new MutableTimeProvider(
                new DateTimeOffset(2026, 7, 30, 16, 0, 0, TimeSpan.Zero)
            );
            var builder = WebApplication.CreateBuilder();
            _ = builder.Services.AddLogging();
            _ = builder.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
            _ = builder.Services.AddSingleton<TimeProvider>(time);
            _ = builder.Services.AddSingleton(TestEventBus.Create<AppEventKind>());
            _ = builder.Services.AddSingleton<HostedChannelChangeNotifier>();
            _ = builder.Services.AddSingleton<HostFeatureService>();
            _ = builder.Services.AddSingleton<IModeratorAuthorityService>(
                new GrantedModeratorAuthority()
            );
            _ = builder.Services.AddSingleton(new PreviewAuthenticationSettings());
            var mediaRoot = Path.Combine(
                Path.GetTempPath(),
                $"blokebot-browser-source-media-{Guid.NewGuid():N}"
            );
            _ = Directory.CreateDirectory(mediaRoot);
            _ = builder.Services.AddSingleton<IOptions<BlokeBotOptions>>(
                Options.Create(
                    new BlokeBotOptions { DatabasePath = Path.Combine(mediaRoot, "state.db") }
                )
            );
            _ = builder
                .Services.AddAuthentication(PreviewAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, PreviewAuthenticationHandler>(
                    PreviewAuthenticationHandler.SchemeName,
                    static _ => { }
                );
            _ = builder.Services.AddAuthorization(options =>
                options.AddPolicy(
                    "HostSelected",
                    policy =>
                        policy
                            .RequireAuthenticatedUser()
                            .AddRequirements(
                                new AuthSessionCapabilityRequirement(
                                    AuthSessionCapability.HostSelected
                                )
                            )
                )
            );
            _ = builder.Services.AddSingleton<
                IAuthorizationHandler,
                AuthSessionCapabilityHandler
            >();
            _ = builder.Services.AddBlokeBotOverlays();
            _ = builder.Services.AddSingleton<IOverlayDnsResolver>(new PublicOverlayDnsResolver());

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            if (pathBase is not null)
            {
                _ = app.UsePathBase(pathBase);
            }
            _ = app.UseAuthentication();
            _ = app.UseAuthorization();
            var observedCompletedPaths = new ConcurrentQueue<string>();
            _ = app.Use(
                async (context, next) =>
                {
                    await next(context);
                    observedCompletedPaths.Enqueue(context.Request.Path.Value ?? string.Empty);
                }
            );
            app.UseOverlayAccessLogRedaction();
            app.MapOverlayBrowserSourceEndpoints();
            await app.StartAsync();

            var address =
                app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()
                    ?.Addresses.ShouldHaveSingleItem()
                ?? throw new InvalidOperationException("The test host did not publish an address.");
            return new BrowserSourceHost(
                app,
                new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                {
                    BaseAddress = new Uri(address),
                },
                database,
                time,
                observedCompletedPaths,
                mediaRoot
            );
        }

        internal async Task<OverlaySeed> SeedAsync(
            string login,
            bool enabled = true,
            long revision = 1
        )
        {
            var accessKey = AccessKey(login);
            await using var db = await database.CreateDbContextAsync();
            var host = new BotHost
            {
                EnabledFeatures = HostFeatureFlags.All,
                TwitchUserId = $"{login}-id",
                Login = login,
                DisplayName = login,
                CreatedAtUtc = Time.GetUtcNow().UtcDateTime,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            var overlay = new OverlayInstance
            {
                PublicId = Guid.NewGuid(),
                HostId = host.Id,
                Name = login,
                Type = OverlayType.Empty,
                IsEnabled = enabled,
                ConfigurationJson = """{"schemaVersion":1}""",
                AccessKeyDigest = OverlayAccessKeyDigest.Compute(accessKey),
                KeyVersion = 1,
                Revision = revision,
                CreatedAtUtc = Time.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = Time.GetUtcNow().UtcDateTime,
            };
            _ = db.OverlayInstances.Add(overlay);
            _ = await db.SaveChangesAsync();
            _authentication.SelectedHostId = host.Id;
            return new OverlaySeed(host.Id, overlay.PublicId, accessKey);
        }

        internal async Task<OverlaySeed> SeedGuessingAsync(string login)
        {
            var accessKey = AccessKey(login);
            await using var db = await database.CreateDbContextAsync();
            var host = new BotHost
            {
                EnabledFeatures = HostFeatureFlags.Overlays | HostFeatureFlags.Guessing,
                TwitchUserId = $"{login}-id",
                Login = login,
                DisplayName = login,
                CreatedAtUtc = Time.GetUtcNow().UtcDateTime,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            var overlay = new OverlayInstance
            {
                PublicId = Guid.NewGuid(),
                HostId = host.Id,
                Name = login,
                Type = OverlayType.Guessing,
                IsEnabled = true,
                ConfigurationJson =
                    """{"schemaVersion":1,"showGuessCount":true,"resultDurationSeconds":8}""",
                AccessKeyDigest = OverlayAccessKeyDigest.Compute(accessKey),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = Time.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = Time.GetUtcNow().UtcDateTime,
            };
            _ = db.OverlayInstances.Add(overlay);
            _ = await db.SaveChangesAsync();
            _authentication.SelectedHostId = host.Id;
            return new OverlaySeed(host.Id, overlay.PublicId, accessKey);
        }

        internal async Task<OverlaySeed> SeedGiveawayAsync(string login)
        {
            var accessKey = AccessKey(login);
            await using var db = await database.CreateDbContextAsync();
            var host = new BotHost
            {
                EnabledFeatures = HostFeatureFlags.Overlays | HostFeatureFlags.Points,
                TwitchUserId = $"{login}-id",
                Login = login,
                DisplayName = login,
                CreatedAtUtc = Time.GetUtcNow().UtcDateTime,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            _ = db.PointsSettings.Add(
                new PointsSettings { HostId = host.Id, PointLabel = "points" }
            );
            _ = db.CommandAliases.Add(
                new CommandAlias
                {
                    HostId = host.Id,
                    Kind = AppCommandKind.Join,
                    Alias = "enter",
                }
            );
            var overlay = new OverlayInstance
            {
                PublicId = Guid.NewGuid(),
                HostId = host.Id,
                Name = login,
                Type = OverlayType.Giveaway,
                IsEnabled = true,
                ConfigurationJson =
                    """{"schemaVersion":1,"title":"Community giveaway","showEntrantCount":true,"showCountdown":true,"showJoinCommand":true}""",
                AccessKeyDigest = OverlayAccessKeyDigest.Compute(accessKey),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = Time.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = Time.GetUtcNow().UtcDateTime,
            };
            _ = db.OverlayInstances.Add(overlay);
            _ = await db.SaveChangesAsync();
            _authentication.SelectedHostId = host.Id;
            return new OverlaySeed(host.Id, overlay.PublicId, accessKey);
        }

        internal async Task<OverlaySeed> SeedEventFeedAsync(string login)
        {
            var accessKey = AccessKey(login);
            await using var db = await database.CreateDbContextAsync();
            var host = new BotHost
            {
                EnabledFeatures =
                    HostFeatureFlags.Overlays | HostFeatureFlags.Points | HostFeatureFlags.Guessing,
                TwitchUserId = $"{login}-id",
                Login = login,
                DisplayName = login,
                CreatedAtUtc = Time.GetUtcNow().UtcDateTime,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            var overlay = new OverlayInstance
            {
                PublicId = Guid.NewGuid(),
                HostId = host.Id,
                Name = login,
                Type = OverlayType.EventFeed,
                IsEnabled = true,
                ConfigurationJson = OverlayConfiguration.EventFeedV1.Default.ToPersistenceJson(),
                AccessKeyDigest = OverlayAccessKeyDigest.Compute(accessKey),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = Time.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = Time.GetUtcNow().UtcDateTime,
            };
            _ = db.OverlayInstances.Add(overlay);
            _ = await db.SaveChangesAsync();
            _authentication.SelectedHostId = host.Id;
            return new OverlaySeed(host.Id, overlay.PublicId, accessKey);
        }

        internal Task PresentPointEventAsync(
            OverlaySeed seed,
            string sourceKey,
            string recipient
        ) =>
            app
                .Services.GetRequiredService<IOverlayEventPresenter>()
                .PresentAsync(
                    new OverlayEventPresentation.PointAward
                    {
                        HostId = seed.HostId,
                        SourceKey = sourceKey,
                        Recipient = recipient,
                        Amount = "5",
                        PointLabel = "points",
                    },
                    CancellationToken.None
                );

        internal async Task<OverlaySeed> SeedCuePlayerAsync(string login)
        {
            var accessKey = AccessKey(login);
            await using var db = await database.CreateDbContextAsync();
            var host = new BotHost
            {
                EnabledFeatures = HostFeatureFlags.Overlays,
                TwitchUserId = $"{login}-id",
                Login = login,
                DisplayName = login,
                CreatedAtUtc = Time.GetUtcNow().UtcDateTime,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            var overlay = new OverlayInstance
            {
                PublicId = Guid.NewGuid(),
                HostId = host.Id,
                Name = login,
                Type = OverlayType.CuePlayer,
                IsEnabled = true,
                ConfigurationJson = """{"schemaVersion":1}""",
                AccessKeyDigest = OverlayAccessKeyDigest.Compute(accessKey),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = Time.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = Time.GetUtcNow().UtcDateTime,
            };
            _ = db.OverlayInstances.Add(overlay);
            _ = await db.SaveChangesAsync();
            _authentication.SelectedHostId = host.Id;
            return new OverlaySeed(host.Id, overlay.PublicId, accessKey);
        }

        internal async Task<OverlayMediaAssetView> UploadMp4Async(OverlaySeed seed, string name)
        {
            var host = new BotHostChoice(
                seed.HostId,
                "media-owner",
                "Media owner",
                AuthRole.Streamer
            );
            var session = new AuthenticatedSession
            {
                IsAuthenticated = true,
                UserId = "media-owner-id",
                Login = "media-owner",
                State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
            };
            await using var content = new MemoryStream([
                0,
                0,
                0,
                12,
                (byte)'f',
                (byte)'t',
                (byte)'y',
                (byte)'p',
                (byte)'i',
                (byte)'s',
                (byte)'o',
                (byte)'m',
            ]);
            return (
                await app
                    .Services.GetRequiredService<OverlayCueService>()
                    .UploadAssetAsync(session, name, "video/mp4", content, CancellationToken.None)
            )
                .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Succeeded>()
                .Value;
        }

        internal async Task<Guid> SeedExternalCueAsync(int hostId)
        {
            await using var db = await database.CreateDbContextAsync();
            var cue = new OverlayCue
            {
                PublicId = Guid.NewGuid(),
                HostId = hostId,
                Name = "External widget",
                IsEnabled = true,
                DurationMilliseconds = 1000,
                QueuePolicy = OverlayCueQueuePolicy.Enqueue,
                ConfigurationJson =
                    """{"schemaVersion":1,"layers":[{"type":"externalWeb","url":"https://widget.example.test/","startOffsetMilliseconds":0,"durationMilliseconds":1000,"zIndex":0,"rectangle":{"xPercent":0,"yPercent":0,"widthPercent":100,"heightPercent":100}}]}""",
                Revision = 1,
                CreatedAtUtc = Time.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = Time.GetUtcNow().UtcDateTime,
            };
            _ = db.OverlayCues.Add(cue);
            _ = await db.SaveChangesAsync();
            return cue.PublicId;
        }

        internal async Task SetFeaturesAsync(int hostId, HostFeatureFlags features)
        {
            await using var db = await database.CreateDbContextAsync();
            _ = await db
                .Hosts.Where(host => host.Id == hostId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(host => host.EnabledFeatures, features)
                );
        }

        internal async Task<LiveStream> OpenLiveAsync(string accessKey, string query = "")
        {
            var response = await GetLiveResponseAsync(accessKey, query);
            _ = response.EnsureSuccessStatusCode();
            response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
            response.Headers.GetValues("X-Accel-Buffering").Single().ShouldBe("no");
            response.Headers.CacheControl?.NoStore.ShouldBeTrue();
            response.Headers.CacheControl?.NoTransform.ShouldBeTrue();
            return await LiveStream.CreateAsync(response);
        }

        internal async Task<HttpResponseMessage> GetLiveResponseAsync(
            string accessKey,
            string query = "",
            CancellationToken cancellationToken = default
        )
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/overlay/{accessKey}/events{query}"
            );
            request.Headers.Accept.ParseAdd("text/event-stream");
            return await Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
        }

        internal async Task<ResolvedOverlayInstance> ResolveAsync(string accessKey)
        {
            var result = await app
                .Services.GetRequiredService<OverlayInstanceResolver>()
                .ResolveAsync(accessKey, CancellationToken.None);
            return result.ShouldBeOfType<OverlayResolutionResult.Resolved>().Instance;
        }

        internal async Task WaitForPresenceAsync(OverlaySeed seed, int activeConnectionCount)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (
                Presence.Read(seed.HostId, seed.OverlayId).ActiveConnectionCount
                != activeConnectionCount
            )
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        internal async Task SetRevisionAsync(Guid overlayId, long revision)
        {
            await using var db = await database.CreateDbContextAsync();
            _ = await db
                .OverlayInstances.Where(value => value.PublicId == overlayId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(value => value.Revision, revision)
                );
        }

        internal async Task SetConfigurationAsync(
            Guid overlayId,
            OverlayConfiguration configuration,
            long revision
        )
        {
            await using var db = await database.CreateDbContextAsync();
            _ = await db
                .OverlayInstances.Where(value => value.PublicId == overlayId)
                .ExecuteUpdateAsync(setters =>
                    setters
                        .SetProperty(
                            value => value.ConfigurationJson,
                            configuration.ToPersistenceJson()
                        )
                        .SetProperty(value => value.Revision, revision)
                );
        }

        internal async Task SetAccessKeyAsync(Guid overlayId, string accessKey)
        {
            var digest = OverlayAccessKeyDigest.Compute(accessKey);
            await using var db = await database.CreateDbContextAsync();
            _ = await db
                .OverlayInstances.Where(value => value.PublicId == overlayId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(value => value.AccessKeyDigest, digest)
                );
        }

        internal async Task<JsonElement> GetStateAsync(string accessKey)
        {
            using var response = await Client.GetAsync($"/overlay/{accessKey}/state");
            _ = response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        internal static string AccessKey(char seed) => new string(seed, 43);

        private static string AccessKey(string seed) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..43];

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
            await database.DisposeAsync();
            Directory.Delete(mediaRoot, recursive: true);
        }
    }

    private sealed class LiveStream(
        HttpResponseMessage response,
        StreamReader reader,
        CancellationTokenSource lifetime
    ) : IAsyncDisposable
    {
        internal static async Task<LiveStream> CreateAsync(HttpResponseMessage response)
        {
            var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stream = await response.Content.ReadAsStreamAsync(lifetime.Token);
            return new LiveStream(response, new StreamReader(stream), lifetime);
        }

        internal async Task<JsonElement> ReadEnvelopeAsync()
        {
            while (true)
            {
                var line =
                    await reader.ReadLineAsync(lifetime.Token)
                    ?? throw new InvalidOperationException(
                        "The overlay live stream ended before an event arrived."
                    );

                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line[6..]);
                return document.RootElement.Clone();
            }
        }

        public ValueTask DisposeAsync()
        {
            lifetime.Cancel();
            reader.Dispose();
            response.Dispose();
            lifetime.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed record OverlaySeed(int HostId, Guid OverlayId, string AccessKey);

    internal sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class PreviewAuthenticationSettings
    {
        internal int SelectedHostId { get; set; }
    }

    private sealed class PreviewAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        PreviewAuthenticationSettings settings
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal const string SchemeName = "OverlayPreviewTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var host = new BotHostChoice(
                settings.SelectedHostId,
                "preview",
                "Preview",
                AuthRole.Streamer
            );
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "preview-id"),
                new Claim(ClaimTypes.Name, "preview"),
                new Claim(AuthClaims.Login, "preview"),
                new Claim(BotHostClaims.AvailableHost, BotHostClaimCodec.Encode(host)),
                new Claim(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(host)),
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class GrantedModeratorAuthority : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        ) => Task.FromResult<ModeratorAuthorityOutcome>(new ModeratorAuthorityOutcome.Granted());
    }

    private sealed class PublicOverlayDnsResolver : IOverlayDnsResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("203.0.113.10")]);
    }
}
