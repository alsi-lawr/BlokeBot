using BlokeBot.Core.Features.HostConfig.StartupMessage;
using BlokeBot.Core.Features.Toasts;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostConfigPage
{
    private bool _startupMessageEnabled;
    private string _startupMessageText = string.Empty;
    private int? _startupMessageDraftHostId;
    private bool _startupMessageDirty;
    private bool _startupMessageSaving;

    private void LoadStartupMessageDraft(int hostId, StartupMessageConfiguration configuration)
    {
        if (_startupMessageDraftHostId == hostId && _startupMessageDirty)
        {
            return;
        }

        _startupMessageDraftHostId = hostId;
        _startupMessageEnabled = configuration.Enabled;
        _startupMessageText = configuration.Text;
        _startupMessageDirty = false;
    }

    private void SetStartupMessageEnabled(ChangeEventArgs args)
    {
        _startupMessageEnabled = args.Value is true;
        _startupMessageDirty = true;
    }

    private void SetStartupMessageText(ChangeEventArgs args)
    {
        _startupMessageText = args.Value?.ToString() ?? string.Empty;
        _startupMessageDirty = true;
    }

    private Task SaveStartupMessageAsync(int hostId)
    {
        return ObserveUiOperationAsync(
            nameof(SaveStartupMessageAsync),
            () => RunSelectedHostMutationAsync(hostId, () => SaveStartupMessageCoreAsync(hostId))
        );
    }

    private async Task SaveStartupMessageCoreAsync(int hostId)
    {
        _startupMessageSaving = true;
        try
        {
            var outcome = await _startupMessages.SaveAsync(
                PageContext.Session,
                hostId,
                new StartupMessageSaveCommand(_startupMessageEnabled, _startupMessageText),
                CancellationToken.None
            );
            switch (outcome)
            {
                case StartupMessageSaveOutcome.Saved:
                    _startupMessageDirty = false;
                    await LoadCoreAsync();
                    _toasts.Publish(
                        ToastRequest<PositiveStatusToastStrategy>.WithTitle(
                            "The change will apply the next time the bot joins or reconnects. The active bot was not restarted.",
                            "Startup message saved"
                        )
                    );
                    break;
                case StartupMessageSaveOutcome.TextRequired:
                    PublishStartupMessageError("Enter a message before turning startup chat on.");
                    break;
                case StartupMessageSaveOutcome.TextTooLong tooLong:
                    PublishStartupMessageError(
                        $"Keep the startup message to {tooLong.MaximumLength} characters or fewer."
                    );
                    break;
                case StartupMessageSaveOutcome.Unauthorized:
                    PublishStartupMessageError(
                        "Only the streamer or a BlokeBot administrator can change this setting."
                    );
                    break;
                case StartupMessageSaveOutcome.HostNotFound:
                    PublishStartupMessageError("The selected channel setup was not found.");
                    break;
            }
        }
        finally
        {
            _startupMessageSaving = false;
        }
    }

    private void PublishStartupMessageError(string message)
    {
        _toasts.Publish(
            ToastRequest<ErrorToastStrategy>.WithTitle(message, "Startup message not saved")
        );
    }
}
