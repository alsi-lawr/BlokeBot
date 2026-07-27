using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Components;

public partial class CollapsibleSection
{
    private bool _isOpen;
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
    public bool InitiallyOpen { get; set; } = true;

    [Parameter]
    public long OpenRequest { get; set; }

    [Parameter]
    public string? PreferenceKey { get; set; }

    [Parameter]
    public string Title { get; set; } = string.Empty;

    private string _panelClass =>
        string.IsNullOrWhiteSpace(Class) ? "disclosure-panel" : $"disclosure-panel {Class}";

    private string _resolvedContentId
    {
        get => string.IsNullOrWhiteSpace(ContentId) ? field : ContentId;
    } = $"disclosure-{Guid.NewGuid():N}";

    private string? _storageKey =>
        string.IsNullOrWhiteSpace(PreferenceKey) ? null : $"blokebot.disclosure.{PreferenceKey}";

    protected override void OnInitialized()
    {
        _isOpen = InitiallyOpen;
    }

    protected override void OnParametersSet()
    {
        if (OpenRequest <= _handledOpenRequest)
        {
            return;
        }

        _handledOpenRequest = OpenRequest;
        _isOpen = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _storageKey is null)
        {
            return;
        }

        try
        {
            _module = await _js.InvokeAsync<IJSObjectReference>(
                "import",
                "./Components/CollapsibleSection.razor.js"
            );
            var rememberedOpen = await _module.InvokeAsync<bool?>("readBoolean", _storageKey);
            if (rememberedOpen is not null && _handledOpenRequest == 0)
            {
                _isOpen = rememberedOpen.Value;
            }

            await InvokeAsync(StateHasChanged);
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

    private long _handledOpenRequest;

    private async Task ToggleAsync()
    {
        _isOpen = !_isOpen;
        if (_module is not null && _storageKey is not null)
        {
            await _module.InvokeVoidAsync("writeBoolean", _storageKey, _isOpen);
        }
    }
}
