using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Components.Layout;

public partial class StickySaveRegion
{
    private ElementReference _element;
    private IJSObjectReference? _module;
    private IJSObjectReference? _registration;

    [Parameter]
    public bool Active { get; set; } = true;

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    [Parameter, EditorRequired]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public string Class { get; set; } = string.Empty;

    [Parameter]
    public PageSaveFeedback? Feedback { get; set; }

    [Parameter]
    public StickySaveScope Scope { get; set; } = StickySaveScope.Page;

    private string _class =>
        string.IsNullOrWhiteSpace(Class) ? "sticky-save-region" : $"sticky-save-region {Class}";

    private string _feedbackRole =>
        Feedback?.Kind is PageSaveFeedbackKind.Validation or PageSaveFeedbackKind.Failure
            ? "alert"
            : "status";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _module = await JS.InvokeAsync<IJSObjectReference>(
            "import",
            "./Components/Layout/StickySaveRegion.razor.js"
        );
        _registration = await _module.InvokeAsync<IJSObjectReference>("register", _element);
    }

    public async ValueTask DisposeAsync()
    {
        if (_registration is not null)
        {
            try
            {
                await _registration.InvokeVoidAsync("dispose");
                await _registration.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
        }

        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
        }

        GC.SuppressFinalize(this);
    }
}
