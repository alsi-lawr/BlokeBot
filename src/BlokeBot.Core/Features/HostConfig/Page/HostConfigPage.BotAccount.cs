using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Toasts;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostConfigPage
{
    private bool _botAccountCanStart =>
        _state?.BotOverride.Enabled != true
        || _state.BotOverride.Status.State == BotAccountAuthorizationState.Ready;

    private string _botAccountStatusReloadKey =>
        _state is null
            ? string.Empty
            : string.Join(
                ":",
                _state.Login,
                _state.BotOverride.Enabled,
                _state.BotOverride.Status.State,
                _state.BotOverride.Status.AuthorizedLogin ?? string.Empty,
                string.Join(",", _state.BotOverride.Status.GrantedScopes)
            );

    private string _whisperQuotaBadgeClass =>
        _state?.BotOverride.WhisperQuota.Exhausted == true
        || _state?.BotOverride.WhisperQuota.Remaining == 0
            ? "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200"
            : "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200";

    private string _whisperQuotaDotClass =>
        _state?.BotOverride.WhisperQuota.Exhausted == true
        || _state?.BotOverride.WhisperQuota.Remaining == 0
            ? "h-1.5 w-1.5 rounded-full bg-amber-500"
            : "h-1.5 w-1.5 rounded-full bg-emerald-500";

    private string _whisperQuotaText =>
        _state?.BotOverride.WhisperQuota is { } quota
            ? $"{quota.RecipientCount} of {quota.Limit}"
            : "0 of 40";

    private Task SetBotOverrideEnabledAsync(int hostId, bool enabled)
    {
        return ObserveUiOperationAsync(
            nameof(SetBotOverrideEnabledAsync),
            () => SetBotOverrideEnabledCoreAsync(hostId, enabled)
        );
    }

    private async Task SetBotOverrideEnabledCoreAsync(int hostId, bool enabled)
    {
        var runtimeWasActive =
            _runtimeLifecycle
            is HostedChannelRuntimeLifecycle.Starting
                or HostedChannelRuntimeLifecycle.Started;
        if (enabled)
        {
            await _hostBotAccounts.UseCustomBotAsync(hostId, CancellationToken.None);
        }
        else
        {
            await _hostBotAccounts.UseMainBotAsync(hostId, CancellationToken.None);
        }

        await LoadCoreAsync();
        if (runtimeWasActive)
        {
            TrackPendingRuntimeTransition();
        }

        if (enabled)
        {
            _toasts.Publish(
                ToastRequest<PositiveStatusToastStrategy>.WithTitle(
                    "Custom bot is turned on for this channel. Connect the account before starting the bot.",
                    "Custom bot on"
                )
            );
        }
        else
        {
            _toasts.Publish(
                ToastRequest<CautionStatusToastStrategy>.WithTitle(
                    "Custom bot is turned off. This channel will use the main bot account.",
                    "Custom bot off"
                )
            );
        }
    }

    private Task SetWhisperResponsesEnabledAsync(int hostId, bool enabled)
    {
        return ObserveUiOperationAsync(
            nameof(SetWhisperResponsesEnabledAsync),
            () => SetWhisperResponsesEnabledCoreAsync(hostId, enabled)
        );
    }

    private async Task SetWhisperResponsesEnabledCoreAsync(int hostId, bool enabled)
    {
        var outcome = enabled
            ? await _hostBotAccounts.EnableWhisperResponsesAsync(hostId, CancellationToken.None)
            : await _hostBotAccounts.DisableWhisperResponsesAsync(hostId, CancellationToken.None);
        await LoadCoreAsync();

        outcome
            .Match<Action>(_ => ShowSavedStatus, _ => ShowRejectedStatus, _ => ShowRejectedStatus)
            .Invoke();

        void ShowRejectedStatus()
        {
            _toasts.Publish(
                ToastRequest<ErrorToastStrategy>.WithTitle(
                    "Turn on custom bot before enabling whisper responses.",
                    "Whisper responses not saved"
                )
            );
        }

        void ShowSavedStatus()
        {
            if (enabled)
            {
                _toasts.Publish(
                    ToastRequest<PositiveStatusToastStrategy>.WithTitle(
                        "Command replies will use custom-bot whispers when available.",
                        "Whisper responses on"
                    )
                );
            }
            else
            {
                _toasts.Publish(
                    ToastRequest<CautionStatusToastStrategy>.WithTitle(
                        "Command replies will use public chat.",
                        "Whisper responses off"
                    )
                );
            }
        }
    }
}
