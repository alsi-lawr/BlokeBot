using System.Diagnostics;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Toasts;

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
                $"Next global shoutout: {global.ToLocalTime():g}. Twitch has not supplied a same-target cooldown for @{_targetLogin}.",
            { TargetCooldown: ShoutoutTargetCooldownReadiness.EligibleAt target } =>
                $"Global cooldown is unknown. @{_targetLogin} is eligible at {target.Value.ToLocalTime():g}.",
            _ => "Twitch has not supplied applicable cooldown metadata yet.",
        };

    protected override async Task OnInitializedAsync()
    {
        TrackSubscription(
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
            await LoadPageContextAsync();
            _nativeTwitchEnabled =
                HostId != 0 && await _nativeTwitch.IsEnabledAsync(HostId, CancellationToken.None);
            var state = _nativeTwitchEnabled
                ? await _shoutouts.LoadAsync(HostId, _targetLogin, CancellationToken.None)
                : null;
            _nativeTwitchEnabled =
                state is not null
                && await _nativeTwitch.IsEnabledAsync(HostId, CancellationToken.None);
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
                    ShoutoutOperationOutcome.NotReady notReady => (notReady.Message, false),
                    ShoutoutOperationOutcome.CooldownActive cooldown => (
                        $"Try again after {cooldown.EligibleAtUtc.ToLocalTime():g}.",
                        false
                    ),
                    ShoutoutOperationOutcome.CooldownUnknown => (
                        "Twitch did not confirm the cooldown state.",
                        false
                    ),
                    ShoutoutOperationOutcome.ProviderRejected rejected => (rejected.Message, false),
                    _ => throw new UnreachableException(),
                };
                if (success)
                {
                    _toasts.Publish(new ToastRequest<SuccessToastStrategy>(message));
                }
                else
                {
                    _toasts.Publish(new ToastRequest<WarningToastStrategy>(message));
                }
                await LoadAsync();
            }
        );
    }
}
