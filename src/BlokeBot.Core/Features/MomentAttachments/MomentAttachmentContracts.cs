using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Competitions;

namespace BlokeBot.Core.Features.MomentAttachments;

public abstract record MomentAttachmentDestination
{
    private MomentAttachmentDestination() { }

    public sealed record Bounty(Guid Id) : MomentAttachmentDestination;

    public sealed record Achievement(CommunityDefinitionId Id) : MomentAttachmentDestination;

    public sealed record TournamentResult(CompetitionMatchId Id) : MomentAttachmentDestination;
}

public enum MomentAttachmentSectionAvailability
{
    Available,
    ParentDisabled,
    DestinationUnavailable,
}

public sealed record MomentAttachmentDestinationView(
    string Kind,
    string Title,
    string Context,
    string State,
    string Visibility
);

public sealed record MomentAttachmentMomentView(
    Guid Id,
    string Title,
    string Category,
    string StreamIdentity,
    string? ProviderUrl,
    string SourceUrl,
    DateTime CapturedAtUtc,
    DateTime ApprovedAtUtc,
    bool IsAttached
);

public sealed record MomentAttachmentSectionView(
    MomentAttachmentSectionAvailability Availability,
    string DisabledParents,
    MomentAttachmentDestinationView? Destination,
    IReadOnlyList<MomentAttachmentMomentView> Attached,
    IReadOnlyList<MomentAttachmentMomentView> Discoverable
);

public sealed record MomentAttachmentPublicProjection(
    MomentAttachmentDestinationView Destination,
    IReadOnlyList<MomentAttachmentMomentView> Moments
);

public abstract record MomentAttachmentMutationOutcome
{
    private MomentAttachmentMutationOutcome() { }

    public sealed record Succeeded(bool WasIdempotent) : MomentAttachmentMutationOutcome;

    public sealed record Rejected(MomentAttachmentRejection Reason)
        : MomentAttachmentMutationOutcome;
}

public abstract record MomentAttachmentRejection(string Message)
{
    public sealed record Unauthorized()
        : MomentAttachmentRejection(
            "You are not authorised to change Moment links for this channel."
        );

    public sealed record ParentDisabled(string Parents)
        : MomentAttachmentRejection(
            $"Turn on {Parents} in Channel setup before changing Moment links."
        );

    public sealed record DestinationUnavailable()
        : MomentAttachmentRejection("This destination is no longer available for Moment links.");

    public sealed record MomentUnavailable()
        : MomentAttachmentRejection(
            "Only currently approved Moments from this channel can be attached."
        );
}
