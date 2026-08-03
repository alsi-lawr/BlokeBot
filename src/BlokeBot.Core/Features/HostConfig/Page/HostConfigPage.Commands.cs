using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Toasts;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostConfigPage
{
    private string _commandsAliases = string.Empty;
    private int? _commandsDraftHostId;
    private bool _commandsDirty;
    private bool _commandsSaving;
    private bool _commandCatalogLoading;
    private ViewerCommandCatalogSnapshot? _commandCatalog;

    private void LoadCommandsDraft(int hostId, CommandsConfiguration configuration)
    {
        if (_commandsDraftHostId == hostId && _commandsDirty)
        {
            return;
        }

        _commandsDraftHostId = hostId;
        _commandsAliases = configuration.Aliases;
        _commandsDirty = false;
    }

    private void MarkCommandsDirty() => _commandsDirty = true;

    private Task SaveCommandsAsync(int hostId) =>
        ObserveUiOperationAsync(
            nameof(SaveCommandsAsync),
            () => RunSelectedHostMutationAsync(hostId, () => SaveCommandsCoreAsync(hostId))
        );

    private async Task SaveCommandsCoreAsync(int hostId)
    {
        _commandsSaving = true;
        try
        {
            var outcome = await _commandsConfiguration.SaveAsync(
                PageContext.Session,
                hostId,
                new CommandsConfigurationSaveCommand(_commandsAliases),
                CancellationToken.None
            );
            switch (outcome)
            {
                case CommandsConfigurationSaveOutcome.Saved saved:
                    _commandsAliases = saved.Configuration.Aliases;
                    _commandsDirty = false;
                    if (_state is not null)
                    {
                        _state = _state with { Commands = saved.Configuration };
                    }
                    await RefreshCommandCatalogAsync();
                    _ = _toasts.Publish(
                        ToastRequest<PositiveStatusToastStrategy>.WithTitle(
                            string.IsNullOrWhiteSpace(_commandsAliases)
                                ? "The viewer command catalog is disabled."
                                : "Viewers can now use the saved Commands aliases.",
                            "Commands saved"
                        )
                    );
                    break;
                case CommandsConfigurationSaveOutcome.AliasConflict conflict:
                    PublishCommandsError(
                        $"!{conflict.Alias} is already used by another command. Choose a different word."
                    );
                    break;
                case CommandsConfigurationSaveOutcome.AliasTooLong tooLong:
                    PublishCommandsError(
                        $"Keep each command word to {tooLong.MaximumLength} characters or fewer."
                    );
                    break;
                case CommandsConfigurationSaveOutcome.Unauthorized:
                    PublishCommandsError(
                        "Only the streamer or a BlokeBot administrator can change this setting."
                    );
                    break;
                case CommandsConfigurationSaveOutcome.HostNotFound:
                    PublishCommandsError("The selected channel setup was not found.");
                    break;
            }
        }
        finally
        {
            _commandsSaving = false;
        }
    }

    private async Task RefreshCommandCatalogAsync()
    {
        if (_state?.HostId is not { } hostId)
        {
            _commandCatalog = null;
            return;
        }

        _commandCatalogLoading = true;
        try
        {
            _commandCatalog = await _commandCatalogService.LoadForHostAsync(
                hostId,
                CancellationToken.None
            );
        }
        finally
        {
            _commandCatalogLoading = false;
        }
    }

    private void PublishCommandsError(string message) =>
        _toasts.Publish(ToastRequest<ErrorToastStrategy>.WithTitle(message, "Commands not saved"));
}
