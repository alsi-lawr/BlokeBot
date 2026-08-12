using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components;

public partial class SaveChangesButton
{
    [Parameter]
    public string? AccessibleName { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    [Parameter]
    public string Class { get; set; } = string.Empty;

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public bool HasChanges { get; set; } = true;

    [Parameter, EditorRequired]
    public EventCallback OnSave { get; set; }

    [Parameter]
    public bool Saving { get; set; }

    private const string _baseClass =
        "btn-primary save-changes-button disabled:cursor-not-allowed disabled:opacity-50";

    private string _class =>
        string.IsNullOrWhiteSpace(Class) ? _baseClass : $"{_baseClass} {Class}";

    private bool _disabled => Disabled || Saving;

    private string _state => HasChanges ? "dirty" : "clean";
}
