using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Plugins;

public partial class PluginAdminPanel
{
    private readonly HashSet<PluginId> _busy = [];
    private PluginAdminConfirmation? _confirmation;
    private bool _loading;
    private long _loadVersion;
    private string _query = string.Empty;
    private PluginAdminSnapshot? _snapshot;

    [Parameter, EditorRequired]
    public AuthenticatedSession Session { get; set; } = AuthenticatedSession.Anonymous;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private bool IsBusy(PluginId pluginId) => _busy.Contains(pluginId);

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        _loading = true;
        var outcome = await _plugins.LoadAsync(Session, _query, CancellationToken.None);
        if (version != _loadVersion)
        {
            return;
        }

        _snapshot = outcome switch
        {
            PluginAdminLoadOutcome.Loaded loaded => loaded.Snapshot,
            PluginAdminLoadOutcome.Unauthorized => null,
            _ => throw new InvalidOperationException("Unknown plugin Admin load result."),
        };
        _loading = false;
    }

    private async Task ReloadAsync() => await LoadAsync();

    private async Task SearchAsync(ChangeEventArgs args)
    {
        _query = args.Value?.ToString() ?? string.Empty;
        await LoadAsync();
    }

    private void OpenInstall(PluginAdminCatalogEntry entry) =>
        _confirmation = new PluginAdminConfirmation.Install(entry.Entry);

    private void OpenUpdate(PluginAdminInstalledPlugin plugin)
    {
        if (plugin.UpdateRelease is { } release)
        {
            _confirmation = new PluginAdminConfirmation.Update(plugin, release);
        }
    }

    private void OpenRemove(PluginAdminInstalledPlugin plugin) =>
        _confirmation = new PluginAdminConfirmation.Remove(plugin);

    private void CloseConfirmation() => _confirmation = null;

    private async Task ConfirmAsync()
    {
        if (_confirmation is not { } confirmation || IsBusy(confirmation.PluginId))
        {
            return;
        }

        _confirmation = null;
        await RunAsync(
            confirmation.PluginId,
            () =>
                confirmation switch
                {
                    PluginAdminConfirmation.Install install => _plugins.InstallAsync(
                        Session,
                        install.PluginId,
                        install.Entry.Release,
                        CancellationToken.None
                    ),
                    PluginAdminConfirmation.Update update => _plugins.UpdateAsync(
                        Session,
                        update.PluginId,
                        update.Release,
                        CancellationToken.None
                    ),
                    PluginAdminConfirmation.Remove remove => _plugins.RemoveAsync(
                        Session,
                        remove.PluginId,
                        CancellationToken.None
                    ),
                    _ => throw new InvalidOperationException("Unknown plugin confirmation."),
                }
        );
    }

    private async Task RestartAsync(PluginAdminInstalledPlugin plugin) =>
        await RunAsync(
            plugin.PluginId,
            () => _plugins.RestartAsync(Session, plugin.PluginId, CancellationToken.None)
        );

    private async Task RunAsync(
        PluginId pluginId,
        Func<ValueTask<PluginMarketplaceCommandOutcome>> operation
    )
    {
        if (!_busy.Add(pluginId))
        {
            return;
        }

        StateHasChanged();
        try
        {
            var outcome = await operation();
            PublishOutcome(outcome);
            await LoadAsync();
        }
        finally
        {
            _ = _busy.Remove(pluginId);
        }
    }

    private void PublishOutcome(PluginMarketplaceCommandOutcome outcome)
    {
        var feedback = outcome switch
        {
            PluginMarketplaceCommandOutcome.Completed
            {
                Lifecycle: PluginLifecycleCommandOutcome.Succeeded succeeded,
            } => new PluginAdminFeedback(
                succeeded.View.LatestOutcome.Code switch
                {
                    PluginLifecycleOutcomeCode.Activated => "The plugin is active.",
                    PluginLifecycleOutcomeCode.Restarted => "The plugin restarted.",
                    _ => "The plugin operation completed.",
                },
                false
            ),
            PluginMarketplaceCommandOutcome.Completed
            {
                Lifecycle: PluginLifecycleCommandOutcome.Removed,
            } => new("The plugin and all of its context were removed.", false),
            PluginMarketplaceCommandOutcome.Completed
            {
                Lifecycle: PluginLifecycleCommandOutcome.Failed failed,
            } => new(PluginAdminCopy.Fault(failed.View.LatestOutcome), true),
            PluginMarketplaceCommandOutcome.Rejected rejected => new(
                PluginAdminCopy.Rejection(rejected),
                true
            ),
            _ => new(
                "The plugin operation did not complete. Reload the status and try again.",
                true
            ),
        };
        _ = feedback.Error
            ? _toasts.Publish(new ToastRequest<ErrorToastStrategy>(feedback.Message))
            : _toasts.Publish(new ToastRequest<StatusToastStrategy>(feedback.Message));
    }

    private sealed record PluginAdminFeedback(string Message, bool Error);
}

public abstract record PluginAdminConfirmation
{
    private PluginAdminConfirmation() { }

    public abstract PluginId PluginId { get; }

    public sealed record Install(PluginMarketplaceCatalogEntry Entry) : PluginAdminConfirmation
    {
        public override PluginId PluginId => Entry.PluginId;
    }

    public sealed record Update(PluginAdminInstalledPlugin Plugin, PluginReleaseIdentity Release)
        : PluginAdminConfirmation
    {
        public override PluginId PluginId => Plugin.PluginId;
    }

    public sealed record Remove(PluginAdminInstalledPlugin Plugin) : PluginAdminConfirmation
    {
        public override PluginId PluginId => Plugin.PluginId;
    }
}
