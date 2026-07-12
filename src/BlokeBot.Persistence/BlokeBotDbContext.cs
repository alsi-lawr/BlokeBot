using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed class BlokeBotDbContext(DbContextOptions<BlokeBotDbContext> options)
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
    public DbSet<CustomCounter> CustomCounters => Set<CustomCounter>();
    public DbSet<CustomAnnouncement> CustomAnnouncements => Set<CustomAnnouncement>();
    public DbSet<CustomAnnouncementSchedule> CustomAnnouncementSchedules =>
        Set<CustomAnnouncementSchedule>();
    public DbSet<DurableAlert> DurableAlerts => Set<DurableAlert>();
    public DbSet<PublicChatOutboxMessage> PublicChatOutboxMessages =>
        Set<PublicChatOutboxMessage>();
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BotHost>(b =>
        {
            b.ToTable("hosts");
            b.HasKey(x => x.Id);
            b.Property(x => x.BotRuntimeState);
            b.Property(x => x.BotRuntimeStateChangedAtUtc);
            b.Property(x => x.ChannelBotAuthorizedAtUtc);
            b.Property(x => x.ChannelBotAuthorizedScopes).HasMaxLength(512);
            b.Property(x => x.EnabledFeatures)
                .HasConversion(features => (long)features, value => (HostFeatureFlags)(ulong)value)
                .HasDefaultValue(HostFeatureFlags.All);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.DisplayName).HasMaxLength(128);
            b.Property(x => x.ProfileImageUrl).HasMaxLength(512);
            b.Property(x => x.TimeZoneId).HasMaxLength(128).HasDefaultValue("UTC");
            b.Property(x => x.TwitchUserId).HasMaxLength(64);
            b.HasIndex(x => x.Login).IsUnique();
        });

        modelBuilder.Entity<HostBotAccountSettings>(b =>
        {
            b.ToTable("host_bot_account_settings");
            b.HasKey(x => x.Id);
            b.Property(x => x.AccessToken).HasMaxLength(4096);
            b.Property(x => x.AuthorizedScopes).HasMaxLength(512);
            b.Property(x => x.DisplayName).HasMaxLength(128);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.ProfileImageUrl).HasMaxLength(512);
            b.Property(x => x.RefreshToken).HasMaxLength(4096);
            b.Property(x => x.TwitchUserId).HasMaxLength(64);
            b.HasIndex(x => x.HostId).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WhisperQuotaBucket>(b =>
        {
            b.ToTable("whisper_quota_buckets");
            b.HasKey(x => x.Id);
            b.Property(x => x.BotTwitchUserId).HasMaxLength(64);
            b.HasIndex(x => new
                {
                    x.HostId,
                    x.BotTwitchUserId,
                    x.DayUtc,
                })
                .IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Recipients)
                .WithOne(x => x.WhisperQuotaBucket)
                .HasForeignKey(x => x.WhisperQuotaBucketId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WhisperQuotaRecipient>(b =>
        {
            b.ToTable("whisper_quota_recipients");
            b.HasKey(x => x.Id);
            b.Property(x => x.RecipientLogin).HasMaxLength(128);
            b.Property(x => x.RecipientTwitchUserId).HasMaxLength(64);
            b.HasIndex(x => new { x.WhisperQuotaBucketId, x.RecipientTwitchUserId }).IsUnique();
        });

        modelBuilder.Entity<ReplyDeliverySetting>(b =>
        {
            b.ToTable("reply_delivery_settings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Feature).HasMaxLength(64);
            b.Property(x => x.ReplyKey).HasMaxLength(128);
            b.Property(x => x.Target).HasMaxLength(32);
            b.HasIndex(x => new
                {
                    x.HostId,
                    x.Feature,
                    x.ScopeId,
                    x.ReplyKey,
                })
                .IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SiteAccessSettings>(b =>
        {
            b.ToTable("site_access_settings");
            b.HasKey(x => x.Id);
        });

        modelBuilder.Entity<SiteAccessEntry>(b =>
        {
            b.ToTable(
                "site_access_entries",
                t =>
                    t.HasCheckConstraint("CK_site_access_entries_Kind", KindIn("Kind", AccessKinds))
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.Kind)
                .HasConversion(
                    kind => PersistedEnumTokens<AccessListEntryKind>.Format(kind),
                    value => PersistedEnumTokens<AccessListEntryKind>.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new { x.Kind, x.Login }).IsUnique();
        });

        modelBuilder.Entity<HostModAccessSettings>(b =>
        {
            b.ToTable("host_mod_access_settings");
            b.HasKey(x => x.Id);
            b.Property(x => x.AllowModsByDefault).HasDefaultValue(true);
            b.HasIndex(x => x.HostId).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HostModAccessEntry>(b =>
        {
            b.ToTable(
                "host_mod_access_entries",
                t =>
                    t.HasCheckConstraint(
                        "CK_host_mod_access_entries_Kind",
                        KindIn("Kind", AccessKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.Kind)
                .HasConversion(
                    kind => PersistedEnumTokens<AccessListEntryKind>.Format(kind),
                    value => PersistedEnumTokens<AccessListEntryKind>.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new
                {
                    x.HostId,
                    x.Kind,
                    x.Login,
                })
                .IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BotReplySettings>(b =>
        {
            b.ToTable("reply_settings");
            b.HasKey(x => x.Id);
            b.HasOne(x => x.GuessRoundProfile)
                .WithOne(x => x.ReplySettings)
                .HasForeignKey<BotReplySettings>(x => x.GuessRoundProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommandAlias>(b =>
        {
            b.ToTable(
                "command_aliases",
                t =>
                    t.HasCheckConstraint(
                        "CK_command_aliases_Kind",
                        KindIn("Kind", CommandAliasKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind)
                .HasConversion(
                    kind => PersistedEnumTokens<AppCommandKind>.Format(kind),
                    value => PersistedEnumTokens<AppCommandKind>.Parse(value)
                )
                .HasMaxLength(64);
            b.Property(x => x.Alias).HasMaxLength(64);
            b.HasIndex(x => new { x.HostId, x.Alias }).IsUnique();
            b.HasIndex(x => new { x.HostId, x.GuessRoundProfileId });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.GuessRoundProfile)
                .WithMany(x => x.CommandAliases)
                .HasForeignKey(x => new { x.HostId, x.GuessRoundProfileId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomMessageLibraryEntry>(b =>
        {
            b.ToTable(
                "custom_message_library_entries",
                t =>
                    t.HasCheckConstraint(
                        "CK_custom_message_library_entries_SelectionMode",
                        KindIn("SelectionMode", CustomMessageSelectionModes)
                    )
            );
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.HostId, x.Id });
            b.Property(x => x.Name).HasMaxLength(128);
            b.Property(x => x.SelectionMode)
                .HasConversion(
                    mode => PersistedEnumTokens<CustomMessageSelectionMode>.Format(mode),
                    value => PersistedEnumTokens<CustomMessageSelectionMode>.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new { x.HostId, x.Name }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Variants)
                .WithOne(x => x.Entry)
                .HasForeignKey(x => x.CustomMessageLibraryEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomMessageVariant>(b =>
        {
            b.ToTable("custom_message_variants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Text).HasMaxLength(500);
            b.HasIndex(x => new { x.CustomMessageLibraryEntryId, x.SortOrder }).IsUnique();
        });

        modelBuilder.Entity<CustomCounter>(b =>
        {
            b.ToTable("custom_counters");
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.HostId, x.Id });
            b.Property(x => x.Name).HasMaxLength(128);
            b.HasIndex(x => new { x.HostId, x.Name }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomCommand>(b =>
        {
            b.ToTable(
                "custom_commands",
                t =>
                    t.HasCheckConstraint(
                        "CK_custom_commands_CooldownScope",
                        KindIn("CooldownScope", CustomCommandCooldownScopes)
                    )
            );
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.HostId, x.Id });
            b.Property(x => x.Name).HasMaxLength(128);
            b.Property(x => x.CooldownScope)
                .HasConversion(
                    scope => PersistedEnumTokens<CustomCommandCooldownScope>.Format(scope),
                    value => PersistedEnumTokens<CustomCommandCooldownScope>.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new { x.HostId, x.Name }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Action)
                .WithOne(x => x.Command)
                .HasForeignKey<CustomCommandAction>(x => new { x.HostId, x.CustomCommandId })
                .HasPrincipalKey<CustomCommand>(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomCommandAction>(b =>
        {
            b.ToTable(
                "custom_command_actions",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_custom_command_actions_ActionType",
                        KindIn("ActionType", CustomCommandActionTypes)
                    );
                    t.HasCheckConstraint(
                        "CK_custom_command_actions_Payload",
                        "(ActionType = 'Message' AND CounterId IS NULL) OR "
                            + "(ActionType = 'Counter' AND CounterId IS NOT NULL)"
                    );
                }
            );
            b.HasKey(x => x.CustomCommandId);
            b.Property<string>("ActionType").HasMaxLength(32);
            b.HasDiscriminator<string>("ActionType")
                .HasValue<MessageCustomCommandAction>(MessageCustomCommandAction.Discriminator)
                .HasValue<CounterCustomCommandAction>(CounterCustomCommandAction.Discriminator);
            b.HasOne(x => x.MessageLibraryEntry)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.MessageLibraryEntryId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CounterCustomCommandAction>(b =>
        {
            b.HasOne(x => x.Counter)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.CounterId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustomCommandAlias>(b =>
        {
            b.ToTable("custom_command_aliases");
            b.HasKey(x => x.Id);
            b.Property(x => x.Alias).HasMaxLength(64);
            b.HasIndex(x => new { x.HostId, x.Alias }).IsUnique();
            b.HasOne(x => x.Command)
                .WithMany(x => x.Aliases)
                .HasForeignKey(x => new { x.HostId, x.CustomCommandId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomAnnouncement>(b =>
        {
            b.ToTable("custom_announcements");
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.HostId, x.Id });
            b.Property(x => x.Name).HasMaxLength(128);
            b.HasIndex(x => new { x.HostId, x.Name }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.MessageLibraryEntry)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.MessageLibraryEntryId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Schedule)
                .WithOne(x => x.Announcement)
                .HasForeignKey<CustomAnnouncementSchedule>(x =>
                    new { x.HostId, x.CustomAnnouncementId }
                )
                .HasPrincipalKey<CustomAnnouncement>(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomAnnouncementSchedule>(b =>
        {
            b.ToTable(
                "custom_announcement_schedules",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_custom_announcement_schedules_ScheduleType",
                        KindIn("ScheduleType", CustomAnnouncementScheduleTypes)
                    );
                    t.HasCheckConstraint(
                        "CK_custom_announcement_schedules_Payload",
                        "(ScheduleType = 'Interval' AND IntervalMinutes >= 1 "
                            + "AND RequiredChatMessages IS NULL AND WeeklyDay IS NULL AND WeeklyTime IS NULL) OR "
                            + "(ScheduleType = 'IntervalAfterChat' AND IntervalMinutes >= 1 "
                            + "AND RequiredChatMessages >= 1 AND WeeklyDay IS NULL AND WeeklyTime IS NULL) OR "
                            + "(ScheduleType = 'Weekly' AND IntervalMinutes IS NULL "
                            + "AND RequiredChatMessages IS NULL AND WeeklyDay BETWEEN 0 AND 6 "
                            + "AND WeeklyTime IS NOT NULL)"
                    );
                }
            );
            b.HasKey(x => x.CustomAnnouncementId);
            b.Property<string>("ScheduleType").HasMaxLength(32);
            b.HasDiscriminator<string>("ScheduleType")
                .HasValue<IntervalCustomAnnouncementSchedule>(
                    IntervalCustomAnnouncementSchedule.Discriminator
                )
                .HasValue<IntervalAfterChatCustomAnnouncementSchedule>(
                    IntervalAfterChatCustomAnnouncementSchedule.Discriminator
                )
                .HasValue<WeeklyCustomAnnouncementSchedule>(
                    WeeklyCustomAnnouncementSchedule.Discriminator
                );
        });

        modelBuilder.Entity<IntervalCustomAnnouncementSchedule>(b =>
            b.Property(x => x.IntervalMinutes).HasColumnName("IntervalMinutes")
        );
        modelBuilder.Entity<IntervalAfterChatCustomAnnouncementSchedule>(b =>
        {
            b.Property(x => x.IntervalMinutes).HasColumnName("IntervalMinutes");
            b.Property(x => x.RequiredChatMessages).HasColumnName("RequiredChatMessages");
        });
        modelBuilder.Entity<WeeklyCustomAnnouncementSchedule>(b =>
        {
            b.Property(x => x.Day).HasColumnName("WeeklyDay");
            b.Property(x => x.Time).HasColumnName("WeeklyTime");
        });

        modelBuilder.Entity<DurableAlert>(b =>
        {
            b.ToTable(
                "durable_alerts",
                t =>
                    t.HasCheckConstraint(
                        "CK_durable_alerts_Severity",
                        KindIn("Severity", DurableAlertSeverities)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Severity)
                .HasConversion(
                    severity => PersistedEnumTokens<DurableAlertSeverity>.Format(severity),
                    value => PersistedEnumTokens<DurableAlertSeverity>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.Source).HasMaxLength(64);
            b.Property(x => x.SourceKey).HasMaxLength(256);
            b.Property(x => x.Title).HasMaxLength(160);
            b.Property(x => x.Message).HasMaxLength(1000);
            b.Property(x => x.LinkPath).HasMaxLength(256);
            b.Property(x => x.AcknowledgedByLogin).HasMaxLength(128);
            b.HasIndex(x => new { x.HostId, x.AcknowledgedAtUtc });
            b.HasIndex(x => new
                {
                    x.HostId,
                    x.Source,
                    x.SourceKey,
                })
                .IsUnique()
                .HasFilter("\"AcknowledgedAtUtc\" IS NULL");
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PublicChatOutboxMessage>(b =>
        {
            b.ToTable(
                "public_chat_outbox",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_public_chat_outbox_Status",
                        KindIn("Status", PublicChatOutboxStatuses)
                    );
                    t.HasCheckConstraint(
                        "CK_public_chat_outbox_AttemptCount",
                        "AttemptCount >= 0"
                    );
                    t.HasCheckConstraint(
                        "CK_public_chat_outbox_Channel",
                        "length(trim(Channel)) > 0"
                    );
                    t.HasCheckConstraint(
                        "CK_public_chat_outbox_DeduplicationKey",
                        "length(DeduplicationKey) = 64"
                    );
                    t.HasCheckConstraint(
                        "CK_public_chat_outbox_State",
                        "(Status = 'Pending' AND length(Message) > 0 "
                            + "AND ClaimToken IS NULL AND ClaimSlot IS NULL "
                            + "AND ClaimExpiresAtUtc IS NULL "
                            + "AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NULL) OR "
                            + "(Status = 'Claimed' AND length(Message) > 0 "
                            + "AND ClaimToken IS NOT NULL AND ClaimSlot = 1 "
                            + "AND ClaimExpiresAtUtc IS NOT NULL "
                            + "AND SendStartedAtUtc IS NULL AND CompletedAtUtc IS NULL) OR "
                            + "(Status = 'Sending' AND length(Message) > 0 "
                            + "AND ClaimToken IS NOT NULL AND ClaimSlot = 1 "
                            + "AND ClaimExpiresAtUtc IS NOT NULL "
                            + "AND SendStartedAtUtc IS NOT NULL AND CompletedAtUtc IS NULL) OR "
                            + "(Status IN ('Delivered', 'Faulted') AND Message IS NULL "
                            + "AND ClaimToken IS NULL AND ClaimSlot IS NULL "
                            + "AND ClaimExpiresAtUtc IS NULL "
                            + "AND SendStartedAtUtc IS NOT NULL AND CompletedAtUtc IS NOT NULL)"
                    );
                }
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Channel).HasMaxLength(128);
            b.Property(x => x.DeduplicationKey).HasMaxLength(64);
            b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<PublicChatOutboxStatus>.Format(status),
                    value => PersistedEnumTokens<PublicChatOutboxStatus>.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new
            {
                x.Status,
                x.NextAttemptAtUtc,
                x.CreatedAtUtc,
                x.Id,
            });
            b.HasIndex(x => new { x.Status, x.ClaimExpiresAtUtc });
            b.HasIndex(x => x.ClaimToken)
                .IsUnique()
                .HasFilter("\"ClaimToken\" IS NOT NULL");
            b.HasIndex(x => x.ClaimSlot)
                .IsUnique()
                .HasFilter("\"ClaimSlot\" IS NOT NULL");
        });

        modelBuilder.Entity<PointsSettings>(b =>
        {
            b.ToTable(
                "points_settings",
                t =>
                    t.HasCheckConstraint(
                        "CK_points_settings_GiveawayEligibility",
                        KindIn("GiveawayEligibility", PointsEligibilityKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.PointLabel).HasMaxLength(64);
            b.Property(x => x.GiveawayMinimumPayout).HasMaxLength(128);
            b.Property(x => x.GiveawayMaximumPayout).HasMaxLength(128);
            b.Property(x => x.GiveawayEligibility)
                .HasConversion(
                    mode => PersistedEnumTokens<PointsEligibilityMode>.Format(mode),
                    value => PersistedEnumTokens<PointsEligibilityMode>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.FollowerEligibilityUnavailableReply)
                .HasColumnName("FollowerChecksUnavailableReply");
            b.HasIndex(x => x.HostId).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PointBalance>(b =>
        {
            b.ToTable("point_balances");
            b.HasKey(x => x.Id);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.Amount).HasMaxLength(128);
            b.HasIndex(x => new { x.HostId, x.Login }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PointLedgerEntry>(b =>
        {
            b.ToTable("point_ledger_entries");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasMaxLength(64);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.Delta).HasMaxLength(128);
            b.Property(x => x.BalanceAfter).HasMaxLength(128);
            b.Property(x => x.ActorLogin).HasMaxLength(128);
            b.Property(x => x.CounterpartyLogin).HasMaxLength(128);
            b.HasIndex(x => new { x.HostId, x.CreatedAtUtc });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PointsGiveaway>(b =>
        {
            b.ToTable(
                "points_giveaways",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_points_giveaways_Status",
                        KindIn("Status", PointsGiveawayStatusKinds)
                    );
                    t.HasCheckConstraint(
                        "CK_points_giveaways_Eligibility",
                        KindIn("Eligibility", PointsEligibilityKinds)
                    );
                }
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<PointsGiveawayStatus>.Format(status),
                    value => PersistedEnumTokens<PointsGiveawayStatus>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.MinimumPayout).HasMaxLength(128);
            b.Property(x => x.MaximumPayout).HasMaxLength(128);
            b.Property(x => x.Eligibility)
                .HasConversion(
                    mode => PersistedEnumTokens<PointsEligibilityMode>.Format(mode),
                    value => PersistedEnumTokens<PointsEligibilityMode>.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => x.HostId).IsUnique().HasFilter("\"Status\" = 'Active'");
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Entrants)
                .WithOne(x => x.Giveaway)
                .HasForeignKey(x => x.GiveawayId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Winners)
                .WithOne(x => x.Giveaway)
                .HasForeignKey(x => x.GiveawayId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PointsGiveawayEntrant>(b =>
        {
            b.ToTable("points_giveaway_entrants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Login).HasMaxLength(128);
            b.HasIndex(x => new { x.GiveawayId, x.Login }).IsUnique();
        });

        modelBuilder.Entity<PointsGiveawayWinner>(b =>
        {
            b.ToTable("points_giveaway_winners");
            b.HasKey(x => x.Id);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.Payout).HasMaxLength(128);
        });

        modelBuilder.Entity<GuessOption>(b =>
        {
            b.ToTable("guess_options");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(128);
            b.Property(x => x.ReplyTarget).HasMaxLength(32).HasDefaultValue("chat");
            b.HasIndex(x => new { x.GuessRoundProfileId, x.Name }).IsUnique();
            b.HasOne(x => x.GuessRoundProfile)
                .WithMany(x => x.Options)
                .HasForeignKey(x => x.GuessRoundProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuessRoundProfile>(b =>
        {
            b.ToTable("guess_round_profiles");
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.HostId, x.Id });
            b.Property(x => x.Name).HasMaxLength(128);
            b.Property(x => x.Slug).HasMaxLength(128);
            b.Property(x => x.WinningGuessPointReward).HasMaxLength(128).HasDefaultValue("0");
            b.HasIndex(x => new { x.HostId, x.Slug }).IsUnique();
            b.HasIndex(x => x.HostId).IsUnique().HasFilter("\"IsDefault\" = 1");
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuessRound>(b =>
        {
            b.ToTable(
                "guess_rounds",
                t =>
                    t.HasCheckConstraint(
                        "CK_guess_rounds_Status",
                        KindIn("Status", GuessRoundStatusKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<GuessRoundStatus>.Format(status),
                    value => PersistedEnumTokens<GuessRoundStatus>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.WinningName).HasMaxLength(128);
            b.HasIndex(x => x.HostId).IsUnique().HasFilter("\"Status\" IN ('Open', 'Closed')");
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.GuessRoundProfile)
                .WithMany(x => x.Rounds)
                .HasForeignKey(x => x.GuessRoundProfileId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Votes)
                .WithOne(x => x.GuessRound)
                .HasForeignKey(x => x.GuessRoundId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuessVote>(b =>
        {
            b.ToTable("guess_votes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.GuessName).HasMaxLength(128);
            b.HasIndex(x => new { x.GuessRoundId, x.Login }).IsUnique();
        });
    }

    private static readonly string[] AccessKinds =
        PersistedEnumTokens<AccessListEntryKind>.Values.ToArray();

    private static readonly string[] CommandAliasKinds =
        PersistedEnumTokens<AppCommandKind>.Values.ToArray();

    private static readonly string[] CustomAnnouncementScheduleTypes =
    [
        IntervalCustomAnnouncementSchedule.Discriminator,
        IntervalAfterChatCustomAnnouncementSchedule.Discriminator,
        WeeklyCustomAnnouncementSchedule.Discriminator,
    ];

    private static readonly string[] CustomCommandActionTypes =
    [
        CounterCustomCommandAction.Discriminator,
        MessageCustomCommandAction.Discriminator,
    ];

    private static readonly string[] CustomCommandCooldownScopes =
        PersistedEnumTokens<CustomCommandCooldownScope>.Values.ToArray();

    private static readonly string[] CustomMessageSelectionModes =
        PersistedEnumTokens<CustomMessageSelectionMode>.Values.ToArray();

    private static readonly string[] DurableAlertSeverities =
        PersistedEnumTokens<DurableAlertSeverity>.Values.ToArray();

    private static readonly string[] GuessRoundStatusKinds =
        PersistedEnumTokens<GuessRoundStatus>.Values.ToArray();

    private static readonly string[] PointsEligibilityKinds =
        PersistedEnumTokens<PointsEligibilityMode>.Values.ToArray();

    private static readonly string[] PointsGiveawayStatusKinds =
        PersistedEnumTokens<PointsGiveawayStatus>.Values.ToArray();

    private static readonly string[] PublicChatOutboxStatuses =
        PersistedEnumTokens<PublicChatOutboxStatus>.Values.ToArray();

    private static string KindIn(string columnName, IEnumerable<string> values) =>
        $"{columnName} IN ({string.Join(", ", values.Select(value => $"'{value}'"))})";
}
