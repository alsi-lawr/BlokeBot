using System.Diagnostics;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.TwitchOperations.Polls.Page;

public partial class PollsPage
{
    private PollDashboardState? _state;
    private string _title = string.Empty;
    private string _choices = string.Empty;
    private string _duration = "60";
    private bool _channelPointsVotingEnabled;
    private string _channelPointsPerVote = string.Empty;
    private bool _nativeTwitchEnabled;
    private bool _loading = true;
    private bool _loadFailed;

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
                HostId != 0
                && await _nativeTwitch.IsEnabledAsync(
                    HostId,
                    HostFeatureFlags.Polls,
                    CancellationToken.None
                );
            _state = _nativeTwitchEnabled
                ? await _polls.LoadAsync(HostId, CancellationToken.None)
                : null;
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

    private async Task SaveTemplateAsync()
    {
        if (!int.TryParse(_duration, out var duration))
        {
            Warn("Poll duration must be a number.");
            return;
        }
        int? pointsPerVote = null;
        if (_channelPointsVotingEnabled)
        {
            if (!int.TryParse(_channelPointsPerVote, out var parsed))
            {
                Warn("Channel Points cost must be a whole number from 1 to 1,000,000.");
                return;
            }

            pointsPerVote = parsed;
        }

        await MutateAsync(async hostId =>
        {
            var outcome = await _polls.SaveTemplateAsync(
                hostId,
                new(
                    _title,
                    _choices.Split('\n'),
                    duration,
                    _channelPointsVotingEnabled,
                    pointsPerVote
                ),
                CancellationToken.None
            );
            Publish(outcome);
        });
    }

    private Task StartPollAsync(int templateId) =>
        MutateAsync(async hostId =>
            Publish(await _polls.StartAsync(hostId, templateId, CancellationToken.None))
        );

    private async Task EndPollAsync()
    {
        var confirmed =
            _state?.ActivePoll?.IsExternallyStarted != true
            || await _js.InvokeAsync<bool>("confirm", ["End the externally started Twitch poll?"]);
        await MutateAsync(async hostId =>
            Publish(await _polls.EndAsync(hostId, confirmed, CancellationToken.None))
        );
    }

    private async Task MutateAsync(Func<int, Task> operation)
    {
        var hostId = HostId;
        await RunSelectedHostMutationAsync(
            hostId,
            async () =>
            {
                await operation(hostId);
                await LoadAsync();
            }
        );
    }

    private void Publish(PollOperationOutcome outcome)
    {
        var (message, success) = outcome switch
        {
            PollOperationOutcome.Started => ("Poll started.", true),
            PollOperationOutcome.Ended => ("Poll ended.", true),
            PollOperationOutcome.TemplateSaved => ("Poll template saved.", true),
            PollOperationOutcome.ActivePollExists => ("Twitch already has an active poll.", false),
            PollOperationOutcome.TemplateNotFound => (
                "That poll template is no longer available.",
                false
            ),
            PollOperationOutcome.ConfirmationRequired => (
                "Confirm before ending a poll started outside BlokeBot.",
                false
            ),
            PollOperationOutcome.NotReady => (
                "Reconnect this channel to Twitch, then try again.",
                false
            ),
            PollOperationOutcome.InvalidTemplate invalid => (invalid.Message, false),
            PollOperationOutcome.ProviderRejected => (
                "Twitch could not complete that poll action. Reload the page before trying again.",
                false
            ),
            _ => throw new UnreachableException(),
        };
        if (success)
        {
            _toasts.Publish(new ToastRequest<SuccessToastStrategy>(message));
        }
        else
        {
            Warn(message);
        }
    }

    private void Warn(string message) =>
        _toasts.Publish(new ToastRequest<WarningToastStrategy>(message));
}
