namespace BlokeBot.Core.Features.TwitchOperations.Polls;

public sealed record PollControlState(
    PollAuthorizationReadiness Authorization,
    PollTemplateView? Template,
    PollView? ActivePoll
);
