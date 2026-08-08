using System.Globalization;
using Microsoft.AspNetCore.Components.Forms;

namespace BlokeBot.Core.Features.Overlays;

public partial class OverlayMediaPanel
{
    private IReadOnlyList<OverlayMediaAssetView> _items = [];
    private Guid? _previewTarget;
    private string _name = "Stream media";

    protected override Task LoadAsync() =>
        LoadOverlayAsync(LoadMediaAsync, "Media could not load. Try again.");

    private async Task LoadMediaAsync()
    {
        var result = await _media.ListAssetsAsync(PageContext.Session, CancellationToken.None);
        if (result is OverlayCueResult<IReadOnlyList<OverlayMediaAssetView>>.Succeeded succeeded)
        {
            _items = succeeded.Value;
        }
        _previewTarget = (await _playback.QueryCatalogAsync(HostId, CancellationToken.None))
            .Targets.FirstOrDefault()
            ?.Id;
    }

    private Task UploadAsync(InputFileChangeEventArgs args) =>
        RunAsync(async () =>
        {
            var file = args.File;
            await using var stream = file.OpenReadStream(file.Size);
            var result = await _media.UploadAssetAsync(
                PageContext.Session,
                _name,
                file.ContentType,
                stream,
                CancellationToken.None
            );
            if (result is OverlayCueResult<OverlayMediaAssetView>.Rejected rejected)
            {
                Fail(rejected.Reason.Message);
                return;
            }
            Success("Media uploaded and ready for cues.");
            await LoadAsync();
        });

    private Task ReplaceAsync(OverlayMediaAssetView asset, InputFileChangeEventArgs args) =>
        RunAsync(async () =>
        {
            var file = args.File;
            await using var stream = file.OpenReadStream(file.Size);
            var result = await _media.ReplaceAssetAsync(
                PageContext.Session,
                new ReplaceOverlayMediaAssetCommand(
                    asset.Id,
                    asset.ContentRevision,
                    file.ContentType,
                    stream
                ),
                CancellationToken.None
            );
            if (result is OverlayCueResult<OverlayMediaAssetView>.Rejected rejected)
            {
                Fail(rejected.Reason.Message);
                return;
            }
            Success("Media replaced. New cue playback uses the new file.");
            await LoadAsync();
        });

    private Task DeleteAsync(OverlayMediaAssetView asset) =>
        RunAsync(async () =>
        {
            var result = await _media.DeleteAssetAsync(
                PageContext.Session,
                asset.Id,
                asset.ContentRevision,
                CancellationToken.None
            );
            if (result is OverlayCueResult<Guid>.Rejected rejected)
            {
                Fail(rejected.Reason.Message);
                return;
            }
            Success("Media deleted.");
            await LoadAsync();
        });

    private static string ByteLabel(long value) =>
        value >= 1024 * 1024 ? $"{value / (1024m * 1024m):0.##} MB" : $"{value / 1024m:0.##} KB";

    private long UsedBytes() => _items.Sum(asset => asset.ByteLength);

    private string StorageMeterStyle()
    {
        var maximum = _options.Value.Overlays.Media.MaximumHostStorageBytes;
        var percent = maximum <= 0 ? 0m : Math.Clamp(UsedBytes() * 100m / maximum, 0m, 100m);
        return string.Create(CultureInfo.InvariantCulture, $"width: {percent:0.#}%");
    }

    private string SavedMediaNote()
    {
        var count = _items.Count == 1 ? "1 file" : $"{_items.Count} files";
        return _previewTarget is null
            ? count
            : $"{count} · previews play through your enabled Cue player";
    }

    private string ThumbnailClass(OverlayMediaAssetView asset) =>
        OverlayMediaTypes.Kind(asset.ContentType) switch
        {
            OverlayCueMediaKind.Audio =>
                "aspect-[16/10] bg-[var(--app-surface-muted)] text-2xl text-muted-foreground",
            _ when _previewTarget is not null => "aspect-[16/10] bg-slate-950",
            OverlayCueMediaKind.Image =>
                "aspect-[16/10] bg-linear-to-br from-slate-800 to-slate-600 text-2xl text-white",
            _ => "aspect-[16/10] bg-linear-to-br from-slate-700 to-slate-900 text-2xl text-white",
        };

    private static string ThumbnailGlyph(OverlayMediaAssetView asset) =>
        OverlayMediaTypes.Kind(asset.ContentType) switch
        {
            OverlayCueMediaKind.Image => "🖼",
            OverlayCueMediaKind.Audio => "♪",
            _ => "▶",
        };
}
