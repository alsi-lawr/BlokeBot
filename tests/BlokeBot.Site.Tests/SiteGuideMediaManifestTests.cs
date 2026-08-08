using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using BlokeBot.Site.Content;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Shouldly;

namespace BlokeBot.Site.Tests;

[NotInParallel]
public sealed class SiteGuideMediaManifestTests
{
    private static readonly string _repositoryRoot = FindRepositoryRoot();
    private static readonly string _mediaRoot = Path.Combine(
        _repositoryRoot,
        "src",
        "BlokeBot.Site",
        "wwwroot",
        "media"
    );

    [Test]
    public void Manifest_MatchesSiteReferencesCaptureMatrixAndDecodableAssets()
    {
        var manifest = LoadManifest();
        manifest.Version.ShouldBe(1);
        manifest.Assets.Count.ShouldBe(108);
        manifest.Assets.Count(static asset => asset.Format == "png").ShouldBe(100);
        manifest.Assets.Count(static asset => asset.Format == "webp").ShouldBe(8);
        manifest.Assets.Select(static asset => asset.File).ShouldBeUnique();
        var expectedCaptureCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["capture/screenshots.lua"] = 48,
            ["capture/home-scroll.lua"] = 4,
            ["capture/guessing-workflow.lua"] = 4,
            ["capture/v0.5-guides.lua"] = 12,
            ["capture/community-guides.lua"] = 12,
            ["capture/v0.6-overlay-guides.lua"] = 28,
        };
        var captureCounts = manifest
            .Assets.GroupBy(static asset => asset.Capture, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.Ordinal
            );
        captureCounts.Count.ShouldBe(expectedCaptureCounts.Count);
        foreach (var expected in expectedCaptureCounts)
        {
            captureCounts.ShouldContainKey(expected.Key);
            captureCounts[expected.Key].ShouldBe(expected.Value);
        }

        var overlayFiles = manifest
            .Assets.Where(static asset => asset.Capture == "capture/v0.6-overlay-guides.lua")
            .Select(static asset => asset.File)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedOverlayFiles = (
            from device in new[] { "laptop", "phone" }
            from theme in new[] { "dark", "light" }
            from view in new[]
            {
                "cues",
                "event-feed",
                "giveaway",
                "guessing",
                "media",
                "sources",
                "viewer-queue",
            }
            select $"{device}-{theme}-overlay-{view}.png"
        )
            .Order(StringComparer.Ordinal)
            .ToArray();
        overlayFiles.ShouldBe(expectedOverlayFiles);
        manifest.Assets.ShouldNotContain(static asset =>
            asset.Capture == "capture/twitch-operations.lua"
        );

        var mediaInventory = Directory
            .EnumerateFiles(_mediaRoot, "*", SearchOption.AllDirectories)
            .Where(static path => Path.GetExtension(path) is ".png" or ".webp")
            .Select(static path =>
                Path.GetRelativePath(_mediaRoot, path).Replace(Path.DirectorySeparatorChar, '/')
            )
            .Order(StringComparer.Ordinal)
            .ToArray();
        manifest
            .Assets.Select(static asset => asset.File)
            .Order(StringComparer.Ordinal)
            .ShouldBe(mediaInventory);

        var generatedSiteReferences = SiteGuideCatalog
            .All.SelectMany(static page =>
                OptionalSources(page.Media)
                    .Concat(
                        page.Sections.SelectMany(static section => OptionalSources(section.Media))
                    )
            )
            .Append("media/phone-light-home-scroll.webp")
            .Append("media/phone-dark-home-scroll.webp")
            .Append("media/laptop-light-home-scroll.webp")
            .Append("media/laptop-dark-home-scroll.webp")
            .Select(static source => source["media/".Length..])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        generatedSiteReferences.ShouldBe(mediaInventory);

