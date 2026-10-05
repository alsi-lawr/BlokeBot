using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Overlays.Full;

public partial class FullOverlayEditorPage
{
    private Guid? _previewId;
    private long _previewRequest;
    private FullOverlayDataMode _dataMode = FullOverlayDataMode.Sample;
    private string _previewState = "Preparing private preview";

    [JSInvokable]
    public async Task RefreshPreviewAsync(long revision)
    {
        if (_disposed || _client is null || revision != _view.Revision)
        {
            return;
        }

        var request = ++_previewRequest;
        var candidate = await ReadCandidateAsync();
        var result = await _delivery.CreatePreviewAsync(
            PageContext.Session,
            candidate,
            _dataMode,
            _lifetime.Token
        );
        if (result is FullOverlayResult<Guid>.Succeeded created)
        {
            if (!await InstallPreviewAsync(created.Value, request, revision))
            {
                return;
            }
        }
        else
        {
            _ = result.Match(
                _ => true,
                rejected =>
                {
                    _previewState = FullOverlayEditorMessages.Rejection(rejected.Reason);
                    return false;
                }
            );
        }

        await InvokeAsync(StateHasChanged);
    }

    internal async Task<bool> InstallPreviewAsync(Guid id, long request, long revision)
    {
        if (!PreviewCurrent(request, revision))
        {
            await ReleaseCandidateAsync(id);
            return false;
        }

        await ReleasePreviewAsync();
        if (!PreviewCurrent(request, revision))
        {
            await ReleaseCandidateAsync(id);
            return false;
        }

        _previewId = id;
        await _client!.InvokeVoidAsync("preview", id.ToString(), revision);
        return true;
    }

    private bool PreviewCurrent(long request, long revision) =>
        !_disposed && request == _previewRequest && revision == _view.Revision;

    private Task ReleaseCandidateAsync(Guid id) =>
        _delivery.ReleasePreviewAsync(PageContext.Session, id, CancellationToken.None);

    [JSInvokable]
    public Task PreviewStatusAsync(string state, string[] codes)
    {
        _previewState =
            codes.Contains("audio-blocked", StringComparer.Ordinal)
                ? "Audio blocked by browser autoplay"
            : codes.Length > 0 ? "Preview ready · some widgets need attention"
            : state == "ready" ? "Unsaved"
            : "Preview reconnecting";
        return InvokeAsync(StateHasChanged);
    }

    private async Task ReleasePreviewAsync()
    {
        if (_previewId is { } previous)
        {
            _previewId = null;
            await ReleaseCandidateAsync(previous);
        }
    }

    private Task DataModeAsync(ChangeEventArgs args)
    {
        _dataMode =
            args.Value?.ToString() == "Live"
                ? FullOverlayDataMode.Live
                : FullOverlayDataMode.Sample;
        return RefreshPreviewAsync(_view.Revision);
    }

    private Task ReplayAsync() => RefreshPreviewAsync(_view.Revision);
}
