namespace BlokeBot.Twitch.Runtime;

public interface IPollEventObserver
{
    Task PollReceivedAsync(EventSubPollEvent poll, CancellationToken cancellationToken);
}

public sealed record EventSubPollEvent(
    string BroadcasterUserId,
    string BroadcasterUserLogin,
    string PollId,
    string Title,
    IReadOnlyList<EventSubPollChoice> Choices,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndsAt,
    string MessageId,
    EventSubPollStage Stage
);

public enum EventSubPollStage
{
    Begin,
    Progress,
    End,
}

public sealed record EventSubPollChoice(string Id, string Title, int Votes, int ChannelPointsVotes);
