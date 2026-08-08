using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

/// <summary>
/// Maps native Twitch operation EventSub payloads to bounded automation contexts. Every context
/// carries only the documented fields below; access tokens and raw transport headers are never
/// included.
/// <list type="bullet">
/// <item><c>shoutout-sent</c>/<c>shoutout-received</c>: the other broadcaster as the sensitive
/// actor plus safe <c>viewer_count</c>.</item>
/// <item><c>poll-*</c>: safe <c>poll_id</c>, <c>poll_title</c>, <c>poll_status</c>
/// (<c>active</c>, <c>completed</c>, <c>terminated</c>, <c>archived</c>, or <c>unknown</c>), and
/// <c>total_votes</c>.</item>
/// <item><c>prediction-*</c>: safe <c>prediction_id</c>, <c>prediction_title</c>, and
/// <c>prediction_status</c> (<c>active</c>, <c>locked</c>, <c>resolved</c>, or <c>canceled</c>),
/// plus safe <c>winning_outcome_id</c> and <c>winning_outcome_title</c> when Twitch reports a
/// winner.</item>
/// </list>
/// </summary>
internal static class NativeOperationAutomationContext
{
    internal static AutomationContext Shoutout(
        BotHost host,
        AutomationDefinitionId definitionId,
        EventSubShoutoutEvent shoutout,
        DateTimeOffset receivedAtUtc
    ) =>
        TwitchEventAutomationContext.Create(
            host,
            definitionId,
            shoutout.Direction == EventSubShoutoutDirection.Sent
                ? TwitchEventAutomationContext.Actor(
                    shoutout.ToBroadcasterUserId,
                    shoutout.ToBroadcasterUserLogin,
                    string.Empty
                )
                : TwitchEventAutomationContext.Actor(
                    shoutout.FromBroadcasterUserId,
                    shoutout.FromBroadcasterUserLogin,
                    string.Empty
                ),
            stream: null,
            shoutout.StartedAt,
            receivedAtUtc,
            new Dictionary<AutomationVariableName, AutomationVariable>
            {
                [new("viewer_count")] = TwitchEventAutomationContext.SafeNumber(
                    shoutout.ViewerCount
                ),
            }
        );

    internal static AutomationContext Poll(
        BotHost host,
        AutomationDefinitionId definitionId,
        EventSubPollEvent poll,
        DateTimeOffset receivedAtUtc
    ) =>
        TwitchEventAutomationContext.Create(
            host,
            definitionId,
            actor: null,
            stream: null,
            poll.Stage == EventSubPollStage.Begin ? poll.StartedAt : receivedAtUtc,
            receivedAtUtc,
            new Dictionary<AutomationVariableName, AutomationVariable>
            {
                [new("poll_id")] = TwitchEventAutomationContext.SafeText(
                    TwitchEventAutomationContext.Bound(poll.PollId)
                ),
                [new("poll_title")] = TwitchEventAutomationContext.SafeText(
                    TwitchEventAutomationContext.Bound(poll.Title)
                ),
                [new("poll_status")] = TwitchEventAutomationContext.SafeText(PollStatusToken(poll)),
                [new("total_votes")] = TwitchEventAutomationContext.SafeNumber(
                    poll.Choices.Sum(static choice => choice.Votes)
                ),
            }
        );

    internal static AutomationContext Prediction(
        BotHost host,
        AutomationDefinitionId definitionId,
        EventSubPredictionEvent prediction,
        DateTimeOffset receivedAtUtc
    )
    {
        var variables = new Dictionary<AutomationVariableName, AutomationVariable>
        {
            [new("prediction_id")] = TwitchEventAutomationContext.SafeText(
                TwitchEventAutomationContext.Bound(prediction.PredictionId)
            ),
            [new("prediction_title")] = TwitchEventAutomationContext.SafeText(
                TwitchEventAutomationContext.Bound(prediction.Title)
            ),
            [new("prediction_status")] = TwitchEventAutomationContext.SafeText(
                TwitchEventAutomationContext.Bound(prediction.Status)
            ),
        };
        if (prediction.WinningOutcomeId is { Length: > 0 } winningOutcomeId)
        {
            variables[new("winning_outcome_id")] = TwitchEventAutomationContext.SafeText(
                TwitchEventAutomationContext.Bound(winningOutcomeId)
            );
            var winner = prediction.Outcomes.FirstOrDefault(outcome =>
                outcome.Id == winningOutcomeId
            );
            if (winner is not null)
            {
                variables[new("winning_outcome_title")] = TwitchEventAutomationContext.SafeText(
                    TwitchEventAutomationContext.Bound(winner.Title)
                );
            }
        }

        var occurredAtUtc = prediction.Stage switch
        {
            EventSubPredictionStage.Begin => prediction.CreatedAt,
            EventSubPredictionStage.End => prediction.EndedAt ?? receivedAtUtc,
            _ => receivedAtUtc,
        };
        return TwitchEventAutomationContext.Create(
            host,
            definitionId,
            actor: null,
            stream: null,
            occurredAtUtc,
            receivedAtUtc,
            variables
        );
    }

    private static string PollStatusToken(EventSubPollEvent poll) =>
        poll.Stage is EventSubPollStage.Begin or EventSubPollStage.Progress
            ? "active"
            : poll.Status.ToLowerInvariant() switch
            {
                "completed" => "completed",
                "terminated" => "terminated",
                "archived" => "archived",
                _ => "unknown",
            };
}
