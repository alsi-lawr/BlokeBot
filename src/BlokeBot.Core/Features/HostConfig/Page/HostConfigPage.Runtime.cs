using System.Diagnostics;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Hosts;

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
            ? "status-pill bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200"
            : "status-pill bg-amber-50 text-amber-700 ring-1 ring-amber-200";

    private string _authorizationDotClass =>
        _state?.IsChannelBotAuthorized == true
        && _state.RuntimeStatus?.ChannelBotAuthorizationScopesCurrent == true
            ? "status-pill__dot bg-emerald-500"
            : "status-pill__dot bg-amber-500";

    private string _authorizationText =>
        _state?.IsChannelBotAuthorized == true
        && _state.RuntimeStatus?.ChannelBotAuthorizationScopesCurrent == true
            ? "connected"
        : _state?.IsChannelBotAuthorized == true ? "needs update"
        : "not connected";

    private string _operationsAuthorizationBadgeClass =>
        _state?.TwitchOperationsAuthorization is TwitchOperationsAuthorizationState.Ready
            ? "status-pill bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200"
            : "status-pill bg-amber-50 text-amber-700 ring-1 ring-amber-200";

    private string _operationsAuthorizationDotClass =>
        _state?.TwitchOperationsAuthorization is TwitchOperationsAuthorizationState.Ready
            ? "status-pill__dot bg-emerald-500"
            : "status-pill__dot bg-amber-500";

    private string _operationsAuthorizationText =>
        _state?.TwitchOperationsAuthorization switch
        {
            TwitchOperationsAuthorizationState.Ready => "connected",
            TwitchOperationsAuthorizationState.Stale => "needs update",
            _ => "not connected",
        };

    private string _runtimeBadgeClass =>
        _runtimeLifecycle?.Match(
            static _ => "status-pill bg-slate-100 text-slate-600 ring-1 ring-slate-200",
            static _ => "status-pill bg-orange-50 text-orange-700 ring-1 ring-orange-200",
            static _ => "status-pill bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200",
            static _ => "status-pill bg-purple-50 text-purple-700 ring-1 ring-purple-200"
        ) ?? "status-pill bg-slate-100 text-slate-600 ring-1 ring-slate-200";

    private string _runtimeDotClass =>
        _runtimeLifecycle?.Match(
            static _ => "status-pill__dot bg-slate-400",
            static _ => "status-pill__dot bg-orange-500",
            static _ => "status-pill__dot bg-emerald-500",
            static _ => "status-pill__dot bg-purple-500"
        ) ?? "status-pill__dot bg-slate-400";

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
            () => ClearChannelAuthorizationWithOwnerAuthorityAsync(hostId)
        );
    }

    private async Task ClearChannelAuthorizationWithOwnerAuthorityAsync(int hostId)
    {
        var pageContext = await LoadPageContextAsync();
        if (!pageContext.Session.CanAuthorizeSelectedHost)
        {
            _toasts.Publish(
                ToastRequest<ErrorToastStrategy>.WithTitle(
                    "Only the channel owner can change Chat access.",
                    "Channel connection not changed"
                )
            );
            return;
        }

        await RunSelectedHostMutationAsync(
            hostId,
            () => ClearChannelAuthorizationCoreAsync(hostId)
        );
    }

    private Task DisconnectTwitchIntegrationAsync(int hostId)
    {
        return ObserveUiOperationAsync(
            nameof(DisconnectTwitchIntegrationAsync),
            () => DisconnectTwitchIntegrationCoreAsync(hostId)
        );
    }

    private async Task DisconnectTwitchIntegrationCoreAsync(int hostId)
    {
        var outcome = await AuthorizeAndDisconnectTwitchIntegrationAsync(hostId);
        if (
            outcome
            is TwitchIntegrationDisconnectOutcome.AlreadyDisconnected
                or TwitchIntegrationDisconnectOutcome.Cleared
                or TwitchIntegrationDisconnectOutcome.ClearedWithNotificationFailures
                or TwitchIntegrationDisconnectOutcome.ClearedWithNotificationEscalation
        )
        {
            await LoadCoreAsync();
        }

        PublishTwitchIntegrationDisconnectToast(outcome);
    }

    private async Task<TwitchIntegrationDisconnectOutcome> AuthorizeAndDisconnectTwitchIntegrationAsync(
        int hostId
    )
    {
        var pageContext = await LoadPageContextAsync();
        var selectedHost = pageContext.Session.State.Match<BotHostChoice?>(
            _ => null,
            selected => selected.Selection.Current,
            _ => null
        );
        if (selectedHost?.Id != hostId)
        {
            return new TwitchIntegrationDisconnectOutcome.SelectedChannelChanged();
        }
        if (
            !pageContext.Session.CanAuthorizeSelectedHost
            || selectedHost.Role != AuthRole.Streamer
            || !string.Equals(
                selectedHost.Login,
                pageContext.Session.Login,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return new TwitchIntegrationDisconnectOutcome.OwnerAuthorityRequired();
        }

        return (
            await _hostBroadcasterAuthorization.ClearAsync(hostId, CancellationToken.None)
        ).Match<TwitchIntegrationDisconnectOutcome>(
            static _ => new TwitchIntegrationDisconnectOutcome.AlreadyDisconnected(),
            static _ => new TwitchIntegrationDisconnectOutcome.Cleared(),
            static failed => new TwitchIntegrationDisconnectOutcome.ClearedWithNotificationFailures(
                failed.FailureCount
            ),
            static escalation => new TwitchIntegrationDisconnectOutcome.ClearedWithNotificationEscalation(
                escalation.ObserverFailureCount,
                escalation.HandlingFailureCount
            )
        );
    }

    private void PublishTwitchIntegrationDisconnectToast(TwitchIntegrationDisconnectOutcome outcome)
    {
        switch (outcome)
        {
            case TwitchIntegrationDisconnectOutcome.SelectedChannelChanged:
                _toasts.Publish(
                    ToastRequest<ErrorToastStrategy>.WithTitle(
                        "Your selected channel changed. Choose the channel and try again.",
                        "Twitch integration not disconnected"
                    )
                );
                return;
            case TwitchIntegrationDisconnectOutcome.OwnerAuthorityRequired:
                _toasts.Publish(
                    ToastRequest<ErrorToastStrategy>.WithTitle(
                        "Only the channel owner can disconnect the Twitch integration.",
                        "Twitch integration not disconnected"
                    )
                );
                return;
            case TwitchIntegrationDisconnectOutcome.AlreadyDisconnected:
                _toasts.Publish(
                    ToastRequest<CautionStatusToastStrategy>.WithTitle(
                        "The Twitch integration was already disconnected.",
                        "Twitch integration disconnected"
                    )
                );
                return;
            case TwitchIntegrationDisconnectOutcome.Cleared:
                _toasts.Publish(
                    ToastRequest<CautionStatusToastStrategy>.WithTitle(
                        "The Twitch integration has been disconnected.",
                        "Twitch integration disconnected"
                    )
                );
                return;
            case TwitchIntegrationDisconnectOutcome.ClearedWithNotificationFailures:
            case TwitchIntegrationDisconnectOutcome.ClearedWithNotificationEscalation:
                _toasts.Publish(
                    ToastRequest<WarningToastStrategy>.WithTitle(
                        "The Twitch integration has been disconnected, but the running bot may need attention before it notices the change.",
                        "Twitch integration disconnected"
                    )
                );
                return;
            default:
                throw new UnreachableException();
        }
    }

    private async Task ClearChannelAuthorizationCoreAsync(int hostId)
    {
        await _channelBotAuthorization.ClearAsync(hostId, CancellationToken.None);
        await LoadCoreAsync();
        _toasts.Publish(
            new ToastRequest<StatusToastStrategy>("Chat access has been disconnected.")
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
            session.CurrentHostRoleIs(AuthRole.Admin)
                ? new HostBotAccountActor.BotAdministrator(session.UserId, session.Login)
                : new HostBotAccountActor.ChannelOwner(session.UserId, session.Login),
            CancellationToken.None
        );
        await LoadCoreAsync();
        if (outcome is not HostBotAccountClearOutcome.Cleared)
        {
            _toasts.Publish(
                ToastRequest<ErrorToastStrategy>.WithTitle(
                    "BlokeBot could not confirm that you can manage this channel. Sign in again and retry.",
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
                "Connect Chat access before starting the bot.",
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

internal abstract record TwitchIntegrationDisconnectOutcome
{
    private TwitchIntegrationDisconnectOutcome() { }

    internal sealed record SelectedChannelChanged : TwitchIntegrationDisconnectOutcome;

    internal sealed record OwnerAuthorityRequired : TwitchIntegrationDisconnectOutcome;

    internal sealed record AlreadyDisconnected : TwitchIntegrationDisconnectOutcome;

    internal sealed record Cleared : TwitchIntegrationDisconnectOutcome;

    internal sealed record ClearedWithNotificationFailures(int FailureCount)
        : TwitchIntegrationDisconnectOutcome;

    internal sealed record ClearedWithNotificationEscalation(
        int ObserverFailureCount,
        int HandlingFailureCount
    ) : TwitchIntegrationDisconnectOutcome;
}