        foreach (var asset in manifest.Assets)
        {
            asset.Capture.ShouldStartWith("capture/");
            File.Exists(Path.Combine(_repositoryRoot, asset.Capture)).ShouldBeTrue();
            asset.Route.ShouldStartWith("/");
            asset.SimulationAlias.ShouldStartWith("/simulation/login?view=");
            asset.Scenario.ShouldStartWith("fresh-simulation-process:ready-fixture:");
            asset.Theme.ShouldBeOneOf("light", "dark");
            asset.Device.ShouldBeOneOf("laptop", "phone");
            asset.SemanticReadiness.ShouldNotBeNullOrWhiteSpace();
            var expectedDimensions = asset.Device == "laptop" ? (1308, 840) : (462, 956);
            (asset.Width, asset.Height).ShouldBe(expectedDimensions, asset.File);

            var dimensions = ReadDimensions(Path.Combine(_mediaRoot, asset.File));
            dimensions.Width.ShouldBe(asset.Width, asset.File);
            dimensions.Height.ShouldBe(asset.Height, asset.File);
        }
    }

    [Test]
    public async Task GuideRoutes_RenderUsableFeatureSidebarAndCurrentGuidance()
    {
        await using var app = SiteApplication.Build([
            "--urls=http://127.0.0.1:0",
            .. SiteTestConfiguration.PrivacyArguments,
        ]);

        try
        {
            await app.StartAsync();
            var address = app
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            foreach (var route in SiteRoutes.GuideTopics)
            {
                var response = await client.GetAsync(route);
                response.StatusCode.ShouldBe(HttpStatusCode.OK, route);
                var content = await response.Content.ReadAsStringAsync();
                content.ShouldContain("aria-label=\"Guide features\"");
                content.ShouldContain("<details class=\"guide-sidebar__disclosure\" open>");
                content.ShouldContain("Browse help topics");
                content.ShouldContain("All help topics");
                content.ShouldContain("Community interaction");
                content.ShouldContain("Native Twitch");
                content.ShouldContain("Browser Sources");
                content.ShouldContain("Cues");
                content.ShouldContain("Media library");
                content.ShouldContain("Available viewer commands");
            }

            var overlays = await client.GetStringAsync("/overlays");
            overlays.ShouldContain("Current topic: <strong>Browser Sources</strong>");
            overlays.ShouldContain("private Browser Source URL");
            overlays.ShouldContain("set Width to 1920 and Height to 1080");
            overlays.ShouldContain("Preview is above configuration");
            overlays.ShouldContain("Shift plus an arrow for ten pixels");
            overlays.ShouldContain("Available selectors are .overlay, .card, .accent");
            overlays.ShouldContain("When there is no active giveaway");
            overlays.ShouldContain("renders nothing");
            overlays.ShouldContain("Joining its viewer page requires Twitch sign-in");
            overlays.ShouldContain("every configured field is optional and public");
            overlays.ShouldContain("does not show a wait estimate");
            overlays.ShouldContain("media/laptop-dark-overlay-sources.png");
            overlays.ShouldContain("media/phone-light-overlay-viewer-queue.png");
            overlays.ShouldNotContain("simulation-overlay-access-key");
            overlays.ShouldNotContain("Production preview");
            overlays.ShouldNotContain("Empty V1");
            overlays.ShouldNotContain("Cue-V1");
            overlays.ShouldNotContain("diagnostic presence");
            overlays.ShouldNotContain("live client");
            overlays.ShouldContain("id=\"cues\"");
            overlays.ShouldContain("uploaded media, online media and web pages");
            overlays.ShouldContain("Play after the current cue");
            overlays.ShouldContain("Play an overlay cue");
            overlays.ShouldContain("main command word");
            overlays.ShouldContain("media/laptop-dark-overlay-cues.png");
            overlays.ShouldContain("id=\"media\"");
            overlays.ShouldContain("image, audio or video file");
            overlays.ShouldContain("private channel storage");
            overlays.ShouldContain("Replace file");
            overlays.ShouldContain("media/phone-light-overlay-media.png");
            overlays.ShouldContain("/overlays#cues");
            overlays.ShouldContain("/overlays#media");
            overlays.ShouldNotContain("overlays/cues");
            overlays.ShouldNotContain("overlays/media");

            foreach (
                var source in SiteGuideCatalog
                    .All.Where(static page =>
                        page.Route.StartsWith("/overlays", StringComparison.Ordinal)
                    )
                    .SelectMany(static page =>
                        OptionalSources(page.Media)
                            .Concat(
                                page.Sections.SelectMany(static section =>
                                    OptionalSources(section.Media)
                                )
                            )
                    )
                    .Distinct(StringComparer.Ordinal)
            )
            {
                var response = await client.GetAsync($"/{source}");
                response.StatusCode.ShouldBe(HttpStatusCode.OK, source);
                response.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
            }

            var tools = await client.GetStringAsync("/tools");
            tools.ShouldContain("all thirteen Chat Tools features disabled");
            tools.ShouldContain("Channels migrated from an earlier BlokeBot release");
            tools.ShouldContain("Disabling pauses the feature");
            tools.ShouldContain("does not replay commands");
            tools.ShouldContain("media/laptop-dark-chat-tools-all-disabled.png");
            tools.ShouldContain("media/laptop-dark-chat-tools-enabled.png");
            tools.ShouldContain("shared 12px");

            var connections = await client.GetStringAsync("/connect");
            connections.ShouldContain("Connect Chat access");
            connections.ShouldContain("Connect the Twitch integration");
            connections.ShouldContain(
                "Disconnect removes BlokeBot&#x27;s saved authorization for this channel"
            );
            connections.ShouldContain(
                "Chat access, Twitch integration and bot-account connections are different approvals"
            );
            connections.ShouldNotContain("Twitch operations");

            var requestBoards = await client.GetStringAsync("/community/request-boards");
            requestBoards.ShouldContain("Current topic: <strong>Request boards</strong>");
            requestBoards.ShouldContain("/requests/{channel}/{board-name}");
            requestBoards.ShouldContain(
                "!request &lt;board&gt; &lt;title&gt; | field=value | category=value | tags=a,b"
            );
            requestBoards.ShouldContain(
                "Text, Link, Choose from a list, Number or Twitch clip link"
            );
            requestBoards.ShouldContain("Awaiting review");
            requestBoards.ShouldContain("Merged into another request");
            requestBoards.ShouldContain("Never refund");
            requestBoards.ShouldContain("Points held");
            requestBoards.ShouldContain("request number");
            requestBoards.ShouldContain("media/community/laptop-dark-request-boards.png");

            var playWithViewers = await client.GetStringAsync("/community/play-with-viewers");
            playWithViewers.ShouldContain("Current topic: <strong>Play with viewers</strong>");
            playWithViewers.ShouldContain("/queues/{channel}/{queue-name}");
            playWithViewers.ShouldContain("!ready [queue]");
            playWithViewers.ShouldContain("there is no unsigned typed-login fallback");
            playWithViewers.ShouldContain("First to join");
            playWithViewers.ShouldContain("Viewers who played least recently");
            playWithViewers.ShouldContain("Every configured field is optional and public");
            playWithViewers.ShouldContain("Did not respond");
            playWithViewers.ShouldContain("Lobby messages and moderator notes remain private");
            playWithViewers.ShouldContain("media/community/phone-light-play-with-viewers.png");

            var moments = await client.GetStringAsync("/community/moments");
            moments.ShouldContain("Current topic: <strong>Moments</strong>");
            moments.ShouldContain("/moments/{channel}/streams/{stream-id}");
            moments.ShouldContain("!moment &lt;suggested title&gt;");
            moments.ShouldContain("No reward, First viewer to request or All contributing viewers");
            moments.ShouldContain(
                "Creating clip, Clip ready, Marker ready or Could not create clip"
            );
            moments.ShouldContain("Save details");
            moments.ShouldContain("moment number");
            moments.ShouldContain("does not copy or host the clip or VOD");
            moments.ShouldContain("media/community/phone-light-moments.png");

            foreach (
                var source in SiteGuideCatalog
                    .All.Where(static page =>
                        page.Route.StartsWith("/community/", StringComparison.Ordinal)
                    )
                    .SelectMany(static page => Sources(page.Media!))
                    .Distinct(StringComparer.Ordinal)
            )
            {
                var response = await client.GetAsync($"/{source}");
                response.StatusCode.ShouldBe(HttpStatusCode.OK, source);
                response.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
            }

            var overview = await client.GetStringAsync("/twitch-operations");
            overview.ShouldContain("Open Chat tools, turn on Native Twitch and save the change.");
            overview.ShouldContain("Use the ? button beside a page title");
            overview.ShouldNotContain("channel:manage:");
            overview.ShouldNotContain("moderator:manage:");

            var shoutouts = await client.GetStringAsync("/twitch-operations/shoutouts");
            shoutouts.ShouldContain("Automatic raid shoutouts are off by default");
            shoutouts.ShouldContain("Regular, Pinned or Announcement");
            shoutouts.ShouldContain("Default, Blue, Green, Orange or Purple");
            shoutouts.ShouldContain("{twitch_handle}");
            shoutouts.ShouldContain("There is no retry or fallback action for an earlier raid.");
            shoutouts.ShouldContain("Current topic: <strong>Shoutouts</strong>");
            shoutouts.ShouldContain(
                "BlokeBot Shoutouts page on a phone showing a Twitch channel name field and the Send shoutout action."
            );
            var shoutoutsMedia = SiteGuideCatalog.Get("/twitch-operations/shoutouts").Media!;
            shoutoutsMedia.PhoneAlt.ShouldBe(
                "BlokeBot Shoutouts page on a phone showing a Twitch channel name field and the Send shoutout action."
            );
            shoutoutsMedia.LaptopAlt.ShouldBe(
                "BlokeBot Shoutouts page showing the manual target and automatic raid shoutout settings."
            );

            var clips = await client.GetStringAsync("/twitch-operations/clips-markers");
            clips.ShouldContain("include the stream delay");
            clips.ShouldContain("short description");
            clips.ShouldNotContain("request key");
            clips.ShouldNotContain("idempotency");

            var commandCatalog = await client.GetStringAsync("/commands/catalog");
            commandCatalog.ShouldContain(
                "Current topic: <strong>Available viewer commands</strong>"
            );
            commandCatalog.ShouldContain("The default is commands");
            commandCatalog.ShouldContain("starts collapsed");
            commandCatalog.ShouldContain("only the first command word");
            commandCatalog.ShouldContain("main command names");
            commandCatalog.ShouldContain("Moderator-only commands");
            commandCatalog.ShouldContain("Moment and clip commands depend on live-stream identity");
            commandCatalog.ShouldContain("splits the list across multiple ordinary replies");
            commandCatalog.ShouldContain("media/phone-light-viewer-command-catalog.png");
        }
        finally
        {
            await app.StopAsync();
            await Log.CloseAndFlushAsync();
        }
    }

    [Test]
    public void GuideLinks_ResolveToRegisteredSiteRoutes()
    {
        var registered = SiteRoutes.All.ToHashSet(StringComparer.Ordinal);
        var links = SiteGuideCatalog
            .All.SelectMany(static page =>
                page.Sections.SelectMany(static section => section.Links).Concat(page.Next)
            )
            .Concat(SiteGuideCatalog.NavigationGroups.SelectMany(static group => group.Links));

        foreach (
            var link in links.Where(static link =>
                !Uri.IsWellFormedUriString(link.Href, UriKind.Absolute)
            )
        )
        {
            registered.ShouldContain($"/{link.Href.Split('#')[0].TrimStart('/')}", link.Label);
        }
    }

    private static MediaCaptureManifest LoadManifest()
    {
        var path = Path.Combine(_repositoryRoot, "capture", "media-manifest.json");
        return JsonSerializer.Deserialize<MediaCaptureManifest>(
                File.ReadAllText(path),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            ) ?? throw new InvalidOperationException("The media manifest could not be read.");
    }

    private static IEnumerable<string> Sources(SiteMedia media)
    {
        yield return media.DarkPhoneSource;
        yield return media.LightPhoneSource;
        yield return media.DarkLaptopSource;
        yield return media.LightLaptopSource;
    }

    private static IEnumerable<string> OptionalSources(SiteMedia? media) =>
        media is null ? [] : Sources(media);

    private static (int Width, int Height) ReadDimensions(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Path.GetExtension(path) switch
        {
            ".png" => ReadPngDimensions(bytes),
            ".webp" => ReadAnimatedWebpDimensions(bytes),
            _ => throw new InvalidOperationException($"Unsupported media file: {path}"),
        };
    }

    private static (int Width, int Height) ReadPngDimensions(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        bytes.AsSpan(0, signature.Length).SequenceEqual(signature).ShouldBeTrue();
        Encoding.ASCII.GetString(bytes, 12, 4).ShouldBe("IHDR");
        return (
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4))
        );
    }

    private static (int Width, int Height) ReadAnimatedWebpDimensions(byte[] bytes)
    {
        Encoding.ASCII.GetString(bytes, 0, 4).ShouldBe("RIFF");
        Encoding.ASCII.GetString(bytes, 8, 4).ShouldBe("WEBP");
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunk = Encoding.ASCII.GetString(bytes, offset, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            if (chunk == "VP8X")
            {
                var payload = offset + 8;
                var width = 1 + ReadUInt24LittleEndian(bytes.AsSpan(payload + 4, 3));
                var height = 1 + ReadUInt24LittleEndian(bytes.AsSpan(payload + 7, 3));
                return (width, height);
            }
            offset += 8 + size + (size & 1);
        }
        throw new InvalidOperationException("The animated WebP does not contain a VP8X chunk.");
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes) =>
        bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null && !File.Exists(Path.Combine(directory.FullName, "BlokeBot.slnx"))
        )
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be found.");
    }

    private sealed record MediaCaptureManifest(
        int Version,
        string Generator,
        string ProcessModel,
        IReadOnlyList<MediaCaptureAsset> Assets
    );

    private sealed record MediaCaptureAsset(
        string File,
        string Capture,
        string Route,
        string SimulationAlias,
        string Scenario,
        string Theme,
        string Device,
        string Format,
        int Width,
        int Height,
        string SemanticReadiness
    );
}
