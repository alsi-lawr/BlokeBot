using System.Net;
using BlokeBot.Core.Features.Overlays;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class OverlayCueConfigurationTests
{
    private const string _valid =
        """{"schemaVersion":1,"layers":[{"type":"uploadedMedia","assetId":"7a90d36d-e77c-496b-9211-d0547759050f","mediaKind":"video","startOffsetMilliseconds":100,"durationMilliseconds":900,"zIndex":2,"volume":0.75,"fit":"cover","rectangle":{"xPercent":10,"yPercent":15,"widthPercent":80,"heightPercent":70}},{"type":"remoteMedia","url":"https://cdn.example.test/alert.mp3","mediaKind":"audio","startOffsetMilliseconds":0,"durationMilliseconds":1000,"zIndex":1,"volume":0.5,"fit":"contain","rectangle":{"xPercent":0,"yPercent":0,"widthPercent":100,"heightPercent":100}},{"type":"externalWeb","url":"https://widgets.example.test/celebrate","startOffsetMilliseconds":0,"durationMilliseconds":1000,"zIndex":3,"rectangle":{"xPercent":20,"yPercent":20,"widthPercent":60,"heightPercent":60}}]}""";

    [Test]
    public void CueV1_RoundTripsStrictTypedLayersAndAssetReferences()
    {
        var parsed = OverlayCueConfiguration
            .Parse(_valid)
            .ShouldBeOfType<OverlayCueConfigurationResult.Valid>()
            .Value;

        parsed.SchemaVersion.ShouldBe(1);
        parsed.Layers.Length.ShouldBe(3);
        parsed
            .Layers[0]
            .ShouldBeOfType<OverlayCueLayer.UploadedMedia>()
            .Fit.ShouldBe(OverlayCueFitMode.Cover);
        parsed.ReferencedAssetIds.ShouldBe([Guid.Parse("7a90d36d-e77c-496b-9211-d0547759050f")]);

        var roundTrip = OverlayCueConfiguration
            .Parse(parsed.ToPersistenceJson())
            .ShouldBeOfType<OverlayCueConfigurationResult.Valid>()
            .Value;
        roundTrip.ToPersistenceJson().ShouldBe(parsed.ToPersistenceJson());
        roundTrip.Layers.ShouldBe(parsed.Layers);
    }

    [Test]
    public void TypedFactory_RoundTripsEveryEditableContentKindInOrder()
    {
        var parsed = OverlayCueConfiguration
            .Parse(_valid)
            .ShouldBeOfType<OverlayCueConfigurationResult.Valid>()
            .Value;

        var created = OverlayCueConfiguration
            .Create(parsed.Layers.Reverse().ToArray())
            .ShouldBeOfType<OverlayCueConfigurationResult.Valid>()
            .Value;

        created
            .Layers.Select(layer => layer.GetType())
            .ShouldBe(parsed.Layers.Reverse().Select(layer => layer.GetType()));
        created
            .Layers.Select(layer => layer.ZIndex)
            .ShouldBe(parsed.Layers.Reverse().Select(layer => layer.ZIndex));
        OverlayCueConfiguration
            .Parse(created.ToPersistenceJson())
            .ShouldBeOfType<OverlayCueConfigurationResult.Valid>()
            .Value.Layers.ShouldBe(created.Layers);
    }

    [Test]
    [Arguments(
        """{"schemaVersion":1,"layers":[{"type":"externalWeb","url":"javascript:alert(1)","startOffsetMilliseconds":0,"durationMilliseconds":1000,"zIndex":0,"rectangle":{"xPercent":0,"yPercent":0,"widthPercent":100,"heightPercent":100}}]}"""
    )]
    [Arguments(
        """{"schemaVersion":1,"layers":[{"type":"externalWeb","url":"data:text/html,no","startOffsetMilliseconds":0,"durationMilliseconds":1000,"zIndex":0,"rectangle":{"xPercent":0,"yPercent":0,"widthPercent":100,"heightPercent":100}}]}"""
    )]
    [Arguments(
        """{"schemaVersion":1,"layers":[{"type":"externalWeb","url":"https://example.test/","startOffsetMilliseconds":0,"durationMilliseconds":1000,"zIndex":0,"rectangle":{"xPercent":0,"yPercent":0,"widthPercent":100,"heightPercent":100},"unknown":true}]}"""
    )]
    [Arguments("""{"schemaVersion":2,"layers":[]}""")]
    public void CueV1_RejectsActiveSchemesUnknownFieldsAndVersions(string json) =>
        OverlayCueConfiguration.Parse(json).ShouldBeOfType<OverlayCueConfigurationResult.Invalid>();

    [Test]
    public async Task RemotePolicy_RejectsPrivateDnsUnlessOwnerExplicitlyOptsIn()
    {
        var dns = new FixedDnsResolver(IPAddress.Parse("10.1.2.3"));
        var safe = new OverlayRemoteUrlPolicy(dns, Options.Create(new BlokeBotOptions()));
        var optedIn = new OverlayRemoteUrlPolicy(
            dns,
            Options.Create(
                new BlokeBotOptions
                {
                    Overlays = new() { Media = new() { AllowPrivateNetworkTargets = true } },
                }
            )
        );
        var url = new Uri("https://internal.example.test/widget");

        _ = (
            await safe.ValidateAsync(url, CancellationToken.None)
        ).ShouldBeOfType<OverlayRemoteUrlDecision.Rejected>();
        _ = (
            await optedIn.ValidateAsync(url, CancellationToken.None)
        ).ShouldBeOfType<OverlayRemoteUrlDecision.Allowed>();
        dns.Requests.ShouldBe(["internal.example.test", "internal.example.test"]);
    }

    [Test]
    public void BrowserClient_UsesDirectMediaAndRestrictiveExternalFrameBoundaries()
    {
        OverlayBrowserSourceAssets.JavaScript.ShouldContain(
            "element.setAttribute(\"sandbox\", \"allow-scripts\")"
        );
        OverlayBrowserSourceAssets.JavaScript.ShouldNotContain("allow-same-origin");
        OverlayBrowserSourceAssets.JavaScript.ShouldNotContain("allow-top-navigation");
        OverlayBrowserSourceAssets.JavaScript.ShouldNotContain("allow-popups");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("root.dataset.mediaUrl");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain(
            "layer.mediaKind === \"image\" ? \"img\" : layer.mediaKind"
        );
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("() => element.remove()");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("layer.durationMilliseconds");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain(
            "Server-side expiry still advances the transient queue."
        );
    }

    [Test]
    [Arguments("image/png", "image")]
    [Arguments("video/webm", "video")]
    public void UploadedBrowserMedia_ReachesTheCueRendererWithItsMediaKind(
        string contentType,
        string expectedKind
    )
    {
        var payload = OverlayLiveCoordinator.ToPayload(
            new OverlayCuePlaybackLayer.UploadedMedia
            {
                AssetId = Guid.NewGuid(),
                ContentRevision = 1,
                ContentType = contentType,
                Volume = 1,
                Fit = OverlayCueFitMode.Contain,
                Rectangle = new(0, 0, 100, 100),
                StartOffsetMilliseconds = 0,
                DurationMilliseconds = 1000,
                ZIndex = 0,
            }
        );

        payload.MediaKind.ShouldBe(expectedKind);
        payload.DurationMilliseconds.ShouldBe(1000);
    }

    private sealed class FixedDnsResolver(params IPAddress[] addresses) : IOverlayDnsResolver
    {
        internal List<string> Requests { get; } = [];

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(host);
            return Task.FromResult<IReadOnlyList<IPAddress>>(addresses);
        }
    }
}
