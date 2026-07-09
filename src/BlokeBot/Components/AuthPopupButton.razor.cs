using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlokeBot.Components;

public partial class AuthPopupButton
{
    private IJSObjectReference? module;
    private bool opening;

    [Inject]
    public IJSRuntime Js { get; set; } = default!;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public string? Class { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public EventCallback OnClosed { get; set; }

    [Parameter]
    public string? OpeningText { get; set; }

    [Parameter]
    public string PopupName { get; set; } = "blokebot-oauth";

    [Parameter, EditorRequired]
    public string Url { get; set; } = string.Empty;

    public async ValueTask DisposeAsync()
    {
        if (module is null)
            return;

        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
    }

    private async Task OpenAsync()
    {
        if (opening || Disabled || string.IsNullOrWhiteSpace(Url))
            return;

        opening = true;
        try
        {
            module ??= await Js.InvokeAsync<IJSObjectReference>(
                "import",
                "./Components/AuthPopupButton.razor.js"
            );
            var popupClosed = await module.InvokeAsync<bool>("openAuthPopup", Url, PopupName);
            if (popupClosed && OnClosed.HasDelegate)
                await OnClosed.InvokeAsync();
        }
        finally
        {
            opening = false;
        }
    }
}
