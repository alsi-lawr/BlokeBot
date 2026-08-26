using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Plugins;

public partial class PluginInstalledAdminCard
{
    [Parameter, EditorRequired]
    public PluginAdminInstalledPlugin Plugin { get; set; } = default!;

    [Parameter]
    public bool Busy { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<PluginAdminInstalledPlugin> Update { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<PluginAdminInstalledPlugin> Restart { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<PluginAdminInstalledPlugin> Remove { get; set; }

    private bool _controlsDisabled => Busy || Plugin.OperationInProgress;

    private string _statusClass =>
        Plugin.Status switch
        {
            PluginAdminInstalledStatus.Active => "status-pill status-pill--green",
            PluginAdminInstalledStatus.Degraded => "status-pill status-pill--amber",
            PluginAdminInstalledStatus.Faulted => "status-pill status-pill--red",
            PluginAdminInstalledStatus.Operation => "status-pill status-pill--blue",
        };

    private string _statusLabel =>
        Plugin.Status switch
        {
            PluginAdminInstalledStatus.Active => "Active",
            PluginAdminInstalledStatus.Degraded => "Degraded",
            PluginAdminInstalledStatus.Faulted => "Faulted",
            PluginAdminInstalledStatus.Operation => _operationLabel,
        };

    private string _operationLabel =>
        Busy
            ? "Operation active"
            : Plugin.Lifecycle.Phase switch
            {
                PluginLifecyclePhase.Preparing => "Preparing",
                PluginLifecyclePhase.Migrating => "Updating data",
                PluginLifecyclePhase.Activating => "Activating",
                PluginLifecyclePhase.Draining => "Stopping work",
                PluginLifecyclePhase.Removing => "Removing",
                _ => "Operation active",
            };

    private string _featureSummary =>
        $"{FeatureCount(Plugin.Features.Length)} · {ChannelCount(Plugin.EnabledChannelCount)}";

    private static string FeatureCount(int count) => count == 1 ? "1 feature" : $"{count} features";

    private static string ChannelCount(int count) =>
        count == 1 ? "Enabled on 1 channel" : $"Enabled on {count} channels";

    private static string ReceiptOperation(PluginMarketplaceOperationKind operation) =>
        operation switch
        {
            PluginMarketplaceOperationKind.Install => "Install",
            PluginMarketplaceOperationKind.Update => "Update",
            PluginMarketplaceOperationKind.Restart => "Restart",
            PluginMarketplaceOperationKind.Remove => "Remove",
        };
}
