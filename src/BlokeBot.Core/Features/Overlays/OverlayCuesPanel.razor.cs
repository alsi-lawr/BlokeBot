using System.Globalization;
using BlokeBot.Core.Components.Studio;
using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Overlays;

public partial class OverlayCuesPanel
{
    private readonly HashSet<CueStage> _openStages = [CueStage.Content];
    private readonly HashSet<CueLayerDraft> _openLayers = [];
    private IReadOnlyList<OverlayCueView> _items = [];
    private IReadOnlyList<OverlayMediaAssetView> _assets = [];
    private OverlayCueAdmissionCatalog _catalog = new([], []);
    private OverlayCueView? _selected;
    private Guid? _target;
    private string _name = "Raid celebration";
    private int _duration = 8000;
    private bool _cueEnabled = true;
    private OverlayCueQueuePolicy _policy = OverlayCueQueuePolicy.Enqueue;
    private List<CueLayerDraft> _layers = [CueLayerDraft.New(CueLayerKind.WebPage)];

    protected override Task LoadAsync() => LoadAsync(null);

    private Task LoadAsync(Guid? selectedId) =>
        LoadOverlayAsync(() => LoadCuesAsync(selectedId), "Cues could not load. Try again.");

    private async Task LoadCuesAsync(Guid? selectedId)
    {
        var cueResult = await _cues.ListCuesAsync(PageContext.Session, CancellationToken.None);
        if (cueResult is OverlayCueResult<IReadOnlyList<OverlayCueView>>.Rejected cueRejected)
        {
            Fail(cueRejected.Reason.Message);
            return;
        }
        _items = ((OverlayCueResult<IReadOnlyList<OverlayCueView>>.Succeeded)cueResult).Value;

        var assetResult = await _cues.ListAssetsAsync(PageContext.Session, CancellationToken.None);
        if (
            assetResult
            is OverlayCueResult<IReadOnlyList<OverlayMediaAssetView>>.Rejected assetRejected
        )
        {
            Fail(assetRejected.Reason.Message);
            return;
        }
        _assets = (
            (OverlayCueResult<IReadOnlyList<OverlayMediaAssetView>>.Succeeded)assetResult
        ).Value;

        var item = selectedId is null
            ? _items.FirstOrDefault()
            : _items.FirstOrDefault(value => value.Id == selectedId);
        if (item is not null)
        {
            SelectCue(item);
        }
        else if (_selected is not null)
        {
            NewCue();
        }

        _catalog = await _playback.QueryCatalogAsync(HostId, CancellationToken.None);
        _target ??= _catalog.Targets.FirstOrDefault()?.Id;
    }

    private void NewCue()
    {
        _selected = null;
        _name = "Raid celebration";
        _duration = 8000;
        _cueEnabled = true;
        _policy = OverlayCueQueuePolicy.Enqueue;
        _layers = [CueLayerDraft.New(CueLayerKind.WebPage)];
        Feedback = string.Empty;
        _openLayers.Clear();
        _ = _openLayers.Add(_layers[0]);
    }

    private void SelectCue(OverlayCueView cue)
    {
        _selected = cue;
        _name = cue.Name;
        _duration = cue.DurationMilliseconds;
        _cueEnabled = cue.IsEnabled;
        _policy = cue.QueuePolicy;
        _layers = cue.Configuration.Layers.Select(CueLayerDraft.FromLayer).ToList();
        _openLayers.Clear();
    }

    private void AddLayer(CueLayerKind kind)
    {
        if (_layers.Count < OverlayCueConfiguration.MaximumLayerCount)
        {
            var layer = CueLayerDraft.New(kind);
            _layers.Add(layer);
            _ = _openLayers.Add(layer);
        }
    }

    private void ChangeLayerKind(int index, CueLayerKind kind) =>
        _layers[index] = CueLayerDraft.New(kind);

    private void RemoveLayer(int index)
    {
        if (_layers.Count > 1)
        {
            _layers.RemoveAt(index);
        }
    }

    private void MoveLayer(int index, int offset)
    {
        var destination = index + offset;
        if (destination < 0 || destination >= _layers.Count)
        {
            return;
        }
        (_layers[index], _layers[destination]) = (_layers[destination], _layers[index]);
    }

