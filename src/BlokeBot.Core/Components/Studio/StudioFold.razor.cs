using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// A small labelled content fold for a field the common path rarely needs. It is the unnumbered
/// sibling of <see cref="StudioStage"/>: same button, <c>aria-expanded</c>, inert body and
/// grid-rows spring, without a number, a narrated summary or a stage's weight. Native
/// <c>&lt;details&gt;</c> is deliberately not used, because in this repository that element means a
/// menu.
/// </summary>
public partial class StudioFold
{
    [Parameter, EditorRequired]
    public required string Key { get; set; }

    [Parameter, EditorRequired]
    public required string Label { get; set; }

    [Parameter]
    public bool Open { get; set; }

    /// <summary>
    /// Drops the boxed chrome for a quiet inline toggle, for a fold that sits inside a row that
    /// already carries its own border. The mechanism is identical; only the clothes change.
    /// </summary>
    [Parameter]
    public bool Bare { get; set; }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    private string _bodyId => $"studio-fold-{Key}";

    private Task Toggle() => OpenChanged.InvokeAsync(!Open);
}
