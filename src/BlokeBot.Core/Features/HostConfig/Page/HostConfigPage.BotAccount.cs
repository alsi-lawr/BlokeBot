using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Core.Features.Toasts;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostConfigPage
{
    private string _activeBotAccountName =>
        _state?.BotOverride.Enabled == true
            ? _state.BotOverride.Status.AuthorizedLogin
                ?? _state.BotOverride.Status.ConfiguredBotLogin
                ?? "the custom bot account"
            : _botSettings.Identity.BotUsername;

    private string _activeBotReconnectUrl =>
        _state?.BotOverride.Enabled == true ? "/oauth/host-bot/start" : "/oauth/start";

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

    private WhisperQuotaPresentation _whisperQuotaPresentation =>
        WhisperQuotaPresentation.From(_state?.BotOverride.WhisperQuota);

    private string _whisperQuotaBadgeClass =>
        _whisperQuotaPresentation.State switch
        {
            WhisperQuotaPresentationState.Healthy =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-700 ring-1 ring-slate-200",
            WhisperQuotaPresentationState.Caution =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200",
            WhisperQuotaPresentationState.Limit =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-red-50 px-2.5 text-xs font-bold text-red-700 ring-1 ring-red-200",
            _ => throw new UnreachableException(),
        };

    private string _whisperQuotaDotClass =>
        _whisperQuotaPresentation.State switch
        {
            WhisperQuotaPresentationState.Healthy => "h-1.5 w-1.5 rounded-full bg-slate-500",
            WhisperQuotaPresentationState.Caution => "h-1.5 w-1.5 rounded-full bg-amber-500",
            WhisperQuotaPresentationState.Limit => "h-1.5 w-1.5 rounded-full bg-red-500",
            _ => throw new UnreachableException(),
        };

    private string _whisperQuotaText => _whisperQuotaPresentation.Text;

    private Task SetBotOverrideEnabledAsync(int hostId, bool enabled)
    {
        return ObserveUiOperationAsync(
            nameof(SetBotOverrideEnabledAsync),
            () =>
                RunSelectedHostMutationAsync(
                    hostId,
                    () => SetBotOverrideEnabledCoreAsync(hostId, enabled)
                )
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
            () =>
                RunSelectedHostMutationAsync(
                    hostId,
                    () => SetWhisperResponsesEnabledCoreAsync(hostId, enabled)
                )
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

internal enum WhisperQuotaPresentationState
{
    Healthy,
    Caution,
    Limit,
}

internal sealed record WhisperQuotaPresentation(string Text, WhisperQuotaPresentationState State)
{
    private const int _cautionRecipientCount = 30;

    public static WhisperQuotaPresentation From(WhisperQuotaStatus? status)
    {
        var recipientCount = status?.RecipientCount ?? 0;
        var atLimit =
            status?.Exhausted == true || recipientCount >= WhisperQuotaService.UniqueRecipientLimit;
        var displayedRecipientCount = atLimit
            ? WhisperQuotaService.UniqueRecipientLimit
            : recipientCount;
        var state =
            atLimit ? WhisperQuotaPresentationState.Limit
            : recipientCount >= _cautionRecipientCount ? WhisperQuotaPresentationState.Caution
            : WhisperQuotaPresentationState.Healthy;
        return new($"{displayedRecipientCount}/{WhisperQuotaService.UniqueRecipientLimit}", state);
    }
}
