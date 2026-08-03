using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _guessRoundStatusKinds =
        PersistedEnumTokens<GuessRoundStatus>.Values.ToArray();

    private static void ConfigureGuessing(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<GuessOption>(b =>
        {
            _ = b.ToTable(
                "guess_options",
                t =>
                    t.HasCheckConstraint(
                        "CK_guess_options_ReplyTarget",
                        KindIn("ReplyTarget", ReplyDeliveryTargetPersistence.Tokens)
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Name).HasMaxLength(128);
            _ = b.Property(x => x.SortOrder).HasDefaultValue(0);
            _ = b.Property(x => x.ReplyTarget)
                .HasConversion(
                    target => ReplyDeliveryTargetPersistence.ToToken(target),
                    token => ReplyDeliveryTargetPersistence.FromToken(token)
                )
                .HasMaxLength(32)
                .HasDefaultValue(ReplyDeliveryTarget.Chat);
            _ = b.HasIndex(x => new { x.GuessRoundProfileId, x.Name }).IsUnique();
            _ = b.HasOne(x => x.GuessRoundProfile)
                .WithMany(x => x.Options)
                .HasForeignKey(x => x.GuessRoundProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<GuessRoundProfile>(b =>
        {
            _ = b.ToTable("guess_round_profiles");
            _ = b.HasKey(x => x.Id);
            _ = b.HasAlternateKey(x => new { x.HostId, x.Id });
            _ = b.Property(x => x.Name).HasMaxLength(128);
            _ = b.Property(x => x.Slug).HasMaxLength(128);
            _ = b.Property(x => x.Revision).HasDefaultValue(0L);
            _ = b.Property(x => x.WinningGuessPointReward).HasMaxLength(128).HasDefaultValue("0");
            _ = b.HasIndex(x => new { x.HostId, x.Slug }).IsUnique();
            _ = b.HasIndex(x => x.HostId).IsUnique().HasFilter("\"IsDefault\" = 1");
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<GuessRound>(b =>
        {
            _ = b.ToTable(
                "guess_rounds",
                t =>
                    t.HasCheckConstraint(
                        "CK_guess_rounds_Status",
                        KindIn("Status", _guessRoundStatusKinds)
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<GuessRoundStatus>.Format(status),
                    value => PersistedEnumTokens<GuessRoundStatus>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(x => x.WinningName).HasMaxLength(128);
            _ = b.HasIndex(x => x.HostId).IsUnique().HasFilter("\"Status\" IN ('Open', 'Closed')");
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(x => x.GuessRoundProfile)
                .WithMany(x => x.Rounds)
                .HasForeignKey(x => x.GuessRoundProfileId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasMany(x => x.Votes)
                .WithOne(x => x.GuessRound)
                .HasForeignKey(x => x.GuessRoundId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<GuessVote>(b =>
        {
            _ = b.ToTable("guess_votes");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Login).HasMaxLength(128);
            _ = b.Property(x => x.GuessName).HasMaxLength(128);
            _ = b.HasIndex(x => new { x.GuessRoundId, x.Login }).IsUnique();
        });
    }
}
