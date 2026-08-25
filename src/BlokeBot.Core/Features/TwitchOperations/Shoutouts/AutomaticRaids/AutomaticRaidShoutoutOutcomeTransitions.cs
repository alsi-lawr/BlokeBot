using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;

internal sealed record AutomaticRaidOutcomeIdentity(
    int HostId,
    long OutcomeId,
    string ProviderMessageId
);

internal sealed record AutomaticRaidOutcomeState(
    AutomaticRaidShoutoutOutcomeStatus Status,
    AutomaticRaidShoutoutResultCode? ResultCode,
    DateTime? CompletedAtUtc
);

internal abstract record AutomaticRaidOutcomeTransition
{
    private AutomaticRaidOutcomeTransition() { }

    internal sealed record QueueAccepted : AutomaticRaidOutcomeTransition;

    internal sealed record TransportDelivered : AutomaticRaidOutcomeTransition;

    internal sealed record TerminalFailure(AutomaticRaidShoutoutResultCode ResultCode)
        : AutomaticRaidOutcomeTransition;

    internal sealed record Ambiguous : AutomaticRaidOutcomeTransition;

    internal sealed record PinFailed : AutomaticRaidOutcomeTransition;
}

internal abstract record AutomaticRaidOutcomeTransitionResult
{
    private AutomaticRaidOutcomeTransitionResult() { }

    internal sealed record Applied(
        AutomaticRaidOutcomeIdentity Identity,
        AutomaticRaidOutcomeState State
    ) : AutomaticRaidOutcomeTransitionResult;

    internal sealed record Unchanged(
        AutomaticRaidOutcomeIdentity Identity,
        AutomaticRaidOutcomeState State
    ) : AutomaticRaidOutcomeTransitionResult;

    internal sealed record NotFound : AutomaticRaidOutcomeTransitionResult;
}
