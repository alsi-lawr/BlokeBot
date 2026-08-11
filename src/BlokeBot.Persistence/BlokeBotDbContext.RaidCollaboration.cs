using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _raidDirections =
        PersistedEnumTokens<RaidDirection>.Values.ToArray();
    private static readonly string[] _raidWelcomeOutcomes =
        PersistedEnumTokens<RaidWelcomeOutcome>.Values.ToArray();
    private static readonly string[] _raidShoutoutOutcomes =
        PersistedEnumTokens<RaidShoutoutOutcome>.Values.ToArray();

    private static void ConfigureRaidCollaboration(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<RaidCollaborationSettings>(static b =>
        {
            _ = b.ToTable("raid_collaboration_settings");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.WelcomeEnabled).HasDefaultValue(true);
            _ = b.Property(static x => x.WelcomeMessage)
                .HasMaxLength(500)
                .HasDefaultValue(RaidCollaborationDefaults.WelcomeMessage);
            _ = b.Property(static x => x.NativeShoutoutEnabled).HasDefaultValue(true);
            _ = b.Property(static x => x.DeduplicationWindowMinutes).HasDefaultValue(60);
            _ = b.Property(static x => x.Language).HasMaxLength(16).HasDefaultValue("en");
            _ = b.Property(static x => x.EligibleCategories).HasMaxLength(1000);
            _ = b.Property(static x => x.RelationshipCooldownHours).HasDefaultValue(336);
            _ = b.HasIndex(static x => x.HostId).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<ApprovedRaidChannel>(static b =>
        {
            _ = b.ToTable("approved_raid_channels");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.TwitchUserId).HasMaxLength(64);
            _ = b.Property(static x => x.Login).HasMaxLength(128);
            _ = b.Property(static x => x.DisplayName).HasMaxLength(128);
            _ = b.Property(static x => x.ApprovedClipId).HasMaxLength(128);
            _ = b.HasIndex(static x => new { x.HostId, x.Login }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<RaidCollaborationHistoryEntry>(static b =>
        {
            _ = b.ToTable(
                "raid_collaboration_history",
                static table =>
                {
                    _ = table.HasCheckConstraint(
                        "CK_raid_collaboration_history_Direction",
                        KindIn("Direction", _raidDirections)
                    );
                    _ = table.HasCheckConstraint(
                        "CK_raid_collaboration_history_WelcomeOutcome",
                        KindIn("WelcomeOutcome", _raidWelcomeOutcomes)
                    );
                    _ = table.HasCheckConstraint(
                        "CK_raid_collaboration_history_ShoutoutOutcome",
                        KindIn("ShoutoutOutcome", _raidShoutoutOutcomes)
                    );
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.ProviderMessageId).HasMaxLength(128);
            _ = b.Property(static x => x.Direction)
                .HasConversion(
                    static value => PersistedEnumTokens<RaidDirection>.Format(value),
                    static value => PersistedEnumTokens<RaidDirection>.Parse(value)
                )
                .HasMaxLength(16);
            _ = b.Property(static x => x.OtherTwitchUserId).HasMaxLength(64);
            _ = b.Property(static x => x.OtherLogin).HasMaxLength(128);
            _ = b.Property(static x => x.OtherDisplayName).HasMaxLength(128);
            _ = b.Property(static x => x.Category).HasMaxLength(128);
            _ = b.Property(static x => x.ProviderStreamId).HasMaxLength(128);
            _ = b.Property(static x => x.WelcomeOutcome)
                .HasConversion(
                    static value => PersistedEnumTokens<RaidWelcomeOutcome>.Format(value),
                    static value => PersistedEnumTokens<RaidWelcomeOutcome>.Parse(value)
                )
                .HasMaxLength(20);
            _ = b.Property(static x => x.ShoutoutOutcome)
                .HasConversion(
                    static value => PersistedEnumTokens<RaidShoutoutOutcome>.Format(value),
                    static value => PersistedEnumTokens<RaidShoutoutOutcome>.Parse(value)
                )
                .HasMaxLength(20);
            _ = b.HasIndex(static x => new { x.HostId, x.ProviderMessageId }).IsUnique();
            _ = b.HasIndex(static x => new { x.HostId, x.OccurredAtUtc });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
