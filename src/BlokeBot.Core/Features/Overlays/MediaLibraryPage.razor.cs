using BlokeBot.Persistence.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace BlokeBot.Core.Features.Overlays;

public partial class MediaLibraryPage
{
    private IReadOnlyList<OverlayMediaAssetView> _items = [];
    private Guid? _previewTarget;
    private string _name = "Stream media";
    private bool _enabled;
    private bool _loading = true;
    private bool _busy;
    private bool _failed;
    private string _feedback = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        await LoadPageContextAsync();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _enabled =
                Host is not null
                && await _features.IsEnabledAsync(
                    HostId,
                    HostFeatureFlags.Overlays,
                    CancellationToken.None
                );
            if (!_enabled)
            {
                return;
            }
            var result = await _media.ListAssetsAsync(PageContext.Session, CancellationToken.None);
            if (
                result is OverlayCueResult<IReadOnlyList<OverlayMediaAssetView>>.Succeeded succeeded
            )
            {
                _items = succeeded.Value;
            }
            _previewTarget = (await _playback.QueryCatalogAsync(HostId, CancellationToken.None))
                .Targets.FirstOrDefault()
                ?.Id;
        }
        catch (Exception exception)
        {
            ReportUiFault(nameof(LoadAsync), exception);
            Fail("Media could not load. Try again.");
        }
        finally
        {
            _loading = false;
        }
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

    private async Task RunAsync(Func<Task> operation)
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        try
        {
            await RunSelectedHostMutationAsync(HostId, operation);
        }
        finally
        {
            _busy = false;
        }
    }

    private void Success(string message)
    {
        _feedback = message;
        _failed = false;
    }

    private void Fail(string message)
    {
        _feedback = message;
        _failed = true;
    }

    private static string ByteLabel(long value) =>
        value >= 1024 * 1024 ? $"{value / (1024m * 1024m):0.##} MB" : $"{value / 1024m:0.##} KB";
}
