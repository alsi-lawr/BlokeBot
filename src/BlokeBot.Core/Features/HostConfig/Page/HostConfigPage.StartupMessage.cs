using BlokeBot.Core.Features.HostConfig.StartupMessage;
using BlokeBot.Core.Features.Toasts;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostConfigPage
{
    private bool _startupMessageEnabled;
    private string _startupMessageText = string.Empty;
    private bool _startupMessageSaving;

    private void LoadStartupMessageDraft(StartupMessageConfiguration configuration)
    {
        _startupMessageEnabled = configuration.Enabled;
        _startupMessageText = configuration.Text;
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
