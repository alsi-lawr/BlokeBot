namespace BlokeBot.Core.Features.TwitchOperations.Polls;

public sealed record PollDashboardState(
    PollAuthorizationReadiness Authorization,
    PollView? ActivePoll,
    IReadOnlyList<PollTemplateView> Templates,
    IReadOnlyList<PollView> Results
);

public sealed record PollTemplateView(
    int Id,
    string Title,
    IReadOnlyList<string> Choices,
    int DurationSeconds,
    bool ChannelPointsVotingEnabled,
    int? ChannelPointsPerVote
);

public sealed record PollView(
    string ProviderPollId,
    string Title,
    IReadOnlyList<PollChoiceView> Choices,
    string Status,
    bool IsExternallyStarted,
    DateTime StartedAtUtc,
    DateTime? EndsAtUtc,
    DateTime? EndedAtUtc
);

public sealed record PollChoiceView(string Id, string Title, int Votes, int ChannelPointsVotes);

public abstract record PollAuthorizationReadiness
{
    private PollAuthorizationReadiness() { }

    public sealed record Ready : PollAuthorizationReadiness;

    public sealed record NeedsBroadcasterAuthorization(string Message) : PollAuthorizationReadiness;
}