    private async Task SaveAsync() =>
        await RunAsync(async () =>
        {
            var configuration = BuildConfiguration();
            if (configuration is OverlayCueConfigurationResult.Invalid invalid)
            {
                Fail(FriendlyValidation(invalid.Message));
                return;
            }

            var value = ((OverlayCueConfigurationResult.Valid)configuration).Value;
            var result = await _cues.SaveCueAsync(
                PageContext.Session,
                new SaveOverlayCueCommand(
                    _selected?.Id,
                    _selected?.Revision ?? new OverlayCueRevision(0),
                    _name,
                    _cueEnabled,
                    _duration,
                    _policy,
                    value.ToPersistenceJson()
                ),
                CancellationToken.None
            );
            if (result is OverlayCueResult<OverlayCueView>.Rejected rejected)
            {
                Fail(FriendlyValidation(rejected.Reason.Message));
                return;
            }
            var saved = ((OverlayCueResult<OverlayCueView>.Succeeded)result).Value;
            Success("Cue saved.");
            await LoadAsync(saved.Id);
        });

    private OverlayCueConfigurationResult BuildConfiguration()
    {
        try
        {
            return OverlayCueConfiguration.Create(
                _layers.Select(value => value.ToLayer(_assets)).ToArray()
            );
        }
        catch (UriFormatException)
        {
            return new OverlayCueConfigurationResult.Invalid(
                "Use a complete secure address beginning with https://."
            );
        }
    }

    private async Task DeleteAsync()
    {
        if (_selected is null)
        {
            return;
        }
        await RunAsync(async () =>
        {
            var result = await _cues.DeleteCueAsync(
                PageContext.Session,
                _selected.Id,
                _selected.Revision,
                CancellationToken.None
            );
            if (result is OverlayCueResult<Guid>.Rejected rejected)
            {
                Fail(rejected.Reason.Message);
                return;
            }
            NewCue();
            Success("Cue deleted.");
            await LoadAsync();
        });
    }

    private async Task PlayAsync()
    {
        if (_selected is null || _target is null)
        {
            return;
        }
        await RunAsync(async () =>
        {
            var outcome = await _playback.AdmitAsync(
                new OverlayCueAdmissionRequest(
                    HostId,
                    _target.Value,
                    _selected.Id,
                    _selected.QueuePolicy,
                    OverlayCueAdmissionOrigin.OwnerPreview,
                    OverlayCueSafeContext.Empty
                ),
                CancellationToken.None
            );
            if (
                outcome
                is OverlayCueAdmissionOutcome.Running
                    or OverlayCueAdmissionOutcome.Queued
                    or OverlayCueAdmissionOutcome.Disconnected
            )
            {
                Success("Test cue sent to the selected Browser Source.");
            }
            else
            {
                Fail("The cue could not play. Check that the cue and target are enabled.");
            }
        });
    }

    private IReadOnlyList<StudioRailGroup> _railGroups =>
        [
            new(
                $"Saved cues · {_items.Count}",
                [
                    .. _items.Select(cue => new StudioRailItem
                    {
                        Key = cue.Id.ToString("D"),
                        Label = cue.Name,
                        Sub = $"{(cue.IsEnabled ? "On" : "Off")} · {PolicyLabel(cue.QueuePolicy)}",
                        On = cue.IsEnabled,
                        Selected = _selected?.Id == cue.Id,
                        Select = EventCallback.Factory.Create(this, () => SelectCue(cue)),
                    }),
                ],
                EmptyMessage: "No saved cues yet."
            ),
        ];

    private bool IsStageOpen(CueStage stage) => _openStages.Contains(stage);

    private void SetStage(CueStage stage, bool open) =>
        _ = open ? _openStages.Add(stage) : _openStages.Remove(stage);

    private void ToggleLayer(CueLayerDraft layer) =>
        _ = _openLayers.Add(layer) || _openLayers.Remove(layer);

    private static string LayerBodyId(int index) => $"cue-layer-body-{index}";

    private static string LayerRowClass(bool open) =>
        open
            ? "rounded-[14px] border border-[var(--app-focus-border)] bg-[var(--app-surface-solid)] shadow-[var(--app-shadow-sm)]"
            : "rounded-[14px] border border-[var(--app-control-border)] bg-[var(--app-control-bg)]";

