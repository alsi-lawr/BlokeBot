using BlokeBot.Features.Replies;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Components;

public partial class ReplyTextAreaField
{
    [Parameter]
    public string Label { get; set; } = string.Empty;

    [Parameter]
    public string Value { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    [Parameter, EditorRequired]
    public ReplyDeliveryMap Delivery { get; set; } = new();

    [Parameter, EditorRequired]
    public string ReplyKey { get; set; } = string.Empty;

    [Parameter]
    public bool WhisperDisabled { get; set; }

    [Parameter]
    public string WhisperDisabledTooltip { get; set; } = string.Empty;

    private bool IsWhisper => Delivery.IsWhisper(ReplyKey);

    private string WhisperTitle => WhisperDisabled ? WhisperDisabledTooltip : string.Empty;

    private string WhisperLabelClass =>
        WhisperDisabled
            ? "inline-flex items-center gap-2 text-xs font-semibold text-muted-foreground opacity-60"
            : "inline-flex items-center gap-2 text-xs font-semibold text-muted-foreground";

    private Task OnInput(ChangeEventArgs e) =>
        ValueChanged.InvokeAsync(e.Value?.ToString() ?? string.Empty);

    private Task OnWhisperChanged(ChangeEventArgs e)
    {
        if (!WhisperDisabled)
            Delivery.SetWhisper(ReplyKey, e.Value is true || e.Value?.ToString() == "true");

        return Task.CompletedTask;
    }
}
