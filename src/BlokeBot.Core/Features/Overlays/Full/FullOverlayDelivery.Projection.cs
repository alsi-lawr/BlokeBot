using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed partial class FullOverlayDelivery
{
    internal async Task<FullOverlayRenderFrame?> ProjectAsync(
        FullOverlayRenderLease lease,
        CancellationToken ct
    )
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            lease.Cancellation
        );
        ct = lifetime.Token;
        if (!await RefreshAuthorityAsync(lease, ct))
        {
            return null;
        }
        var context = Context(lease);
        if (
            lease.Preview is { Mode: FullOverlayDataMode.Sample }
            && OverlayCuePlaybackService.HasCuePlayer(context.Document)
        )
        {
            context = context with
            {
                CuePlans = await cues.ProjectSamplePlansAsync(
                    context.HostId,
                    context.OverlayId,
                    ct
                ),
            };
        }
        if (lease.Preview is { Mode: FullOverlayDataMode.Live })
        {
            var selected = await publications.ResolveAsync(lease.HostId, lease.OverlayId, ct);
            context = context with
            {
                CuePlans = selected is null ? [] : cues.ProjectFullPlans(selected),
            };
        }
        var projected = await widgets.ProjectAsync(context, ct);
        var frames = ImmutableArray.CreateBuilder<FullOverlayWidgetFrame>();
        var declaredPlugins = ImmutableDictionary.CreateBuilder<Guid, FullOverlayPluginResource>();
        foreach (var widget in projected)
        {
            try
            {
                frames.Add(
                    await widget.Output.Match<Task<FullOverlayWidgetFrame>>(
                        source =>
                            Task.FromResult(
                                FullOverlayPublicSnapshots.Source(
                                    widget.Id.Value,
                                    widget.Audio,
                                    source.Projection
                                )
                            ),
                        cue =>
                            Task.FromResult(
                                Frame(
                                    "cue",
                                    FullOverlayPublicSnapshots.Json(
                                        cue.Plans.Select(plan => new
                                        {
                                            overlayType = "cuePlayer",
                                            schemaVersion = 1,
                                            plan.RunId,
                                            plan.DurationMilliseconds,
                                            layers = plan.Layers.Select(
                                                OverlayLiveCoordinator.ToPayload
                                            ),
                                            mediaUrl = ResourceBase(lease) + "/media",
                                        })
                                    )
                                )
                            ),
                        html =>
                            Task.FromResult(
                                Frame(
                                    "html",
                                    FullOverlayPublicSnapshots.Json(
                                        new { html = html.Content, html.Css }
                                    )
                                )
                            ),
                        web =>
                            Task.FromResult(
                                Frame(
                                    "web",
                                    FullOverlayPublicSnapshots.Json(
                                        new { url = web.Url.AbsoluteUri }
                                    )
                                )
                            ),
                        asset =>
                            Task.FromResult(
                                Frame(
                                    "media",
                                    FullOverlayPublicSnapshots.Json(
                                        new
                                        {
                                            url = $"{ResourceBase(lease)}/media/{asset.AssetId}/{asset.ContentRevision}",
                                            asset.ContentType,
                                            asset.Loop,
                                        }
                                    )
                                )
                            ),
                        async plugin =>
                        {
                            var resource =
                                lease.DeclaredPlugins.TryGetValue(widget.Id.Value, out var previous)
                                && previous.Endpoint == plugin.Endpoint
                                    ? previous
                                    : new FullOverlayPluginResource(
                                        Guid.NewGuid(),
                                        plugin.Endpoint
                                    );
                            declaredPlugins[widget.Id.Value] = resource;
                            var path = plugin
                                .Endpoint.Declaration.Manifest.Assets.Single(asset =>
                                    asset.Id == plugin.Endpoint.Descriptor.DocumentAsset
                                )
                                .Path;
                            var content = await widgets.ResolvePluginAssetAsync(
                                context,
                                widget.Id,
                                plugin.Endpoint,
                                path,
                                ct
                            );
                            return content.Match(
                                _ =>
                                    Frame(
                                        "plugin",
                                        FullOverlayPublicSnapshots.Json(
                                            new
                                            {
                                                url = $"{ResourceBase(lease)}/plugin/{widget.Id.Value}/{resource.Id}/{EscapePath(path)}",
                                                state = FullOverlayPluginValues.ToJson(
                                                    plugin.State
                                                ),
                                            }
                                        )
                                    ),
                                _ =>
                                    Frame(
                                        "unavailable",
                                        FullOverlayPublicSnapshots.Json(
                                            new { code = "asset-unavailable" }
                                        )
                                    ),
                                _ =>
                                    Frame(
                                        "unavailable",
                                        FullOverlayPublicSnapshots.Json(
                                            new { code = "asset-too-large" }
                                        )
                                    )
                            );
                        },
                        unavailable =>
                            Task.FromResult(
                                Frame(
                                    "unavailable",
                                    FullOverlayPublicSnapshots.Json(
                                        new { unavailable.Diagnostic.Code }
                                    )
                                )
                            )
                    )
                );
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Full overlay widget {WidgetId} delivery failed with {FailureClassification} ({FailureType}).",
                    widget.Id,
                    "asset-unavailable",
                    exception.GetType().FullName
                );
                frames.Add(
                    new(
                        widget.Id.Value,
                        widget.Audio,
                        "unavailable",
                        FullOverlayPublicSnapshots.Json(new { code = "asset-unavailable" })
                    )
                );
            }
            FullOverlayWidgetFrame Frame(string kind, System.Text.Json.JsonElement content) =>
                new(widget.Id.Value, widget.Audio, kind, content);
        }
        if (!await ContextCurrentAsync(lease, context, ct))
        {
            return null;
        }
        lease.DeclaredPlugins = declaredPlugins.ToImmutable();
        lease.DeclaredMedia = projected
            .SelectMany(widget =>
                widget.Output.Match<IEnumerable<FullOverlayMediaReference>>(
                    _ => [],
                    cue =>
                        cue.Plans.SelectMany(plan => plan.Layers)
                            .OfType<OverlayCuePlaybackLayer.UploadedMedia>()
                            .Select(layer => new FullOverlayMediaReference(
                                layer.AssetId,
                                layer.ContentRevision
                            )),
                    _ => [],
                    _ => [],
                    asset => [new(asset.AssetId, asset.ContentRevision)],
                    _ => [],
                    _ => []
                )
            )
            .ToImmutableHashSet();
        return new(
            lease.ConnectionId,
            lease.Selected?.Generation.Value ?? 0,
            context.Document.Html,
            context.Document.Css,
            frames.ToImmutable()
        );
    }

    private FullOverlayRenderContext Context(FullOverlayRenderLease lease) =>
        lease.Selected is { } selected
            ? new(
                selected.HostId,
                selected.OverlayId,
                selected.Version,
                selected.Revision,
                selected.Document,
                FullOverlayDataMode.Live,
                cues.ProjectFullPlans(selected)
            )
            : new(
                lease.HostId,
                lease.OverlayId,
                null,
                null,
                lease.Preview!.Document,
                lease.Preview.Mode,
                []
            );

    private async Task<bool> ContextCurrentAsync(
        FullOverlayRenderLease lease,
        FullOverlayRenderContext context,
        CancellationToken ct
    ) =>
        await RefreshAuthorityAsync(lease, ct)
        && (
            lease.Selected is null
            || (
                lease.Selected.Version == context.Version
                && lease.Selected.Revision == context.SelectionRevision
            )
        );
}
