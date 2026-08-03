using System.Diagnostics;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts;

public partial class ShoutoutsPage
{
    private ShoutoutDashboardState? _state;
    private string _targetLogin = string.Empty;
    private bool _nativeTwitchEnabled;
    private bool _loading = true;
    private bool _loadFailed;

    private string _cooldownText =>
        _state switch
        {
            {
                GlobalEligibleAtUtc: { } global,
                TargetCooldown: ShoutoutTargetCooldownReadiness.EligibleAt target
            } =>
                $"Next global shoutout: {global.ToLocalTime():g}. @{_targetLogin} is eligible at {target.Value.ToLocalTime():g}.",
            { GlobalEligibleAtUtc: { } global } =>
                $"You can send another shoutout after {global.ToLocalTime():g}. No separate time is available yet for @{_targetLogin}.",
            { TargetCooldown: ShoutoutTargetCooldownReadiness.EligibleAt target } =>
                $"@{_targetLogin} can be shouted out after {target.Value.ToLocalTime():g}. The overall next-send time is not available yet.",
            _ => "No cooldown time is available yet. Try sending when you are ready.",
        };

    protected override async Task OnInitializedAsync()
    {
        _ = TrackSubscription(
            _events.SubscribeForComponentRefresh(
                [AppEventKind.HostedChannelsChanged, AppEventKind.TwitchOperationsChanged],
                InvokeAsync,
                LoadAsync,
                StateHasChanged
            )
        );
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadFailed = false;
        try
        {
            _ = await LoadPageContextAsync();
            _nativeTwitchEnabled =
                HostId != 0
                && await _nativeTwitch.IsEnabledAsync(
                    HostId,
                    HostFeatureFlags.Shoutouts,
                    CancellationToken.None
                );
            var state = _nativeTwitchEnabled
                ? await _shoutouts.LoadAsync(HostId, _targetLogin, CancellationToken.None)
                : null;
            _nativeTwitchEnabled =
                state is not null
                && await _nativeTwitch.IsEnabledAsync(
                    HostId,
                    HostFeatureFlags.Shoutouts,
                    CancellationToken.None
                );
            _state = _nativeTwitchEnabled ? state : null;
        }
        catch (Exception exception)
        {
            _state = null;
            _nativeTwitchEnabled = false;
            _loadFailed = true;
            ReportUiFault(nameof(LoadAsync), exception);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SendAsync()
    {
        var hostId = HostId;
        await RunSelectedHostMutationAsync(
            hostId,
            async () =>
            {
                var outcome = await _shoutouts.SendAsync(
                    hostId,
                    _targetLogin,
                    CancellationToken.None
                );
                var (message, success) = outcome switch
                {
                    ShoutoutOperationOutcome.Sent sent => (
                        $"Shoutout sent to @{sent.TargetLogin}.",
                        true
                    ),
                    ShoutoutOperationOutcome.TargetNotFound missing => (
                        $"Twitch user @{missing.TargetLogin} was not found.",
                        false
                    ),
                    ShoutoutOperationOutcome.SelfTarget => (
                        "You cannot shout out the selected channel.",
                        false
                    ),
                    ShoutoutOperationOutcome.TargetOffline offline => (
                        $"@{offline.TargetLogin} must be live with viewers.",
                        false
                    ),
                    ShoutoutOperationOutcome.NotReady => (
                        "Connect the bot account to Twitch, then try again.",
                        false
                    ),
                    ShoutoutOperationOutcome.CooldownActive cooldown => (
                        $"Try again after {cooldown.EligibleAtUtc.ToLocalTime():g}.",
                        false
                    ),
                    ShoutoutOperationOutcome.CooldownUnknown => (
                        "Twitch did not confirm the cooldown state.",
                        false
                    ),
                    ShoutoutOperationOutcome.ProviderRejected => (
                        "Twitch could not send this shoutout. Check the channel name and try again.",
                        false
                    ),
                    _ => throw new UnreachableException(),
                };
                if (success)
                {
                    _ = _toasts.Publish(new ToastRequest<SuccessToastStrategy>(message));
                }
                else
                {
                    _ = _toasts.Publish(new ToastRequest<WarningToastStrategy>(message));
                }
                await LoadAsync();
            }
        );
    }

    private Task RunAutomaticRaidMutationAsync(int hostId, Func<Task> mutation) =>
        RunSelectedHostMutationAsync(hostId, mutation);
}
