using BlokeBot.Plugins.Contracts;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Plugins;

public partial class PluginGeneratedSettingField
{
    private ElementReference _input;
    private long _handledFocusRequest;

    [Parameter, EditorRequired]
    public PluginSettingEditor Editor { get; set; } = default!;

    private string _fieldClass =>
        Editor.Descriptor.Schema is PluginSettingSchema.MultilineText or PluginSettingSchema.Secret
            ? "studio-span-12 space-y-2"
            : "studio-span-6 space-y-2";

    private string _descriptionId => $"{Editor.InputId}-description";
    private string _labelId => $"{Editor.InputId}-label";

    private string _describedBy =>
        Editor.Error is null ? _descriptionId : $"{_descriptionId} {Editor.ErrorId}";

    private string _invalid => Editor.Error is null ? "false" : "true";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Editor.FocusRequest <= _handledFocusRequest)
        {
            return;
        }
        _handledFocusRequest = Editor.FocusRequest;
        await _input.FocusAsync();
    }

    private void SetOptionalBoolean(ChangeEventArgs args) =>
        Editor.SetOptionalBoolean(args.Value?.ToString());

    private static string NumberStep(int decimalPlaces) =>
        decimalPlaces == 0 ? "1" : $"0.{new string('0', decimalPlaces - 1)}1";

    private void SetValue(ChangeEventArgs args) =>
        Editor.Value = args.Value?.ToString() ?? string.Empty;
}
