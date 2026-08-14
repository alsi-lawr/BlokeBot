using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.RaidCollaboration;

public sealed record RaidCollaborationConfiguration(
    bool WelcomeEnabled,
    string WelcomeMessage,
    AutomaticRaidShoutoutConfiguration AutomaticShoutout,
    int DeduplicationWindowMinutes,
    string Language,
    IReadOnlyList<string> EligibleCategories,
    int RelationshipCooldownHours,
    IReadOnlyList<ApprovedRaidChannelDraft> ApprovedChannels,
    bool IncludeFollowedLiveChannels
)
{
    public static RaidCollaborationConfiguration Defaults { get; } =
        new(
            true,
            RaidCollaborationDefaults.WelcomeMessage,
            AutomaticRaidShoutoutConfiguration.Defaults,
            60,
            "en",
            [],
            336,
            [],
            false
        );
}

public sealed record ApprovedRaidChannelDraft(
    string Login,
    string DisplayName,
    string? ApprovedClipId
)
{
    public string? TwitchUserId { get; init; }
}

public sealed record RaidCollaborationDashboard(
    RaidCollaborationConfiguration Configuration,
    IReadOnlyList<RaidShortlistEntry> EligibleChannels,
    IReadOnlyList<RaidShortlistExclusion> ExcludedChannels,
    IReadOnlyList<RaidRelationshipHistory> History,
    RaidArrivalSummary? LatestArrival,
    ShoutoutDashboardState ShoutoutContext,
    IReadOnlyList<AutomaticRaidShoutoutOutcomeView> AutomaticShoutoutOutcomes,
    bool RaidManagementAuthorized,
    FollowedLiveSourceState FollowedLiveSource
);

public enum RaidCandidateProvenance
{
    Approved,
    Followed,
}

public enum FollowedLiveSourceState
{
    Disabled,
    Ready,
    AuthorizationRequired,
    Unavailable,
}

public sealed record RaidShortlistEntry(
    string TwitchUserId,
    string Login,
    string DisplayName,
    string StreamId,
    string Category,
    string Language,
    string Title,
    int ViewerCount,
    IReadOnlyList<string> EligibilityReasons,
    ApprovedRaidClip? ApprovedClip,
    RaidCandidateProvenance Provenance
);

public sealed record ApprovedRaidClip(
    string Id,
    string Url,
    string Title,
    DateTimeOffset CreatedAt,
    decimal DurationSeconds
);

public sealed record RaidShortlistExclusion(string Login, IReadOnlyList<string> Reasons);

public sealed record RaidRelationshipHistory(
    RaidDirection Direction,
    string TwitchUserId,
    string Login,
    string DisplayName,
    int ViewerCount,
    string? Category,
    string? StreamId,
    DateTimeOffset OccurredAt,
    RaidWelcomeOutcome WelcomeOutcome,
    RaidShoutoutOutcome ShoutoutOutcome
);

public sealed record RaidArrivalSummary(
    string Login,
    string DisplayName,
    int ViewerCount,
    string? Category,
    DateTimeOffset OccurredAt,
    RaidWelcomeOutcome WelcomeOutcome,
    RaidShoutoutOutcome ShoutoutOutcome
);

public abstract record RaidCollaborationLoadOutcome
{
    private RaidCollaborationLoadOutcome() { }

    public sealed record Loaded(RaidCollaborationDashboard Dashboard)
        : RaidCollaborationLoadOutcome;

    public sealed record FeatureDisabled : RaidCollaborationLoadOutcome;

    public sealed record HostNotFound : RaidCollaborationLoadOutcome;

    public sealed record ProviderUnavailable : RaidCollaborationLoadOutcome;
}

public abstract record RaidCollaborationSaveOutcome
{
    private RaidCollaborationSaveOutcome() { }

    public sealed record Saved(RaidCollaborationConfiguration Configuration)
        : RaidCollaborationSaveOutcome;

    public sealed record Invalid(
        IReadOnlyList<string> Errors,
        IReadOnlyList<AutomaticRaidShoutoutValidationError> ShoutoutErrors
    ) : RaidCollaborationSaveOutcome;

    public sealed record FeatureDisabled : RaidCollaborationSaveOutcome;

    public sealed record HostNotFound : RaidCollaborationSaveOutcome;

