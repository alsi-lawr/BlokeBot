using System.IO.Compression;
using System.Text.Json;
using BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task Transfer_CurrentMediaAndVerbatimWorkingSourceRoundtripWithFreshUnpublishedIdentityAndVisibleSetup()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var plugin = new WidgetPluginRig(fixture.HostId);
        PublishPortableWidget(plugin);
        await using var environment = new WidgetEnvironment(fixture, plugin);
        var transfer = Transfer(fixture, environment, plugin.Declarations);
        var uploaded = (
            await environment.Media.UploadAssetAsync(
                fixture.Owner,
                "Logo",
                "image/png",
                new MemoryStream([1, 2, 3]),
                _ct
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Succeeded>()
            .Value;
        _ = (
            await environment.Media.ReplaceAssetAsync(
                fixture.Owner,
                new(
                    uploaded.Id,
                    uploaded.ContentRevision,
                    "image/png",
                    new MemoryStream([7, 8, 9, 10])
                ),
                _ct
            )
        ).ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Succeeded>();
        var aliasId = Guid.NewGuid();
        await using (var db = fixture.Database.CreateDbContext())
        {
            var source = await db.OverlayMediaAssets.SingleAsync(asset =>
                asset.PublicId == uploaded.Id
            );
            _ = db.OverlayMediaAssets.Add(
                new()
                {
                    PublicId = aliasId,
                    HostId = fixture.HostId,
                    Name = "Logo alias",
                    DocumentId = source.DocumentId,
                    ContentRevision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var image = environment.Registry.Create(new("image"), Guid.NewGuid())! with
        {
            Configuration = JsonSerializer.SerializeToElement(
                new FullOverlayMediaConfiguration(uploaded.Id, true)
            ),
            Audio = new(true, .37),
        };
        var alias = image with
        {
            Id = new(Guid.NewGuid()),
            Configuration = JsonSerializer.SerializeToElement(
                new FullOverlayMediaConfiguration(aliasId, false)
            ),
            Audio = new(false, .62),
        };
        var display = environment.Registry.Create(
            new($"plugin:{plugin.Manifest.Manifest.Id.Value}/display"),
            Guid.NewGuid()
        )! with
        {
            Configuration = JsonSerializer.SerializeToElement(
                new
                {
                    text = "Portable title",
                    nested = new { token = "declared-portable", items = new[] { 1, 2 } },
                    destination = "source-private-id",
                    secret = "installation-private",
                    legacy = "undeclared-private",
                }
            ),
        };
        var unknown = display with
        {
            Id = new(Guid.NewGuid()),
            Kind = new("plugin:absent/widget"),
            Configuration = JsonSerializer.SerializeToElement(new { unknown = "unknown-private" }),
        };
        var queue = environment.Registry.Create(new("viewer-queue"), Guid.NewGuid())! with
        {
            Configuration = Config(
                new OverlayConfiguration.ViewerQueueV1(
                    91,
                    2,
                    3,
                    new(0, 0, 500, 250, ".card{opacity:.4}")
                )
            ),
        };
        var goal = environment.Registry.Create(new("community-goal"), Guid.NewGuid())! with
        {
            Configuration = Config(new OverlayConfiguration.CommunityGoalV1(Guid.NewGuid(), 12, 3)),
        };
        var feed = environment.Registry.Create(new("event-feed"), Guid.NewGuid())!;
        var feedCopy = feed with { Id = new(Guid.NewGuid()), Audio = new(true, .2) };
        var widgets = new[] { image, alias, display, unknown, queue, goal, feed, feedCopy };
        var html =
            "<!-- KEEP EXACT -->\n<custom-card strange='yes'>Authored token=author-responsibility</custom-card><script>window.x='https://remote.test/?token=authored';</script>\n"
            + string.Join(
                "",
                widgets.Select(widget =>
                    $"<section data-blokebot-widget='{widget.Id.Value}'></section>"
                )
            );
        var candidate = Document(
            html,
            "@layer strange { custom-card { --future: 7; color: color(display-p3 1 0 0); } }\n/* EXACT */"
        ) with
        {
            Widgets = [.. widgets],
        };
        var original = Value(
            await fixture.Service.CreateAsync(
                fixture.Owner,
                new("Original", candidate with { Html = "SAVED BASELINE" }),
                _ct
            )
        );
        candidate = candidate with { Id = original.Overlay.Id };
        var bytes = await Export(transfer, fixture.Owner, candidate);
        using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            using var stream = archive.GetEntry("document.json")!.Open();
            var json = await new StreamReader(stream).ReadToEndAsync();
            json.ShouldContain("Portable title");
            json.ShouldContain("declared-portable");
            foreach (
                var privateValue in new[]
                {
                    "source-private-id",
                    "installation-private",
                    "undeclared-private",
                    "unknown-private",
                    original.PrivateAccess.AccessKey,
                }
            )
            {
                json.ShouldNotContain(privateValue);
            }
        }
        var imported = Value(
            await transfer.ImportAsync(Session(fixture.OtherHostId), new MemoryStream(bytes), _ct)
        ).Created;
        imported.Overlay.PublishedVersion.ShouldBeNull();
        imported.Overlay.Revision.Value.ShouldBe(1);
        imported.Overlay.Id.ShouldNotBe(original.Overlay.Id);
        imported.PrivateAccess.AccessKey.ShouldNotBe(original.PrivateAccess.AccessKey);
        var reopened = Value(
            await fixture
                .NewService(fixture.Admission)
                .GetAsync(Session(fixture.OtherHostId), imported.Overlay.Id, _ct)
        );
        reopened.Draft.Html.ShouldBe(html);
        reopened.Draft.Css.ShouldBe(candidate.Css);
        reopened
            .Draft.Widgets.Select(widget => widget.Id)
            .ShouldBe(widgets.Select(widget => widget.Id));
        reopened
            .Draft.Widgets.Select(widget => widget.Audio)
            .ShouldBe(widgets.Select(widget => widget.Audio));
        reopened
            .Draft.Widgets.Select(widget => widget.Authoring)
            .ShouldBe(widgets.Select(widget => widget.Authoring));
        reopened
            .Draft.Widgets[2]
            .Configuration.GetProperty("nested")
            .GetProperty("token")
            .GetString()
            .ShouldBe("declared-portable");
        reopened.Draft.Widgets[4].Configuration.GetProperty("queueId").GetInt32().ShouldBe(0);
        reopened
            .Draft.Widgets[4]
            .Configuration.GetProperty("appearance")
            .GetProperty("css")
            .GetString()
            .ShouldBe(".card{opacity:.4}");
        reopened
            .Draft.Widgets[5]
            .Configuration.GetProperty("selectedItemId")
            .ValueKind.ShouldBe(JsonValueKind.Null);
        reopened
            .Draft.Widgets[6]
            .Configuration.GetRawText()
            .ShouldBe(reopened.Draft.Widgets[7].Configuration.GetRawText());
        reopened.Draft.Widgets[2].RequiresSetup.ShouldBeTrue();
        reopened.Draft.Widgets[3].RequiresSetup.ShouldBeTrue();
        var mediaId = reopened.Draft.Widgets[0].Configuration.GetProperty("assetId").GetGuid();
        var aliasMediaId = reopened.Draft.Widgets[1].Configuration.GetProperty("assetId").GetGuid();
        mediaId.ShouldNotBe(uploaded.Id);
        aliasMediaId.ShouldNotBe(mediaId);
        var content = (
            await environment.Media.ResolveContentAsync(fixture.OtherHostId, mediaId, 1, _ct)
        )!;
        (await File.ReadAllBytesAsync(content.Path)).ShouldBe([7, 8, 9, 10]);
        await using (var db = fixture.Database.CreateDbContext())
        {
            var rows = await db
                .OverlayMediaAssets.Where(asset => asset.HostId == fixture.OtherHostId)
                .ToArrayAsync();
            rows.Select(asset => asset.DocumentId).Distinct().Count().ShouldBe(1);
        }
        var context = new FullOverlayRenderContext(
            fixture.OtherHostId,
            reopened.Id,
            null,
            null,
            reopened.Draft,
            FullOverlayDataMode.Sample,
            []
        );
        var projections = await environment.Registry.ProjectAsync(context, _ct);
        _ = projections[0].Output.ShouldBeOfType<FullOverlayWidgetOutput.Media>();
        projections[2]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>()
            .Diagnostic.Code.ShouldBe("destination-setup-required");
        FullOverlayEventFeeds.Bindings(reopened.Draft).ShouldBeEmpty();
        Value(await fixture.Service.GetAsync(fixture.Owner, original.Overlay.Id, _ct))
            .Draft.Html.ShouldBe("SAVED BASELINE");
    }

    private static FullOverlayTransferService Transfer(
        Fixture fixture,
        WidgetEnvironment environment,
        IPluginFeatureDeclarationProvider? declarations = null
    ) =>
        new(
            new(fixture.Database, fixture.Moderator),
            environment.Media,
            fixture.Service,
            new(declarations ?? new PluginFeatureDeclarationRegistry()),
            NullLogger<FullOverlayTransferService>.Instance
        );

    private static async Task<byte[]> Export(
        FullOverlayTransferService transfer,
        BlokeBot.Core.Auth.Sessions.AuthenticatedSession session,
        FullOverlayDocument document
    )
    {
        using var output = new MemoryStream();
        await using var prepared = Value(
            await transfer.PrepareExportAsync(session, "Transferred document", document, _ct)
        );
        await prepared.CopyToAsync(output, _ct);
        return output.ToArray();
    }

    private static void PublishPortableWidget(
        WidgetPluginRig plugin,
        PluginWidgetFieldPortability textPortability = PluginWidgetFieldPortability.Portable
    )
    {
        var original = plugin.Manifest.Manifest;
        var descriptor = original.Widgets[0] with
        {
            ConfigurationFields =
            [
                new("text", "Text", PluginValueKind.String, true) { Portability = textPortability },
                new("nested", "Nested customization", PluginValueKind.Map, false)
                {
                    Portability = PluginWidgetFieldPortability.Portable,
                },
                new("destination", "Destination", PluginValueKind.String, false)
                {
                    Portability = PluginWidgetFieldPortability.DestinationBinding,
                },
                new("secret", "Private", PluginValueKind.String, false)
                {
                    Portability = PluginWidgetFieldPortability.Withheld,
                },
                new("legacy", "Legacy", PluginValueKind.String, false),
            ],
        };
        var validated = PluginManifestValidator
            .Validate(
                original with
                {
                    Widgets = [descriptor],
                },
                PluginContractFixtures.CompatibleHost()
            )
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
            .Manifest;
        var serialized = PluginManifestToml.Serialize(validated);
        var parsed = PluginManifestToml
            .Validate(serialized, PluginContractFixtures.CompatibleHost())
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
            .Manifest;
        plugin.Declarations.Publish(parsed, plugin.State.Fence);
    }
}
