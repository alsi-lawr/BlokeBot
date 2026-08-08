using System.Diagnostics;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.TwitchOperations.Polls.Page;

public partial class PollsPage
{
    private string _title = string.Empty;
    private string _choices = string.Empty;
    private string _duration = "60";
    private bool _channelPointsVotingEnabled;
    private string _channelPointsPerVote = string.Empty;

    protected override HostFeatureFlags Feature => HostFeatureFlags.Polls;

    protected override async Task<PollDashboardState?> LoadStateAsync(
        int hostId,
        CancellationToken cancellationToken
    ) => await _polls.LoadAsync(hostId, cancellationToken);

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
            State?.ActivePoll?.IsExternallyStarted != true
            || await _js.InvokeAsync<bool>("confirm", ["End the externally started Twitch poll?"]);
        await MutateAsync(async hostId =>
            Publish(await _polls.EndAsync(hostId, confirmed, CancellationToken.None))
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
        Publish(message, success);
    }
}
