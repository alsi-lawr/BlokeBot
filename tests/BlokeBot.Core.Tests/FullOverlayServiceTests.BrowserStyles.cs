using System.Collections.Immutable;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test, Explicit]
    public async Task BrowserDelivery_RepeatedNativeAppearanceStylesStayLocalAcrossPublicAndFrozenPreviewChanges()
    {
        var driver =
            Environment.GetEnvironmentVariable("BLOKEBOT_FULL_STYLE_DRIVER")
            ?? throw new InvalidOperationException(
                "Set BLOKEBOT_FULL_STYLE_DRIVER to the owned browser driver."
            );
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        await using var runtime = new DeliveryRuntime(fixture, environment);
        var first = environment.Registry.Create(new("giveaway"), Guid.NewGuid())!;
        var second = first with { Id = new(Guid.NewGuid()) };
        var document = Document(
            $"""
            <!doctype html><html><body>
            <h1 id="raw-sentinel">AUTHORED DOCUMENT</h1>
            <svg><rect class="card" id="outside-native" width="10" height="10"/></svg>
            <section style="height:270px" data-blokebot-widget="{first.Id.Value}"></section>
            <section style="height:270px" data-blokebot-widget="{second.Id.Value}"></section>
            <script>window.authoredStyleScript = true;</script>
            </body></html>
            """,
            "#raw-sentinel{color:rgb(19,23,29)} .card{opacity:.91}"
        ) with
        {
            Widgets = [first, second],
        };
        document = StyledDocument(document, [".card{opacity:.31}", ".card{opacity:.73}"]);
        var created = Value(
            await fixture.Service.CreateAsync(fixture.Owner, new("Native styles", document), _ct)
        );
        var current = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                new(
                    created.Overlay.Id,
                    created.Overlay.Revision,
                    "Native styles",
                    created.Overlay.Draft
                ),
                _ct
            )
        ).Overlay;
        var simpleKey = new CryptographicOverlayAccessKeyGenerator().Generate();
        await using (var db = fixture.Database.CreateDbContext())
        {
            _ = db.PointsGiveaways.Add(
                new()
                {
                    HostId = fixture.HostId,
                    Status = PointsGiveawayStatus.Active,
                    StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                    EndsAtUtc = DateTime.UtcNow.AddMinutes(5),
                }
            );
            _ = db.OverlayInstances.Add(
                new()
                {
                    PublicId = Guid.NewGuid(),
                    HostId = fixture.HostId,
                    Name = "Simple style regression",
                    Type = OverlayType.Giveaway,
                    IsEnabled = true,
                    ConfigurationJson = SourceStyle(".card{opacity:.42}").ToPersistenceJson(),
                    AccessKeyDigest = OverlayAccessKeyDigest.Compute(simpleKey),
                    KeyVersion = 1,
                    Revision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        await using var app = BrowserApp(fixture, environment, runtime);
        _ = app.MapGet(
            "/fixture",
            () =>
                Results.Json(
                    new
                    {
                        relativeUrl = created.PrivateAccess.RelativeUrl,
                        simpleUrl = $"/overlay/{simpleKey}",
                        document = current.Draft,
                        widgetIds = new[] { first.Id.Value, second.Id.Value },
                    }
                )
        );
        _ = app.MapGet(
                "/fixture/observe",
                () =>
                    Results.Json(
                        new
                        {
                            presence = runtime
                                .Live.Read(fixture.HostId, current.Id)
                                .ActiveConnectionCount,
                            html = current.Draft.Html,
                        }
                    )
            )
            .RequireAuthorization();
        _ = app.MapPost(
                "/fixture/publish",
                async (SourceStyleChange change) =>
                {
                    var changed = StyledDocument(current.Draft, change.Css);
                    if (change.RemoveFirst)
                    {
                        changed = changed with { Widgets = [.. changed.Widgets.Skip(1)] };
                    }
                    current = Value(
                        await fixture.Service.SaveAndPublishAsync(
                            fixture.Owner,
                            new(current.Id, current.Revision, current.Name, changed),
                            _ct
                        )
                    ).Overlay;
                    return Results.Ok();
                }
            )
            .RequireAuthorization();
        _ = app.MapPost(
                "/fixture/simple",
                async (SourceStyleChange change) =>
                {
                    await using var db = fixture.Database.CreateDbContext();
                    var simple = await db.OverlayInstances.SingleAsync();
                    simple.ConfigurationJson = SourceStyle(change.Css[0]).ToPersistenceJson();
                    simple.Revision++;
                    _ = await db.SaveChangesAsync();
                    _ = await fixture.Events.PublishAsync(AppEventKind.OverlaysChanged, _ct);
                    return Results.Ok();
                }
            )
            .RequireAuthorization();
        await runtime.Live.StartAsync(_ct);
        await RunBrowserAsync(app, driver);
    }

    private sealed record SourceStyleChange(ImmutableArray<string> Css, bool RemoveFirst = false);

    private static OverlayConfiguration.GiveawayV1 SourceStyle(string css) =>
        new("PUBLIC GIVEAWAY TITLE", true, true, true, new(160, 690, 1600, 270, css));

    private static FullOverlayDocument StyledDocument(
        FullOverlayDocument document,
        ImmutableArray<string> css
    ) =>
        document with
        {
            Widgets =
            [
                .. document.Widgets.Select(
                    (widget, index) =>
                        widget with
                        {
                            Configuration = Config(SourceStyle(css[index])),
                        }
                ),
            ],
        };
}
