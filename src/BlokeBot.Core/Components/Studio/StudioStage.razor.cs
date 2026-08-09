using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// One numbered stage of a staged-disclosure editor. The closed header narrates the stage's current
/// state through <see cref="Summary"/>, so the whole configuration reads without opening anything.
/// A surface whose save-time validation names a field inside this stage raises
/// <see cref="FocusRequest"/> to send the caret there once the stage is open.
/// </summary>
public partial class StudioStage : IAsyncDisposable
{
    private long _handledFocusRequest;
    private IJSObjectReference? _module;

    [Parameter, EditorRequired]
    public required string Key { get; set; }

    [Parameter, EditorRequired]
    public required int Number { get; set; }

    [Parameter, EditorRequired]
    public required string Title { get; set; }

    [Parameter]
    public string? Summary { get; set; }

    [Parameter]
    public bool Open { get; set; }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    /// <summary>
    /// The element focused when <see cref="FocusRequest"/> next rises. Opening the stage stays the
    /// caller's job, because the caller owns <see cref="Open"/>.
    /// </summary>
    [Parameter]
    public string? FocusElementId { get; set; }

    /// <summary>
    /// A monotonic counter; each increase asks for one focus once the stage has rendered open.
    /// </summary>
    [Parameter]
    public long FocusRequest { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    public static string BodyId(string key) => $"studio-stage-{key}";

    private string _bodyId => BodyId(Key);

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (
            FocusRequest <= _handledFocusRequest
            || !Open
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
                "./Components/Studio/StudioStage.razor.js"
            );
            await _module.InvokeVoidAsync("focusElement", FocusElementId);
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (TaskCanceledException) { }
    }

    private Task Toggle() => OpenChanged.InvokeAsync(!Open);
}
