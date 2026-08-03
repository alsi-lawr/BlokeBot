using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Overlays;

public partial class OverlayStateToggle
{
    [Parameter, EditorRequired]
    public required string Label { get; set; }

    [Parameter]
    public bool Value { get; set; }

    [Parameter]
    public EventCallback<bool> ValueChanged { get; set; }

    private string _cssClass =>
        Value
            ? "inline-flex min-h-9 items-center gap-2 rounded-full border border-blue-500 bg-blue-600 px-3 py-1.5 text-sm font-semibold text-white shadow-sm transition"
            : "inline-flex min-h-9 items-center gap-2 rounded-full border border-slate-300 bg-slate-100 px-3 py-1.5 text-sm font-semibold text-slate-500 transition";

    private string _indicatorClass =>
        Value ? "size-2.5 rounded-full bg-white" : "size-2.5 rounded-full bg-slate-400";

    private Task ToggleAsync() => ValueChanged.InvokeAsync(!Value);
}
