using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext(DbContextOptions<BlokeBotDbContext> options)
    : DbContext(options)
{
    public DbSet<BotHost> Hosts => Set<BotHost>();
    public DbSet<HostBotAccountSettings> HostBotAccountSettings => Set<HostBotAccountSettings>();
    public DbSet<WhisperQuotaBucket> WhisperQuotaBuckets => Set<WhisperQuotaBucket>();
    public DbSet<WhisperQuotaRecipient> WhisperQuotaRecipients => Set<WhisperQuotaRecipient>();
    public DbSet<ReplyDeliverySetting> ReplyDeliverySettings => Set<ReplyDeliverySetting>();
    public DbSet<BotReplySettings> ReplySettings => Set<BotReplySettings>();
    public DbSet<CommandAlias> CommandAliases => Set<CommandAlias>();
    public DbSet<CustomMessageLibraryEntry> CustomMessageLibraryEntries =>
        Set<CustomMessageLibraryEntry>();
    public DbSet<CustomMessageVariant> CustomMessageVariants => Set<CustomMessageVariant>();
    public DbSet<CustomCommand> CustomCommands => Set<CustomCommand>();
    public DbSet<CustomCommandAction> CustomCommandActions => Set<CustomCommandAction>();
    public DbSet<CustomCommandAlias> CustomCommandAliases => Set<CustomCommandAlias>();
    public DbSet<CustomCommandAllowedUser> CustomCommandAllowedUsers =>
        Set<CustomCommandAllowedUser>();
    public DbSet<CustomCommandInvocationClaim> CustomCommandInvocationClaims =>
        Set<CustomCommandInvocationClaim>();
    public DbSet<CustomCommandInvocationResetAudit> CustomCommandInvocationResetAudits =>
        Set<CustomCommandInvocationResetAudit>();
    public DbSet<CustomCounter> CustomCounters => Set<CustomCounter>();
    public DbSet<CustomAnnouncement> CustomAnnouncements => Set<CustomAnnouncement>();
    public DbSet<CustomAnnouncementSchedule> CustomAnnouncementSchedules =>
        Set<CustomAnnouncementSchedule>();
    public DbSet<CustomAnnouncementDeliveryPolicy> CustomAnnouncementDeliveryPolicies =>
        Set<CustomAnnouncementDeliveryPolicy>();
    public DbSet<DurableAlert> DurableAlerts => Set<DurableAlert>();
    public DbSet<PublicChatOutboxMessage> PublicChatOutboxMessages =>
        Set<PublicChatOutboxMessage>();
    public DbSet<PublicChatSendReceipt> PublicChatSendReceipts => Set<PublicChatSendReceipt>();
    public DbSet<ReplyPinPolicy> ReplyPinPolicies => Set<ReplyPinPolicy>();
    public DbSet<PublicChatPinOperation> PublicChatPinOperations => Set<PublicChatPinOperation>();
    public DbSet<ActivePublicChatPin> ActivePublicChatPins => Set<ActivePublicChatPin>();
    public DbSet<PointBalance> PointBalances => Set<PointBalance>();
    public DbSet<PointLedgerEntry> PointLedgerEntries => Set<PointLedgerEntry>();
    public DbSet<Bounty> Bounties => Set<Bounty>();
    public DbSet<BountyPledge> BountyPledges => Set<BountyPledge>();
    public DbSet<BountyContributorReward> BountyContributorRewards =>
        Set<BountyContributorReward>();
    public DbSet<BountyModerationAudit> BountyModerationAudits => Set<BountyModerationAudit>();
    public DbSet<BountyDomainEvent> BountyEvents => Set<BountyDomainEvent>();
    public DbSet<CommunitySeason> CommunitySeasons => Set<CommunitySeason>();
    public DbSet<CommunityDefinition> CommunityDefinitions => Set<CommunityDefinition>();
    public DbSet<CommunityRewardDefinition> CommunityRewardDefinitions =>
        Set<CommunityRewardDefinition>();
    public DbSet<CommunityDefinitionReward> CommunityDefinitionRewards =>
        Set<CommunityDefinitionReward>();
    public DbSet<CommunityProgress> CommunityProgress => Set<CommunityProgress>();
    public DbSet<CommunityCompletion> CommunityCompletions => Set<CommunityCompletion>();
    public DbSet<CommunityRewardUnlock> CommunityRewardUnlocks => Set<CommunityRewardUnlock>();
    public DbSet<CommunityEquippedReward> CommunityEquippedRewards =>
        Set<CommunityEquippedReward>();
    public DbSet<CommunitySourceEventReceipt> CommunitySourceEventReceipts =>
        Set<CommunitySourceEventReceipt>();
    public DbSet<CommunityExternalGrantReceipt> CommunityExternalGrantReceipts =>
        Set<CommunityExternalGrantReceipt>();
    public DbSet<CommunityResetPeriod> CommunityResetPeriods => Set<CommunityResetPeriod>();
    public DbSet<CommunitySeasonStanding> CommunitySeasonStandings =>
        Set<CommunitySeasonStanding>();
    public DbSet<CommunityAudit> CommunityAudits => Set<CommunityAudit>();
    public DbSet<CommunityDomainEvent> CommunityEvents => Set<CommunityDomainEvent>();
    public DbSet<PointsGiveaway> PointsGiveaways => Set<PointsGiveaway>();
    public DbSet<PointsGiveawayEntrant> PointsGiveawayEntrants => Set<PointsGiveawayEntrant>();
    public DbSet<PointsGiveawayWinner> PointsGiveawayWinners => Set<PointsGiveawayWinner>();
    public DbSet<PointsSettings> PointsSettings => Set<PointsSettings>();
    public DbSet<GuessOption> GuessOptions => Set<GuessOption>();
    public DbSet<GuessRoundProfile> Profiles => Set<GuessRoundProfile>();
    public DbSet<GuessRound> Rounds => Set<GuessRound>();
    public DbSet<HostModAccessEntry> HostModAccessEntries => Set<HostModAccessEntry>();
    public DbSet<HostModAccessSettings> HostModAccessSettings => Set<HostModAccessSettings>();
    public DbSet<SiteAccessEntry> SiteAccessEntries => Set<SiteAccessEntry>();
    public DbSet<SiteAccessSettings> SiteAccessSettings => Set<SiteAccessSettings>();
    public DbSet<GuessVote> Votes => Set<GuessVote>();
    public DbSet<ShoutoutHistoryEntry> ShoutoutHistory => Set<ShoutoutHistoryEntry>();
    public DbSet<ShoutoutCooldownState> ShoutoutCooldowns => Set<ShoutoutCooldownState>();
    public DbSet<AutomaticRaidShoutoutSettings> AutomaticRaidShoutoutSettings =>
        Set<AutomaticRaidShoutoutSettings>();
    public DbSet<AutomaticRaidProcessedEvent> AutomaticRaidProcessedEvents =>
        Set<AutomaticRaidProcessedEvent>();
    public DbSet<AutomaticRaidShoutoutOutcome> AutomaticRaidShoutoutOutcomes =>
        Set<AutomaticRaidShoutoutOutcome>();
    public DbSet<HostBroadcasterAuthorization> HostBroadcasterAuthorizations =>
        Set<HostBroadcasterAuthorization>();
    public DbSet<TwitchPollTemplate> TwitchPollTemplates => Set<TwitchPollTemplate>();
    public DbSet<TwitchPollTemplateChoice> TwitchPollTemplateChoices =>
        Set<TwitchPollTemplateChoice>();
    public DbSet<TwitchPoll> TwitchPolls => Set<TwitchPoll>();
    public DbSet<TwitchClip> TwitchClips => Set<TwitchClip>();
    public DbSet<TwitchStreamMarker> TwitchStreamMarkers => Set<TwitchStreamMarker>();
    public DbSet<TwitchCustomReward> TwitchCustomRewards => Set<TwitchCustomReward>();
    public DbSet<TwitchRewardRedemption> TwitchRewardRedemptions => Set<TwitchRewardRedemption>();
    public DbSet<TwitchPredictionTemplate> TwitchPredictionTemplates =>
        Set<TwitchPredictionTemplate>();
    public DbSet<TwitchPredictionTemplateOutcome> TwitchPredictionTemplateOutcomes =>
        Set<TwitchPredictionTemplateOutcome>();
    public DbSet<TwitchPrediction> TwitchPredictions => Set<TwitchPrediction>();
    public DbSet<RequestBoard> RequestBoards => Set<RequestBoard>();
    public DbSet<RequestBoardField> RequestBoardFields => Set<RequestBoardField>();
    public DbSet<RequestSubmission> RequestSubmissions => Set<RequestSubmission>();
    public DbSet<RequestSubmissionValue> RequestSubmissionValues => Set<RequestSubmissionValue>();
    public DbSet<RequestSubmissionVote> RequestSubmissionVotes => Set<RequestSubmissionVote>();
    public DbSet<RequestBoardDomainEvent> RequestBoardEvents => Set<RequestBoardDomainEvent>();
    public DbSet<PlayQueue> PlayQueues => Set<PlayQueue>();
    public DbSet<PlayQueueField> PlayQueueFields => Set<PlayQueueField>();
    public DbSet<PlayQueueRoleRequirement> PlayQueueRoleRequirements =>
        Set<PlayQueueRoleRequirement>();
    public DbSet<PlayQueueEntry> PlayQueueEntries => Set<PlayQueueEntry>();
    public DbSet<PlayQueueEntryValue> PlayQueueEntryValues => Set<PlayQueueEntryValue>();
    public DbSet<PlayQueueParticipation> PlayQueueParticipation => Set<PlayQueueParticipation>();
    public DbSet<PlayQueueExclusion> PlayQueueExclusions => Set<PlayQueueExclusion>();
    public DbSet<PlayQueueDomainEvent> PlayQueueEvents => Set<PlayQueueDomainEvent>();
    public DbSet<MomentHubSettings> MomentHubSettings => Set<MomentHubSettings>();
    public DbSet<MomentCandidate> MomentCandidates => Set<MomentCandidate>();
    public DbSet<MomentCaptureRequest> MomentCaptureRequests => Set<MomentCaptureRequest>();
    public DbSet<MomentContributor> MomentContributors => Set<MomentContributor>();
    public DbSet<MomentSuggestion> MomentSuggestions => Set<MomentSuggestion>();
    public DbSet<MomentVote> MomentVotes => Set<MomentVote>();
    public DbSet<MomentModerationAudit> MomentModerationAudit => Set<MomentModerationAudit>();
    public DbSet<MomentDomainEvent> MomentEvents => Set<MomentDomainEvent>();
    public DbSet<MomentMerge> MomentMerges => Set<MomentMerge>();
    public DbSet<MomentWeeklyFinalization> MomentWeeklyFinalizations =>
        Set<MomentWeeklyFinalization>();
    public DbSet<OverlayInstance> OverlayInstances => Set<OverlayInstance>();
    public DbSet<OverlayInstanceDomainEvent> OverlayInstanceEvents =>
        Set<OverlayInstanceDomainEvent>();
    public DbSet<OverlayCue> OverlayCues => Set<OverlayCue>();
    public DbSet<OverlayMediaAsset> OverlayMediaAssets => Set<OverlayMediaAsset>();
    public DbSet<OverlayCueMediaAssetReference> OverlayCueMediaAssetReferences =>
        Set<OverlayCueMediaAssetReference>();
    public DbSet<OverlayEventFeedItem> OverlayEventFeedItems => Set<OverlayEventFeedItem>();
    public DbSet<AutomationFlow> AutomationFlows => Set<AutomationFlow>();
    public DbSet<AutomationFlowNode> AutomationFlowNodes => Set<AutomationFlowNode>();
    public DbSet<AutomationFlowEdge> AutomationFlowEdges => Set<AutomationFlowEdge>();
    public DbSet<AutomationFlowRun> AutomationFlowRuns => Set<AutomationFlowRun>();
    public DbSet<AutomationNodeRun> AutomationNodeRuns => Set<AutomationNodeRun>();
    public DbSet<AutomationEventReceipt> AutomationEventReceipts => Set<AutomationEventReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureHosts(modelBuilder);
        ConfigureReplyDelivery(modelBuilder);
        ConfigureAccess(modelBuilder);
        ConfigureCommands(modelBuilder);
        ConfigureAnnouncements(modelBuilder);
        ConfigureAlertsAndPublicChat(modelBuilder);
        ConfigurePoints(modelBuilder);
        ConfigureBounties(modelBuilder);
        ConfigureCommunityProgression(modelBuilder);
        ConfigureGuessing(modelBuilder);
        ConfigureShoutouts(modelBuilder);
        ConfigurePolls(modelBuilder);
        ConfigureClipsMarkers(modelBuilder);
        ConfigureChannelPoints(modelBuilder);
        ConfigurePredictions(modelBuilder);
        ConfigureRequestBoards(modelBuilder);
        ConfigurePlayWithViewers(modelBuilder);
        ConfigureMoments(modelBuilder);
        ConfigureOverlays(modelBuilder);
        ConfigureAutomations(modelBuilder);
    }

    private static string KindIn(string columnName, IEnumerable<string> values) =>
        $"{columnName} IN ({string.Join(", ", values.Select(static value => $"'{value}'"))})";

    private static string KindInOrNull(string columnName, IEnumerable<string> values) =>
        $"{columnName} IS NULL OR {KindIn(columnName, values)}";
}
