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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureHosts(modelBuilder);
        ConfigureReplyDelivery(modelBuilder);
        ConfigureAccess(modelBuilder);
        ConfigureCommands(modelBuilder);
        ConfigureAnnouncements(modelBuilder);
        ConfigureAlertsAndPublicChat(modelBuilder);
        ConfigurePoints(modelBuilder);
        ConfigureGuessing(modelBuilder);
        ConfigureShoutouts(modelBuilder);
    }

    private static string KindIn(string columnName, IEnumerable<string> values)
    {
        return $"{columnName} IN ({string.Join(", ", values.Select(value => $"'{value}'"))})";
    }

    private static string KindInOrNull(string columnName, IEnumerable<string> values)
    {
        return $"{columnName} IS NULL OR {KindIn(columnName, values)}";
    }
}
