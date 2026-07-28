using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Components;

public partial class CollapsibleSection
{
    private bool _isOpen;
    private long _handledFocusRequest;
    private long _handledOpenRequest;
    private IJSObjectReference? _module;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public string? Class { get; set; }

    [Parameter]
    public string? ContentId { get; set; }

    [Parameter]
    public string? Description { get; set; }

    [Parameter]
    public string? FocusElementId { get; set; }

    [Parameter]
    public long FocusRequest { get; set; }

    [Parameter]
    public bool InitiallyOpen { get; set; } = true;

    [Parameter]
    public long OpenRequest { get; set; }

    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    private string _panelClass =>
        string.IsNullOrWhiteSpace(Class) ? "disclosure-panel" : $"disclosure-panel {Class}";

    private string _resolvedContentId
    {
        get => string.IsNullOrWhiteSpace(ContentId) ? field : ContentId;
    } = $"disclosure-{Guid.NewGuid():N}";

    protected override void OnInitialized()
    {
        _isOpen = InitiallyOpen;
    }

    protected override void OnParametersSet()
    {
        if (OpenRequest > _handledOpenRequest)
        {
            _handledOpenRequest = OpenRequest;
            _isOpen = true;
        }

        if (FocusRequest > _handledFocusRequest && !string.IsNullOrWhiteSpace(FocusElementId))
        {
            _isOpen = true;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (
            FocusRequest <= _handledFocusRequest
            || !_isOpen
            || string.IsNullOrWhiteSpace(FocusElementId)
        )
        {
            return;
        }

        _handledFocusRequest = FocusRequest;
        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>(
                "import",
                "./Components/CollapsibleSection.razor.js"
            );
            await _module.InvokeVoidAsync("focusElement", FocusElementId);
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (TaskCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    private void Toggle()
    {
        _isOpen = !_isOpen;
    }
}
