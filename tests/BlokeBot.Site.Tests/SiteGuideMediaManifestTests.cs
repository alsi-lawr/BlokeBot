using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using BlokeBot.Site;
using BlokeBot.Site.Content;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Shouldly;
using TUnit.Core;

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
        manifest.Assets.Count.ShouldBe(52);
        manifest.Assets.Count(asset => asset.Format == "png").ShouldBe(44);
        manifest.Assets.Count(asset => asset.Format == "webp").ShouldBe(8);
        manifest.Assets.Select(asset => asset.File).ShouldBeUnique();

        var mediaInventory = Directory
            .EnumerateFiles(_mediaRoot)
            .Where(path => Path.GetExtension(path) is ".png" or ".webp")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        manifest
            .Assets.Select(asset => asset.File)
            .Order(StringComparer.Ordinal)
            .ShouldBe(mediaInventory);

        var siteReferences = SiteGuideCatalog
            .All.Where(page => page.Media is not null)
            .SelectMany(page => Sources(page.Media!))
            .Append("media/phone-light-home-scroll.webp")
            .Append("media/phone-dark-home-scroll.webp")
            .Append("media/laptop-light-home-scroll.webp")
            .Append("media/laptop-dark-home-scroll.webp")
            .Select(source => source["media/".Length..])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        siteReferences.ShouldBe(mediaInventory);

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

            var dimensions = ReadDimensions(Path.Combine(_mediaRoot, asset.File));
            dimensions.Width.ShouldBe(asset.Width, asset.File);
            dimensions.Height.ShouldBe(asset.Height, asset.File);
        }
    }

    [Test]
    public async Task GuideRoutes_RenderUsableFeatureSidebarAndCurrentNativeGuidance()
    {
        await using var app = SiteApplication.Build(["--urls=http://127.0.0.1:0"]);

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
                content.ShouldContain("All help topics");
                content.ShouldContain("Native Twitch");
            }

            var overview = await client.GetStringAsync("/twitch-operations");
            overview.ShouldContain("Open Chat tools, turn on Native Twitch and save the change.");
            overview.ShouldContain("Use the ? button beside a page title");
            overview.ShouldNotContain("channel:manage:");
            overview.ShouldNotContain("moderator:manage:");

            var shoutouts = await client.GetStringAsync("/twitch-operations/shoutouts");
            shoutouts.ShouldContain("Automatic raid shoutouts are off by default");
            shoutouts.ShouldContain("Regular, Pinned or Announcement");
            shoutouts.ShouldContain("{twitch_handle}");
            shoutouts.ShouldContain("There is no retry or fallback action for an earlier raid.");

            var clips = await client.GetStringAsync("/twitch-operations/clips-markers");
            clips.ShouldContain("include the stream delay");
            clips.ShouldContain("short description");
            clips.ShouldNotContain("request key");
            clips.ShouldNotContain("idempotency");
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
            .All.SelectMany(page =>
                page.Sections.SelectMany(section => section.Links).Concat(page.Next)
            )
            .Concat(SiteGuideCatalog.NavigationGroups.SelectMany(group => group.Links));

        foreach (
            var link in links.Where(link => !Uri.IsWellFormedUriString(link.Href, UriKind.Absolute))
        )
        {
            registered.ShouldContain($"/{link.Href.TrimStart('/')}", link.Label);
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

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes)
    {
        return bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
    }

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
