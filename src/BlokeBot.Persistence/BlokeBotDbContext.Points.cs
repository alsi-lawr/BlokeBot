using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _pointsEligibilityKinds =
        PersistedEnumTokens<PointsEligibilityMode>.Values.ToArray();

    private static readonly string[] _pointsGiveawayStatusKinds =
        PersistedEnumTokens<PointsGiveawayStatus>.Values.ToArray();

    private static void ConfigurePoints(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<PointsSettings>(static b =>
        {
            _ = b.ToTable(
                "points_settings",
                static t =>
                    t.HasCheckConstraint(
                        "CK_points_settings_GiveawayEligibility",
                        KindIn("GiveawayEligibility", _pointsEligibilityKinds)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.PointLabel).HasMaxLength(64);
            _ = b.Property(static x => x.GiveawayMinimumPayout).HasMaxLength(128);
            _ = b.Property(static x => x.GiveawayMaximumPayout).HasMaxLength(128);
            _ = b.Property(static x => x.GiveawayEligibility)
                .HasConversion(
                    static mode => PersistedEnumTokens<PointsEligibilityMode>.Format(mode),
                    static value => PersistedEnumTokens<PointsEligibilityMode>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.FollowerEligibilityUnavailableReply)
                .HasColumnName("FollowerChecksUnavailableReply");
            _ = b.HasIndex(static x => x.HostId).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PointBalance>(static b =>
        {
            _ = b.ToTable("point_balances");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Login).HasMaxLength(128);
            _ = b.Property(static x => x.Amount).HasMaxLength(128);
            _ = b.HasIndex(static x => new { x.HostId, x.Login }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PointLedgerEntry>(static b =>
        {
            _ = b.ToTable(
                "point_ledger_entries",
                static t =>
                    t.HasCheckConstraint(
                        "CK_point_ledger_entries_Kind",
                        KindIn("Kind", PointLedgerKindPersistence.Tokens)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Kind)
                .HasConversion(
                    static kind => PointLedgerKindPersistence.ToToken(kind),
                    static token => PointLedgerKindPersistence.FromToken(token)
                )
                .HasMaxLength(64);
            _ = b.Property(static x => x.Login).HasMaxLength(128);
            _ = b.Property(static x => x.Delta).HasMaxLength(128);
            _ = b.Property(static x => x.BalanceAfter).HasMaxLength(128);
            _ = b.Property(static x => x.ActorLogin).HasMaxLength(128);
            _ = b.Property(static x => x.CounterpartyLogin).HasMaxLength(128);
            _ = b.Property(static x => x.OperationKey).HasMaxLength(200);
            _ = b.HasIndex(static x => new { x.HostId, x.CreatedAtUtc });
            _ = b.HasIndex(static x => new { x.HostId, x.OperationKey }).IsUnique();
            _ = b.HasIndex(static x => x.RequestSubmissionId);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PointsGiveaway>(static b =>
        {
            _ = b.ToTable(
                "points_giveaways",
                static t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_points_giveaways_Status",
                        KindIn("Status", _pointsGiveawayStatusKinds)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_points_giveaways_Eligibility",
                        KindIn("Eligibility", _pointsEligibilityKinds)
                    );
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Status)
                .HasConversion(
                    static status => PersistedEnumTokens<PointsGiveawayStatus>.Format(status),
                    static value => PersistedEnumTokens<PointsGiveawayStatus>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.MinimumPayout).HasMaxLength(128);
            _ = b.Property(static x => x.MaximumPayout).HasMaxLength(128);
            _ = b.Property(static x => x.Eligibility)
                .HasConversion(
                    static mode => PersistedEnumTokens<PointsEligibilityMode>.Format(mode),
                    static value => PersistedEnumTokens<PointsEligibilityMode>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(static x => x.HostId).IsUnique().HasFilter("\"Status\" = 'Active'");
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Entrants)
                .WithOne(static x => x.Giveaway)
                .HasForeignKey(static x => x.GiveawayId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Winners)
                .WithOne(static x => x.Giveaway)
                .HasForeignKey(static x => x.GiveawayId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PointsGiveawayEntrant>(static b =>
        {
            _ = b.ToTable("points_giveaway_entrants");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Login).HasMaxLength(128);
            _ = b.HasIndex(static x => new { x.GiveawayId, x.Login }).IsUnique();
        });

        _ = modelBuilder.Entity<PointsGiveawayWinner>(static b =>
        {
            _ = b.ToTable("points_giveaway_winners");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Login).HasMaxLength(128);
            _ = b.Property(static x => x.Payout).HasMaxLength(128);
        });
    }
}
