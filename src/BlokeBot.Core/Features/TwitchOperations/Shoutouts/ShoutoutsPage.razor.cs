using System.Diagnostics;
using BlokeBot.Core.Features.Toasts;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts;

public partial class ShoutoutsPage
{
    private ShoutoutDashboardState? _state;
    private string _targetLogin = string.Empty;

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
        await LoadPageContextAsync();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (HostId != 0)
        {
            _state = await _shoutouts.LoadAsync(HostId, _targetLogin, CancellationToken.None);
        }
    }

    private async Task SendAsync()
    {
        await RunSelectedHostMutationAsync(
            HostId,
            async () =>
            {
                var outcome = await _shoutouts.SendAsync(
                    HostId,
                    _targetLogin,
                    CancellationToken.None
                );
                switch (outcome)
                {
                    case ShoutoutOperationOutcome.Sent sent:
                        _toasts.Publish(
                            new ToastRequest<SuccessToastStrategy>(
                                $"Shoutout sent to @{sent.TargetLogin}."
                            )
                        );
                        break;
                    case ShoutoutOperationOutcome.TargetNotFound missing:
                        _toasts.Publish(
                            new ToastRequest<WarningToastStrategy>(
                                $"Twitch user @{missing.TargetLogin} was not found."
                            )
                        );
                        break;
                    case ShoutoutOperationOutcome.SelfTarget:
                        _toasts.Publish(
                            new ToastRequest<WarningToastStrategy>(
                                "You cannot shout out the selected channel."
                            )
                        );
                        break;
                    case ShoutoutOperationOutcome.TargetOffline offline:
                        _toasts.Publish(
                            new ToastRequest<WarningToastStrategy>(
                                $"@{offline.TargetLogin} must be live with viewers."
                            )
                        );
                        break;
                    case ShoutoutOperationOutcome.NotReady notReady:
                        _toasts.Publish(new ToastRequest<WarningToastStrategy>(notReady.Message));
                        break;
                    case ShoutoutOperationOutcome.CooldownActive cooldown:
                        _toasts.Publish(
                            new ToastRequest<WarningToastStrategy>(
                                $"Try again after {cooldown.EligibleAtUtc.ToLocalTime():g}."
                            )
                        );
                        break;
                    case ShoutoutOperationOutcome.CooldownUnknown:
                        _toasts.Publish(
                            new ToastRequest<WarningToastStrategy>(
                                "Twitch did not confirm the cooldown state."
                            )
                        );
                        break;
                    case ShoutoutOperationOutcome.ProviderRejected rejected:
                        _toasts.Publish(new ToastRequest<WarningToastStrategy>(rejected.Message));
                        break;
                    default:
                        throw new UnreachableException();
                }
                await LoadAsync();
            }
        );
    }
}
