using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Overlays.Full;

public partial class FullOverlayEditorPage
{
    private ElementReference _exportForm;

    private Task ExportAsync() =>
        _client is null
            ? Task.CompletedTask
            : _client.InvokeVoidAsync("export", _exportForm, _name).AsTask();

    private IReadOnlyList<FullOverlayPublicationView> _history = [];
    private string _liveUrl = "";
    private string _savedName = "";
    private bool _hasChanges => _view.Dirty || _name != _savedName;

    private async Task OpenManagementAsync()
    {
        var history = await _documents.HistoryAsync(
            PageContext.Session,
            OverlayId,
            _lifetime.Token
        );
        _ = history.Match(
            found =>
            {
                _history = found.Value;
                return true;
            },
            rejected =>
            {
                Rejected(rejected.Reason);
                return false;
            }
        );
        var access = await _documents.ReadAccessAsync(
            PageContext.Session,
            OverlayId,
            _lifetime.Token
        );
        _ = access.Match(
            found =>
            {
                _liveUrl = _navigation.ToAbsoluteUri(found.Value.RelativeUrl).ToString();
                return true;
            },
            rejected =>
            {
                Rejected(rejected.Reason);
                return false;
            }
        );
        await _module!.InvokeVoidAsync("openDialog", _managementDialog);
    }

    private async Task CopyUrlAsync()
    {
        try
        {
            await _module!.InvokeVoidAsync("copyText", _liveUrl);
            _message = "Live URL copied.";
        }
        catch (JSException)
        {
            _message = "The browser could not copy it. Select the live URL and copy it manually.";
        }
    }

    private Task CloseManagementAsync() =>
        _module!.InvokeVoidAsync("closeDialog", _managementDialog).AsTask();

    private Task AskDeleteAsync() => _module!.InvokeVoidAsync("openDialog", _deleteDialog).AsTask();

    private Task CancelDeleteAsync() =>
        _module!.InvokeVoidAsync("closeDialog", _deleteDialog).AsTask();

    private async Task RollbackAsync(FullOverlayVersion version)
    {
        if (_busy || _row is null)
        {
            return;
        }

        _busy = true;
        try
        {
            var result = await _documents.RollbackAsync(
                PageContext.Session,
                new(OverlayId, _row.Revision, version),
                _lifetime.Token
            );
            await result.Match(
                async selected =>
                {
                    _row = selected.Value.Overlay;
                    _publicationDiagnostics = selected.Value.Warnings;
                    _message = $"Version {version.Value} is live. Your working draft is unchanged.";
                    await RefreshPreviewAsync(_view.Revision);
                },
                rejected =>
                {
                    Rejected(rejected.Reason);
                    return Task.CompletedTask;
                }
            );
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ForgetAsync(FullOverlayVersion version)
    {
        if (
            _busy
            || _row is null
            || !await _js.InvokeAsync<bool>(
                "confirm",
                $"Forget retained version {version.Value}? This cannot be undone."
            )
        )
        {
            return;
        }

        _busy = true;
        try
        {
            var result = await _documents.ForgetVersionAsync(
                PageContext.Session,
                new(OverlayId, _row.Revision, version),
                _lifetime.Token
            );
            _ = result.Match(
                saved =>
                {
                    _row = saved.Value;
                    _history = _history.Where(item => item.Version != version).ToArray();
                    _message = $"Version {version.Value} forgotten.";
                    return true;
                },
                rejected =>
                {
                    Rejected(rejected.Reason);
                    return false;
                }
            );
            await RefreshPreviewAsync(_view.Revision);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DuplicateAsync()
    {
        if (_busy || _row is null)
        {
            return;
        }

        _busy = true;
        try
        {
            var result = await _documents.DuplicateAsync(
                PageContext.Session,
                new(OverlayId, _row.Revision, $"{_row.Name} copy"),
                _lifetime.Token
            );
            _ = result.Match(
                created =>
                {
                    _message =
                        "Copied the saved draft as unpublished, with a new live URL and no publication history.";
                    _navigation.NavigateTo(
                        $"/full-overlays/{created.Value.Overlay.Id}/edit",
                        forceLoad: true
                    );
                    return true;
                },
                rejected =>
                {
                    Rejected(rejected.Reason);
                    return false;
                }
            );
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task LifecycleAsync()
    {
        if (_busy || _row is null)
        {
            return;
        }

        _busy = true;
        try
        {
            var command = new FullOverlayMutation(OverlayId, _row.Revision);
            var result = _row.IsArchived
                ? await _documents.RestoreAsync(PageContext.Session, command, _lifetime.Token)
                : await _documents.ArchiveAsync(PageContext.Session, command, _lifetime.Token);
            _ = result.Match(
                saved =>
                {
                    _row = saved.Value;
                    _message = _row.IsArchived
                        ? "Overlay archived; live delivery is unavailable."
                        : "Overlay restored with its existing live selection and URL.";
                    return true;
                },
                rejected =>
                {
                    Rejected(rejected.Reason);
                    return false;
                }
            );
            await RefreshPreviewAsync(_view.Revision);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (_busy || _row is null)
        {
            return;
        }

        _busy = true;
        try
        {
            var result = await _documents.DeleteAsync(
                PageContext.Session,
                new(OverlayId, _row.Revision, FullOverlayDeleteConfirmation.Confirmed),
                _lifetime.Token
            );
            _ = result.Match(
                deleted =>
                {
                    _view = FullOverlayEditorView.Empty;
                    _savedName = _name;
                    _navigation.NavigateTo("/full-overlays");
                    return true;
                },
                rejected =>
                {
                    Rejected(rejected.Reason);
                    return false;
                }
            );
        }
        finally
        {
            _busy = false;
        }
    }
}
