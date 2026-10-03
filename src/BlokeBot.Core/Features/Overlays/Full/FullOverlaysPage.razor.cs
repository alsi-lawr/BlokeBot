using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Overlays.Full;

public partial class FullOverlaysPage
{
    [Inject]
    private FullOverlayService _documents { get; set; } = default!;
    private IReadOnlyList<FullOverlayView> _items = [];
    private string _name = "New overlay";
    private string _message = "";
    private bool _archived;
    private bool _busy;

    protected override async Task OnInitializedAsync()
    {
        _ = await LoadPageContextAsync();
        await LoadAsync();
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
                new(_name, FullOverlayDocument.Blank()),
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
