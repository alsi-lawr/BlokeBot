using BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace BlokeBot.Core.Features.Overlays.Full;

public partial class FullOverlaysPage
{
    [Inject]
    private FullOverlayService _documents { get; set; } = default!;

    [Inject]
    private FullOverlayWidgetRegistry _registry { get; set; } = default!;

    [Inject]
    private FullOverlayTransferService _transfer { get; set; } = default!;
    private FullOverlayTemplate _template;
    private Guid? _importedId;
    private CancellationTokenSource? _import;
    private IReadOnlyList<FullOverlayView> _items = [];
    private string _name = "New overlay";
    private string _message = "";
    private bool _archived;
    private bool _importOpen;
    private bool _busy;

    protected override async Task OnInitializedAsync()
    {
        _ = await LoadPageContextAsync();
        await LoadAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _import?.Cancel();
        }
        base.Dispose(disposing);
    }

    private async Task LoadAsync()
    {
        var result = await _documents.ListAsync(
            PageContext.Session,
            _archived ? FullOverlayCollection.Archived : FullOverlayCollection.Active,
            CancellationToken.None
        );
        _ = result.Match(
            found =>
            {
                _items = found.Value;
                return true;
            },
            rejected =>
            {
                _message = FullOverlayEditorMessages.Rejection(rejected.Reason);
                return false;
            }
        );
    }

    private async Task SelectCollectionAsync(bool archived)
    {
        _archived = archived;
        await LoadAsync();
    }

    private async Task ImportAsync(InputFileChangeEventArgs args)
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        _import = new();
        _message = "Importing document and media…";
        try
        {
            var file = args.File;
            await using var stream = file.OpenReadStream(file.Size, _import.Token);
            var result = await _transfer.ImportAsync(PageContext.Session, stream, _import.Token);
            _ = result.Match(
                applied =>
                {
                    _message = applied.Value.NotificationPending
                        ? "Imported; live notification needs follow-up. Do not import again."
                        : "Imported as an unpublished draft.";
                    _importedId = applied.Value.Created.Overlay.Id;
                    if (!applied.Value.NotificationPending)
                    {
                        Navigation.NavigateTo($"/full-overlays/{_importedId}/edit");
                    }
                    return true;
                },
                rejected =>
                {
                    _message =
                        rejected.Reason.Diagnostics.FirstOrDefault()?.Message
                        ?? FullOverlayEditorMessages.Rejection(rejected.Reason);
                    return false;
                }
            );
        }
        catch (OperationCanceledException) when (_import.IsCancellationRequested)
        {
            _message = "Import canceled. No document or media changes were committed.";
        }
        finally
        {
            _import.Dispose();
            _import = null;
            _busy = false;
        }
    }

    private async Task CreateAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var result = await _documents.CreateAsync(
                PageContext.Session,
                new(_name, FullOverlayTemplates.Create(_template, _registry)),
                CancellationToken.None
            );
            _ = result.Match(
                created =>
                {
                    Navigation.NavigateTo($"/full-overlays/{created.Value.Overlay.Id}/edit");
                    return true;
                },
                rejected =>
                {
                    _message = FullOverlayEditorMessages.Rejection(rejected.Reason);
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
