using System.Diagnostics;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts;

public partial class ShoutoutsPage
{
    private ShoutoutDashboardState? _state;
    private string _targetLogin = string.Empty;
    private PollDashboardState? _pollState;
    private string _pollTitle = string.Empty;
    private string _pollChoices = string.Empty;
    private string _pollDuration = "60";
    private bool _channelPointsVotingEnabled;
    private string _channelPointsPerVote = string.Empty;
    private ClipMarkerDashboardState? _clipMarkerState;
    private string _clipRequestKey = Guid.NewGuid().ToString("N");
    private bool _clipHasDelay;
    private string _markerRequestKey = Guid.NewGuid().ToString("N");
    private string _markerDescription = string.Empty;
    private bool _nativeTwitchEnabled;

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
                ReloadForEventAsync,
                StateHasChanged
            )
        );
        await LoadPageContextAsync();
        await ReloadForSelectedHostAsync();
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

    private async Task LoadPollsAsync()
    {
        if (HostId != 0)
        {
            _pollState = await _polls.LoadAsync(HostId, CancellationToken.None);
        }
    }

    private async Task ReloadForEventAsync()
    {
        await LoadPageContextAsync();
        await ReloadForSelectedHostAsync();
    }

    private async Task ReloadForSelectedHostAsync()
    {
        _nativeTwitchEnabled =
            HostId != 0 && await _nativeTwitch.IsEnabledAsync(HostId, CancellationToken.None);
        if (!_nativeTwitchEnabled)
        {
            _state = null;
            _pollState = null;
            _clipMarkerState = null;
            return;
        }

        await LoadAsync();
        await LoadPollsAsync();
        await LoadClipsMarkersAsync();
    }

    private async Task LoadClipsMarkersAsync()
    {
        if (HostId != 0)
        {
            _clipMarkerState = await _clipsMarkers.LoadAsync(HostId, CancellationToken.None);
        }
    }

    private async Task CreateClipAsync()
    {
        var hostId = HostId;
        await RunSelectedHostMutationAsync(
            hostId,
            async () =>
            {
                var outcome = await _clipsMarkers.CreateClipAsync(
                    hostId,
                    _clipRequestKey,
                    _clipHasDelay,
                    CancellationToken.None
                );
                PublishClipMarkerOutcome(outcome);
                await LoadClipsMarkersAsync();
            }
        );
    }

    private async Task CreateMarkerAsync()
    {
        var hostId = HostId;
        await RunSelectedHostMutationAsync(
            hostId,
            async () =>
            {
                var outcome = await _clipsMarkers.CreateMarkerAsync(
                    hostId,
                    _markerRequestKey,
                    _markerDescription,
                    CancellationToken.None
                );
                PublishClipMarkerOutcome(outcome);
                await LoadClipsMarkersAsync();
            }
        );
    }

    private void PublishClipMarkerOutcome(ClipMarkerOperationOutcome outcome)
    {
        var message = outcome switch
        {
            ClipMarkerOperationOutcome.ClipPending => "Clip requested; Twitch is preparing it.",
            ClipMarkerOperationOutcome.ClipAvailable => "Clip is available.",
            ClipMarkerOperationOutcome.MarkerCreated => "Stream marker created.",
            ClipMarkerOperationOutcome.NotReady notReady => notReady.Message,
            ClipMarkerOperationOutcome.InvalidRequest invalid => invalid.Message,
            ClipMarkerOperationOutcome.Offline => "Twitch reports that the channel is offline.",
            ClipMarkerOperationOutcome.VodsDisabled =>
                "Twitch reports that VOD or clip creation is disabled.",
            ClipMarkerOperationOutcome.RerunOrPremiere =>
                "Twitch reports that this stream cannot create clips or markers.",
            ClipMarkerOperationOutcome.Ambiguous =>
                "Twitch did not confirm whether the request completed. Reuse the same request key to view its outcome.",
            ClipMarkerOperationOutcome.ProviderRejected rejected => rejected.Message,
            ClipMarkerOperationOutcome.ClipFailed failed => failed.Clip.FailureReason
                ?? "Twitch did not create the clip.",
            ClipMarkerOperationOutcome.MarkerFailed failed => failed.Marker.FailureReason
                ?? "Twitch did not create the marker.",
            _ => throw new UnreachableException(),
        };
        if (
            outcome
            is ClipMarkerOperationOutcome.ClipPending
                or ClipMarkerOperationOutcome.ClipAvailable
                or ClipMarkerOperationOutcome.MarkerCreated
        )
        {
            _toasts.Publish(new ToastRequest<SuccessToastStrategy>(message));
            return;
        }

        _toasts.Publish(new ToastRequest<WarningToastStrategy>(message));
    }

    private async Task SavePollTemplateAsync()
    {
        if (!int.TryParse(_pollDuration, out var duration))
        {
            _toasts.Publish(
                new ToastRequest<WarningToastStrategy>("Poll duration must be a number.")
            );
            return;
        }
        int? pointsPerVote = null;
        if (_channelPointsVotingEnabled)
        {
            if (!int.TryParse(_channelPointsPerVote, out var channelPointsPerVote))
            {
                _toasts.Publish(
                    new ToastRequest<WarningToastStrategy>(
                        "Channel Points cost must be a whole number from 1 to 1,000,000."
                    )
                );
                return;
            }

            pointsPerVote = channelPointsPerVote;
        }

        var hostId = HostId;
        await RunSelectedHostMutationAsync(
            hostId,
            async () =>
            {
                var outcome = await _polls.SaveTemplateAsync(
                    hostId,
                    new(
                        _pollTitle,
                        _pollChoices.Split('\n'),
                        duration,
                        _channelPointsVotingEnabled,
                        pointsPerVote
                    ),
                    CancellationToken.None
                );
                if (outcome is PollOperationOutcome.InvalidTemplate invalid)
                {
                    _toasts.Publish(new ToastRequest<WarningToastStrategy>(invalid.Message));
                }
                await LoadPollsAsync();
            }
        );
    }

    private async Task StartPollAsync(int templateId)
    {
        var hostId = HostId;
        await RunSelectedHostMutationAsync(
            hostId,
            async () =>
            {
                var outcome = await _polls.StartAsync(hostId, templateId, CancellationToken.None);
                if (outcome is PollOperationOutcome.NotReady notReady)
                {
                    _toasts.Publish(new ToastRequest<WarningToastStrategy>(notReady.Message));
                }
                else if (outcome is PollOperationOutcome.ActivePollExists)
                {
                    _toasts.Publish(
                        new ToastRequest<WarningToastStrategy>("Twitch already has an active poll.")
                    );
                }
                await LoadPollsAsync();
            }
        );
    }

    private async Task EndPollAsync()
    {
        var hostId = HostId;
        await RunSelectedHostMutationAsync(
            hostId,
            async () =>
            {
                var confirmed =
                    _pollState?.ActivePoll?.IsExternallyStarted != true
                    || await _js.InvokeAsync<bool>(
                        "confirm",
                        "End the externally started Twitch poll?"
                    );
                var outcome = await _polls.EndAsync(hostId, confirmed, CancellationToken.None);
                if (outcome is PollOperationOutcome.NotReady notReady)
                {
                    _toasts.Publish(new ToastRequest<WarningToastStrategy>(notReady.Message));
                }
                else if (outcome is PollOperationOutcome.ProviderRejected rejected)
                {
                    _toasts.Publish(new ToastRequest<WarningToastStrategy>(rejected.Message));
                }
                await LoadPollsAsync();
            }
        );
    }
}
