using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Hosting;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Simulation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

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
        IOverlayStateProvider provider = new OverlayStateProvider(epoch, time);
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
        snapshot.State.ShouldBeOfType<EmptyV1OverlayPresentationState>();
        var json = JsonSerializer.Serialize(
            snapshot,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
        json.ShouldContain("\"state\":{}");
        using var document = JsonDocument.Parse(json);
        document
            .RootElement.EnumerateObject()
            .Select(property => property.Name)
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
            .Select(property => property.Name)
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
    public async Task Simulator_SeedsAReproducibleAnonymousBrowserSourceRoute()
    {
        await using var simulation = await SimulationApplication.BuildAsync(
            ["--urls=http://127.0.0.1:0"],
            CancellationToken.None
        );
        await simulation.App.InitializeSimulationAsync(CancellationToken.None);
        await simulation.App.StartAsync();
        var address = simulation
            .App.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.ShouldHaveSingleItem();
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        using var document = await client.GetAsync(
            $"/overlay/{SimulationFixtureSeeder.OverlayAccessKey}"
        );
        using var state = await client.GetAsync(
            $"/overlay/{SimulationFixtureSeeder.OverlayAccessKey}/state"
        );

        document.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await document.Content.ReadAsStringAsync()).ShouldContain("id=\"overlay-canvas\"");
        state.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await state.Content.ReadAsStringAsync();
        json.ShouldContain("\"overlayType\":\"empty\"");
        json.ShouldContain("\"sequence\":1");
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
        await oldStream.ReadEnvelopeAsync();
        host.Presence.Read(seed.HostId, seed.OverlayId).ActiveConnectionCount.ShouldBe(1);
        var replacementKey = BrowserSourceHost.AccessKey('r');
        await host.SetAccessKeyAsync(seed.OverlayId, replacementKey);

        await host.Events.PublishAsync(AppEventKind.OverlaysChanged, CancellationToken.None);

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
        await firstStream.ReadEnvelopeAsync();
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
        ConcurrentQueue<string> observedCompletedPaths
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

        internal static async Task<BrowserSourceHost> StartAsync(string? pathBase = null)
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            var time = new MutableTimeProvider(
                new DateTimeOffset(2026, 7, 30, 16, 0, 0, TimeSpan.Zero)
            );
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddLogging();
            builder.Services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
            builder.Services.AddSingleton<TimeProvider>(time);
            builder.Services.AddSingleton(TestEventBus.Create<AppEventKind>());
            builder.Services.AddBlokeBotOverlays();

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            if (pathBase is not null)
            {
                app.UsePathBase(pathBase);
            }
            var observedCompletedPaths = new ConcurrentQueue<string>();
            app.Use(
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
                observedCompletedPaths
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
                TwitchUserId = $"{login}-id",
                Login = login,
                DisplayName = login,
                CreatedAtUtc = Time.GetUtcNow().UtcDateTime,
            };
            db.Hosts.Add(host);
            await db.SaveChangesAsync();
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
            db.OverlayInstances.Add(overlay);
            await db.SaveChangesAsync();
            return new OverlaySeed(host.Id, overlay.PublicId, accessKey);
        }

        internal async Task<LiveStream> OpenLiveAsync(string accessKey, string query = "")
        {
            var response = await GetLiveResponseAsync(accessKey, query);
            response.EnsureSuccessStatusCode();
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
            await db
                .OverlayInstances.Where(value => value.PublicId == overlayId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(value => value.Revision, revision)
                );
        }

        internal async Task SetAccessKeyAsync(Guid overlayId, string accessKey)
        {
            var digest = OverlayAccessKeyDigest.Compute(accessKey);
            await using var db = await database.CreateDbContextAsync();
            await db
                .OverlayInstances.Where(value => value.PublicId == overlayId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(value => value.AccessKeyDigest, digest)
                );
        }

        internal async Task<JsonElement> GetStateAsync(string accessKey)
        {
            using var response = await Client.GetAsync($"/overlay/{accessKey}/state");
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        internal static string AccessKey(char seed)
        {
            return new string(seed, 43);
        }

        private static string AccessKey(string seed)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..43];
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
            await database.DisposeAsync();
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

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        internal void Advance(TimeSpan duration)
        {
            _now += duration;
        }
    }
}
