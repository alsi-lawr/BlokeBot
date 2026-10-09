using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Overlays.Full;

public partial class FullOverlayEditorPage
{
    [JSInvokable]
    public Task SaveShortcutAsync() => SaveAsync();

    private Task SaveAsync() => SaveCandidateAsync(false);

    private Task PublishAsync() => SaveCandidateAsync(true);

    private async Task SaveCandidateAsync(bool publish)
    {
        if (_busy || _row is null || _client is null)
        {
            return;
        }

        _busy = true;
        _message = publish ? "Saving and publishing…" : "Saving draft…";
        _publicationDiagnostics = [];
        try
        {
            var candidate = await _client.InvokeAsync<FullOverlayDocument>("candidate");
            var name = _name;
            var command = new SaveFullOverlayCommand(OverlayId, _row.Revision, name, candidate);
            if (publish)
            {
                var result = await _documents.SaveAndPublishAsync(
                    PageContext.Session,
                    command,
                    _lifetime.Token
                );
                await result.Match(
                    async saved =>
                    {
                        await SavedAsync(saved.Value.Overlay, candidate);
                        _publicationDiagnostics = saved.Value.Warnings;
                        _message = "Saved and published. The stable live URL is unchanged.";
                    },
                    rejected =>
                    {
                        Rejected(rejected.Reason);
                        return Task.CompletedTask;
                    }
                );
            }
            else
            {
                var result = await _documents.SaveAsync(
                    PageContext.Session,
                    command,
                    _lifetime.Token
                );
                await result.Match(
                    async saved =>
                    {
                        await SavedAsync(saved.Value, candidate);
                        _message = "Draft saved. The live publication is unchanged.";
                    },
                    rejected =>
                    {
                        Rejected(rejected.Reason);
                        return Task.CompletedTask;
                    }
                );
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SavedAsync(FullOverlayView saved, FullOverlayDocument candidate)
    {
        _row = saved;
        _savedName = saved.Name;
        if (_client is not null)
        {
            await _client.InvokeVoidAsync("saved", candidate);
        }

        await RefreshPreviewAsync(_view.Revision);
    }

    private void Rejected(FullOverlayRejection rejection)
    {
        _message = FullOverlayEditorMessages.Rejection(rejection);
        _publicationDiagnostics = rejection.Diagnostics;
    }
}