    public sealed record FollowedLiveAuthorizationRequired(
        RaidCollaborationConfiguration Configuration
    ) : RaidCollaborationSaveOutcome;
}

public abstract record ApproveRaidChannelOutcome
{
    private ApproveRaidChannelOutcome() { }

    public sealed record Approved(ApprovedRaidChannelDraft Channel) : ApproveRaidChannelOutcome;

    public sealed record AlreadyApproved : ApproveRaidChannelOutcome;

    public sealed record LimitReached(int Limit) : ApproveRaidChannelOutcome;

    public sealed record FeatureDisabled : ApproveRaidChannelOutcome;
}

public abstract record ConfirmedRaidStartOutcome
{
    private ConfirmedRaidStartOutcome() { }

    public sealed record Started(string TargetLogin) : ConfirmedRaidStartOutcome;

    public sealed record FeatureDisabled : ConfirmedRaidStartOutcome;

    public sealed record TargetNotApproved : ConfirmedRaidStartOutcome;

    public sealed record TargetIneligible(IReadOnlyList<string> Reasons)
        : ConfirmedRaidStartOutcome;

    public sealed record AuthorizationRequired : ConfirmedRaidStartOutcome;

    public sealed record ProviderRejected : ConfirmedRaidStartOutcome;
}

public abstract record RaidChannelSnapshotOutcome
{
    private RaidChannelSnapshotOutcome() { }

    public sealed record Available(RaidChannelSnapshot Snapshot) : RaidChannelSnapshotOutcome;

    public sealed record Offline(string Login) : RaidChannelSnapshotOutcome;

    public sealed record NotFound(string Login) : RaidChannelSnapshotOutcome;

    public sealed record Unavailable : RaidChannelSnapshotOutcome;
}

public abstract record FollowedLiveChannelsOutcome
{
    private FollowedLiveChannelsOutcome() { }

    public sealed record Available(IReadOnlyList<RaidChannelSnapshot> Channels)
        : FollowedLiveChannelsOutcome;

    public sealed record AuthorizationRequired : FollowedLiveChannelsOutcome;

    public sealed record Unavailable : FollowedLiveChannelsOutcome;
}

public sealed record RaidChannelSnapshot(
    string TwitchUserId,
    string Login,
    string DisplayName,
    string StreamId,
    string Category,
    string Language,
    string Title,
    int ViewerCount,
    ApprovedRaidClip? ApprovedClip
);

public interface IRaidCollaborationProvider
{
    Task<RaidChannelSnapshotOutcome> LoadLiveChannelAsync(
        int hostId,
        string login,
        string? approvedClipId,
        CancellationToken cancellationToken
    );

    Task<RaidChannelSnapshotOutcome> LoadLiveChannelByIdAsync(
        int hostId,
        string twitchUserId,
        string? approvedClipId,
        CancellationToken cancellationToken
    );

    Task<FollowedLiveChannelsOutcome> LoadFollowedLiveChannelsAsync(
        int hostId,
        CancellationToken cancellationToken
    );

    Task<bool> HasFollowedLiveAuthorizationAsync(int hostId, CancellationToken cancellationToken);

    Task<ConfirmedRaidStartOutcome> StartConfirmedRaidAsync(
        int hostId,
        string targetTwitchUserId,
        string targetLogin,
        CancellationToken cancellationToken
    );

    Task<bool> HasRaidManagementAuthorizationAsync(int hostId, CancellationToken cancellationToken);
}

public interface IRaidWelcomeSender
{
    Task<bool> SendAsync(
        int hostId,
        string hostLogin,
        string providerMessageId,
        string message,
        CancellationToken cancellationToken
    );
}

public enum RaidCollaborationDomainEventKind
{
    IncomingRaidRecorded,
    OutgoingRaidRecorded,
    WelcomeDelivered,
    NativeShoutoutSent,
}

public sealed record RaidCollaborationDomainEvent(
    int HostId,
    RaidCollaborationDomainEventKind Kind,
    string ProviderMessageId,
    RaidDirection Direction,
    string ChannelTwitchUserId,
    string ChannelLogin,
    string ChannelDisplayName,
    int ViewerCount,
    string? Category,
    string? StreamId,
    DateTimeOffset OccurredAt
);

public interface IRaidCollaborationDomainEventObserver
{
    ValueTask CollaborationEventAsync(
        RaidCollaborationDomainEvent domainEvent,
        CancellationToken cancellationToken
    );
}
