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
        modelBuilder.Entity<PointsSettings>(b =>
        {
            b.ToTable(
                "points_settings",
                t =>
                    t.HasCheckConstraint(
                        "CK_points_settings_GiveawayEligibility",
                        KindIn("GiveawayEligibility", _pointsEligibilityKinds)
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
            b.ToTable(
                "point_ledger_entries",
                t =>
                    t.HasCheckConstraint(
                        "CK_point_ledger_entries_Kind",
                        KindIn("Kind", PointLedgerKindPersistence.Tokens)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind)
                .HasConversion(
                    kind => PointLedgerKindPersistence.ToToken(kind),
                    token => PointLedgerKindPersistence.FromToken(token)
                )
                .HasMaxLength(64);
            b.Property(x => x.Login).HasMaxLength(128);
            b.Property(x => x.Delta).HasMaxLength(128);
            b.Property(x => x.BalanceAfter).HasMaxLength(128);
            b.Property(x => x.ActorLogin).HasMaxLength(128);
            b.Property(x => x.CounterpartyLogin).HasMaxLength(128);
            b.HasIndex(x => new { x.HostId, x.CreatedAtUtc });
            b.HasIndex(x => x.RequestSubmissionId);
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
                        KindIn("Status", _pointsGiveawayStatusKinds)
                    );
                    t.HasCheckConstraint(
                        "CK_points_giveaways_Eligibility",
                        KindIn("Eligibility", _pointsEligibilityKinds)
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
    }
}
