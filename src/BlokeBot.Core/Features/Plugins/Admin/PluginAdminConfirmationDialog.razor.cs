using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Plugins;

public partial class PluginAdminConfirmationDialog
{
    private string _removalConfirmation = string.Empty;

    [Parameter, EditorRequired]
    public PluginAdminConfirmation Confirmation { get; set; } = default!;

    [Parameter]
    public bool Busy { get; set; }

    [Parameter, EditorRequired]
    public EventCallback Cancel { get; set; }

    [Parameter, EditorRequired]
    public EventCallback Confirm { get; set; }

    private bool _confirmDisabled =>
        Busy
        || (
            Confirmation is PluginAdminConfirmation.Remove remove
            && !string.Equals(
                _removalConfirmation.Trim(),
                remove.Plugin.PluginId.Value,
                StringComparison.Ordinal
            )
        );

    private string _confirmClass =>
        Confirmation is PluginAdminConfirmation.Remove
            ? "plugin-admin__danger-button"
            : "btn-primary";

    private string _confirmLabel =>
        Confirmation switch
        {
            PluginAdminConfirmation.Install => "Install plugin",
            PluginAdminConfirmation.Update => "Apply update",
            PluginAdminConfirmation.Remove => "Remove permanently",
            _ => "Confirm",
        };

    private void SetRemovalConfirmation(ChangeEventArgs args) =>
        _removalConfirmation = args.Value?.ToString() ?? string.Empty;
}
