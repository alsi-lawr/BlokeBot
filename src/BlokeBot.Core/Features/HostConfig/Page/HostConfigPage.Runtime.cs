using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Toasts;

namespace BlokeBot.Core.Features.HostConfig.Page;

public partial class HostConfigPage
{
    private PendingRuntimeTransition? _pendingRuntimeTransition;
    private HostedChannelRuntimeLifecycle? _runtimeLifecycle => _state?.RuntimeStatus?.Lifecycle;

    private bool _canStart =>
        _state?.RuntimeStatus is not null
        && _state.IsChannelBotAuthorized
        && _state.RuntimeStatus.ChannelBotAuthorizationScopesCurrent
        && _botAccountCanStart
        && _state.RuntimeStatus.Lifecycle is HostedChannelRuntimeLifecycle.Stopped;

    private bool _canStop =>
        _runtimeLifecycle
            is HostedChannelRuntimeLifecycle.Started
                or HostedChannelRuntimeLifecycle.Starting;

    private string _authorizationBadgeClass =>
        _state?.IsChannelBotAuthorized == true
        && _state.RuntimeStatus?.ChannelBotAuthorizationScopesCurrent == true
            ? "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200"
            : "inline-flex h-6 items-center gap-1.5 rounded-full bg-amber-50 px-2.5 text-xs font-bold text-amber-700 ring-1 ring-amber-200";

    private string _authorizationDotClass =>
        _state?.IsChannelBotAuthorized == true
        && _state.RuntimeStatus?.ChannelBotAuthorizationScopesCurrent == true
            ? "h-1.5 w-1.5 rounded-full bg-emerald-500"
            : "h-1.5 w-1.5 rounded-full bg-amber-500";

    private string _authorizationText =>
        _state?.IsChannelBotAuthorized == true
        && _state.RuntimeStatus?.ChannelBotAuthorizationScopesCurrent == true
            ? "connected"
        : _state?.IsChannelBotAuthorized == true ? "needs update"
        : "not connected";

    private string _runtimeBadgeClass =>
        _runtimeLifecycle?.Match(
            static _ =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-600 ring-1 ring-slate-200",
            static _ =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-orange-50 px-2.5 text-xs font-bold text-orange-700 ring-1 ring-orange-200",
            static _ =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 text-xs font-bold text-emerald-700 ring-1 ring-emerald-200",
            static _ =>
                "inline-flex h-6 items-center gap-1.5 rounded-full bg-purple-50 px-2.5 text-xs font-bold text-purple-700 ring-1 ring-purple-200"
        )
        ?? "inline-flex h-6 items-center gap-1.5 rounded-full bg-slate-100 px-2.5 text-xs font-bold text-slate-600 ring-1 ring-slate-200";

    private string _runtimeDotClass =>
        _runtimeLifecycle?.Match(
            static _ => "h-1.5 w-1.5 rounded-full bg-slate-400",
            static _ => "h-1.5 w-1.5 rounded-full bg-orange-500",
            static _ => "h-1.5 w-1.5 rounded-full bg-emerald-500",
            static _ => "h-1.5 w-1.5 rounded-full bg-purple-500"
        ) ?? "h-1.5 w-1.5 rounded-full bg-slate-400";

    private string _runtimeText =>
        _runtimeLifecycle?.Match(
            static _ => "offline",
            static _ => "starting",
            static _ => "online",
            static _ => "stopping"
        ) ?? "offline";

    private string _runtimeStatusMessage =>
        _runtimeLifecycle?.Match(
            static _ => "The bot is offline.",
            static _ => "The bot is starting.",
            static _ => "The bot is in chat.",
            static _ => "The bot is leaving chat."
        ) ?? "The bot is offline.";

    private string _startRuntimeTooltip =>
        _canStart ? "Start the bot for this channel." : _startRuntimeDisabledTooltip;

    private string _stopRuntimeTooltip =>
        _canStop ? "Stop the bot for this channel." : _stopRuntimeDisabledTooltip;

    private string _startRuntimeDisabledTooltip =>
        _state is null ? "Wait for the channel to load before starting the bot."
        : _state.IsChannelBotAuthorized != true ? "Connect the channel before starting the bot."
        : _state.RuntimeStatus?.ChannelBotAuthorizationScopesCurrent != true
            ? "Reconnect the channel before starting the bot."
        : !_botAccountCanStart ? "Connect the custom bot account before starting the bot."
        : _state.RuntimeStatus?.Lifecycle is not HostedChannelRuntimeLifecycle.Stopped
            ? "Wait for the bot to stop before starting it again."
        : "The bot cannot be started right now.";

    private string _stopRuntimeDisabledTooltip =>
        _runtimeLifecycle is HostedChannelRuntimeLifecycle.Stopping
            ? "The bot is already stopping."
            : "The bot is not running right now.";