    private string HeaderStats() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{_layers.Count} content {(_layers.Count == 1 ? "layer" : "layers")} · {DurationLabel(_duration)}"
        );

    private string BasicsSummary() => $"{DurationLabel(_duration)} · {PolicyLabel(_policy)}";

    private string ContentSummary()
    {
        var kinds = string.Join(
            ", ",
            _layers.Select(layer => KindLabel(layer.Kind).ToLowerInvariant()).Distinct()
        );
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_layers.Count} {(_layers.Count == 1 ? "layer" : "layers")} · {kinds}"
        );
    }

    private string TestSummary() =>
        _catalog.Targets.FirstOrDefault(target => target.Id == _target) is { } chosen
            ? $"Target: {chosen.Name}"
            : "Choose a Cue player";

    private static string DurationLabel(int milliseconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 1000m:0.###} s");

    private static string KindLabel(CueLayerKind kind) =>
        kind switch
        {
            CueLayerKind.UploadedMedia => "Uploaded media",
            CueLayerKind.OnlineMedia => "Online media",
            _ => "Web page",
        };

    private string LayerGlyph(CueLayerDraft layer) =>
        layer.Kind switch
        {
            CueLayerKind.WebPage => "🌐",
            CueLayerKind.OnlineMedia => layer.MediaKind is OverlayCueMediaKind.Audio ? "🔊" : "🎬",
            _ => UploadedAsset(layer) is { } asset
                ? OverlayMediaTypes.Kind(asset.ContentType) switch
                {
                    OverlayCueMediaKind.Image => "🖼",
                    OverlayCueMediaKind.Audio => "🔊",
                    _ => "🎬",
                }
                : "🎬",
        };

    private string LayerTitle(CueLayerDraft layer) =>
        layer.Kind switch
        {
            CueLayerKind.UploadedMedia => UploadedAsset(layer)?.Name ?? "Choose media",
            _ => string.IsNullOrWhiteSpace(layer.Address) ? KindLabel(layer.Kind) : layer.Address,
        };

    private static string LayerSummary(CueLayerDraft layer)
    {
        var timing = string.Create(
            CultureInfo.InvariantCulture,
            $"{KindLabel(layer.Kind)} · {layer.StartMilliseconds} – {layer.StartMilliseconds + layer.DurationMilliseconds} ms"
        );
        return layer.Kind is CueLayerKind.WebPage
            ? timing
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{timing} · {layer.VolumePercent:0.#}% volume · {FitSummary(layer.Fit)}"
            );
    }

    private static string FitSummary(OverlayCueFitMode fit) =>
        fit switch
        {
            OverlayCueFitMode.Cover => "fill and crop",
            OverlayCueFitMode.Fill => "stretch to fill",
            _ => "show all",
        };

    private OverlayMediaAssetView? UploadedAsset(CueLayerDraft layer) =>
        _assets.FirstOrDefault(asset => asset.Id == layer.AssetId);

    private static string PolicyLabel(OverlayCueQueuePolicy policy) =>
        policy switch
        {
            OverlayCueQueuePolicy.Enqueue => "Play after the current cue",
            OverlayCueQueuePolicy.Replace => "Replace the current cue",
            OverlayCueQueuePolicy.Ignore => "Skip while another cue plays",
            OverlayCueQueuePolicy.Concurrent => "Play at the same time",
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };

    private static string FriendlyValidation(string message) =>
        message
            .Replace("Cue-V1", "The cue", StringComparison.Ordinal)
            .Replace("uploadedMedia", "uploaded media", StringComparison.Ordinal)
            .Replace("remoteMedia", "online media", StringComparison.Ordinal)
            .Replace("externalWeb", "web page", StringComparison.Ordinal)
            .Replace("assetId", "media choice", StringComparison.Ordinal)
            .Replace("mediaKind", "media type", StringComparison.Ordinal)
            .Replace("zIndex", "stacking order", StringComparison.Ordinal)
            .Replace("rectangle", "position and size", StringComparison.Ordinal);

    private enum CueStage
    {
        Basics,
        Content,
        Test,
    }

    private enum CueLayerKind
    {
        UploadedMedia,
        OnlineMedia,
        WebPage,
    }

    private sealed class CueLayerDraft
    {
        public CueLayerKind Kind { get; set; }
        public Guid? AssetId { get; set; }
        public string Address { get; set; } = "https://example.com/";
        public OverlayCueMediaKind MediaKind { get; set; } = OverlayCueMediaKind.Video;
        public int StartMilliseconds { get; set; }
        public int DurationMilliseconds { get; set; } = 8000;
        public int StackOrder { get; set; }
        public decimal VolumePercent { get; set; } = 100;
        public OverlayCueFitMode Fit { get; set; } = OverlayCueFitMode.Contain;
        public decimal X { get; set; } = 10;
        public decimal Y { get; set; } = 10;
        public decimal Width { get; set; } = 80;
        public decimal Height { get; set; } = 80;

        public static CueLayerDraft New(CueLayerKind kind) => new() { Kind = kind };

        public static CueLayerDraft FromLayer(OverlayCueLayer layer)
        {
            var rectangle = layer switch
            {
                OverlayCueLayer.UploadedMedia value => value.Rectangle,
                OverlayCueLayer.RemoteMedia value => value.Rectangle,
                OverlayCueLayer.ExternalWeb value => value.Rectangle,
                _ => throw new ArgumentOutOfRangeException(nameof(layer)),
            };
            var draft = new CueLayerDraft
            {
                Kind = layer switch
                {
                    OverlayCueLayer.UploadedMedia => CueLayerKind.UploadedMedia,
                    OverlayCueLayer.RemoteMedia => CueLayerKind.OnlineMedia,
                    OverlayCueLayer.ExternalWeb => CueLayerKind.WebPage,
                    _ => throw new ArgumentOutOfRangeException(nameof(layer)),
                },
                StartMilliseconds = layer.StartOffsetMilliseconds,
                DurationMilliseconds = layer.DurationMilliseconds,
                StackOrder = layer.ZIndex,
                X = rectangle.XPercent,
                Y = rectangle.YPercent,
                Width = rectangle.WidthPercent,
                Height = rectangle.HeightPercent,
            };
            switch (layer)
            {
                case OverlayCueLayer.UploadedMedia uploaded:
                    draft.AssetId = uploaded.AssetId;
                    draft.MediaKind = uploaded.MediaKind;
                    draft.VolumePercent = uploaded.Volume * 100;
                    draft.Fit = uploaded.Fit;
                    break;
                case OverlayCueLayer.RemoteMedia remote:
                    draft.Address = remote.Url.AbsoluteUri;
                    draft.MediaKind = remote.MediaKind;
                    draft.VolumePercent = remote.Volume * 100;
                    draft.Fit = remote.Fit;
                    break;
                case OverlayCueLayer.ExternalWeb web:
                    draft.Address = web.Url.AbsoluteUri;
                    break;
            }
            return draft;
        }

        public OverlayCueLayer ToLayer(IReadOnlyList<OverlayMediaAssetView> assets)
        {
            var rectangle = new OverlayCueRectangle(X, Y, Width, Height);
            return Kind switch
            {
                CueLayerKind.UploadedMedia => new OverlayCueLayer.UploadedMedia
                {
                    AssetId = AssetId ?? Guid.Empty,
                    MediaKind =
                        OverlayMediaTypes.Kind(
                            assets.FirstOrDefault(value => value.Id == AssetId)?.ContentType
                                ?? string.Empty
                        ) ?? OverlayCueMediaKind.Video,
                    StartOffsetMilliseconds = StartMilliseconds,
                    DurationMilliseconds = DurationMilliseconds,
                    ZIndex = StackOrder,
                    Volume = VolumePercent / 100,
                    Fit = Fit,
                    Rectangle = rectangle,
                },
                CueLayerKind.OnlineMedia => new OverlayCueLayer.RemoteMedia
                {
                    Url = new Uri(Address, UriKind.Absolute),
                    MediaKind = MediaKind,
                    StartOffsetMilliseconds = StartMilliseconds,
                    DurationMilliseconds = DurationMilliseconds,
                    ZIndex = StackOrder,
                    Volume = VolumePercent / 100,
                    Fit = Fit,
                    Rectangle = rectangle,
                },
                CueLayerKind.WebPage => new OverlayCueLayer.ExternalWeb
                {
                    Url = new Uri(Address, UriKind.Absolute),
                    StartOffsetMilliseconds = StartMilliseconds,
                    DurationMilliseconds = DurationMilliseconds,
                    ZIndex = StackOrder,
                    Rectangle = rectangle,
                },
                _ => throw new ArgumentOutOfRangeException(),
            };
        }
    }
}