    private Task ClearChannelAuthorizationAsync(int hostId)
    {
        return ObserveUiOperationAsync(
            nameof(ClearChannelAuthorizationAsync),
            () =>
                RunSelectedHostMutationAsync(
                    hostId,
                    () => ClearChannelAuthorizationCoreAsync(hostId)
                )
        );
    }

    private async Task ClearChannelAuthorizationCoreAsync(int hostId)
    {
        await _channelBotAuthorization.ClearAsync(hostId, CancellationToken.None);
        await LoadCoreAsync();
        _toasts.Publish(
            new ToastRequest<StatusToastStrategy>(
                "The channel has been disconnected from Twitch chat."
            )
        );
    }

    private Task ClearBotOverrideAuthorizationAsync(int hostId)
    {
        return ObserveUiOperationAsync(
            nameof(ClearBotOverrideAuthorizationAsync),
            () =>
                RunSelectedHostMutationAsync(
                    hostId,
                    () => ClearBotOverrideAuthorizationCoreAsync(hostId)
                )
        );
    }

    private async Task ClearBotOverrideAuthorizationCoreAsync(int hostId)
    {
        var session = PageContext.Session;
        var outcome = await _hostBotAccounts.ClearAsync(
            hostId,
            new HostBotAccountActor(session.UserId, session.Login),
            CancellationToken.None
        );
        await LoadCoreAsync();
        if (outcome is not HostBotAccountClearOutcome.Cleared)
        {
            _toasts.Publish(
                ToastRequest<ErrorToastStrategy>.WithTitle(
                    "BlokeBot could not confirm that you own this channel. Sign in again and retry.",
                    "Custom bot not disconnected"
                )
            );
            return;
        }

        _toasts.Publish(
            ToastRequest<CautionStatusToastStrategy>.WithTitle(
                "The custom bot account has been disconnected.",
                "Custom bot disconnected"
            )
        );
    }

    private Task StartAsync(int hostId)
    {
        return ObserveUiOperationAsync(
            nameof(StartAsync),
            () => RunSelectedHostMutationAsync(hostId, () => StartCoreAsync(hostId))
        );
    }

    private async Task StartCoreAsync(int hostId)
    {
        var result = await _runtime.Start(hostId).ExecuteAsync(CancellationToken.None);
        var outcome = result.Match(value => value, _ => throw new UnreachableException());
        await LoadCoreAsync();
        if (outcome is HostedChannelRuntimeControlOutcome.Accepted)
        {
            TrackPendingRuntimeTransition();
            _toasts.Publish(new ToastRequest<StatusToastStrategy>(_runtimeStatusMessage));
        }
        else
        {
            _toasts.Publish(new ToastRequest<ErrorToastStrategy>(RuntimeControlMessage(outcome)));
        }
    }

    private Task StopAsync(int hostId)
    {
        return ObserveUiOperationAsync(
            nameof(StopAsync),
            () => RunSelectedHostMutationAsync(hostId, () => StopCoreAsync(hostId))
        );
    }

    private async Task StopCoreAsync(int hostId)
    {
        var result = await _runtime.Stop(hostId).ExecuteAsync(CancellationToken.None);
        var outcome = result.Match(value => value, _ => throw new UnreachableException());
        await LoadCoreAsync();
        if (outcome is HostedChannelRuntimeControlOutcome.Accepted)
        {
            TrackPendingRuntimeTransition();
            _toasts.Publish(new ToastRequest<StatusToastStrategy>(_runtimeStatusMessage));
        }
        else
        {
            _toasts.Publish(new ToastRequest<ErrorToastStrategy>(RuntimeControlMessage(outcome)));
        }
    }

    private static string RuntimeControlMessage(HostedChannelRuntimeControlOutcome outcome)
    {
        return outcome switch
        {
            HostedChannelRuntimeControlOutcome.HostNotFound => "Channel setup was not found.",
            HostedChannelRuntimeControlOutcome.ChannelAuthorizationRequired =>
                "Connect the bot to Twitch chat before starting it.",
            HostedChannelRuntimeControlOutcome.CustomBotNotReady =>
                "Connect the custom bot account before starting it, or turn custom bot off.",
            HostedChannelRuntimeControlOutcome.Cooldown cooldown =>
                $"Wait until {cooldown.NextAllowedAtUtc.ToLocalTime():HH:mm:ss} before starting or stopping the bot again.",
            _ => throw new UnreachableException(),
        };
    }

    private void TrackPendingRuntimeTransition()
    {
        _pendingRuntimeTransition = PendingTransition(_runtimeLifecycle);
    }

    private static PendingRuntimeTransition? PendingTransition(
        HostedChannelRuntimeLifecycle? lifecycle
    )
    {
        return lifecycle?.Match<PendingRuntimeTransition?>(
            static _ => null,
            static _ => PendingRuntimeTransition.Starting,
            static _ => null,
            static _ => PendingRuntimeTransition.Stopping
        );
    }

    private enum PendingRuntimeTransition
    {
        Starting,
        Stopping,
    }
}
