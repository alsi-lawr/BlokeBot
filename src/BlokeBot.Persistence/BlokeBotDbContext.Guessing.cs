using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _guessRoundStatusKinds =
        PersistedEnumTokens<GuessRoundStatus>.Values.ToArray();

    private static void ConfigureGuessing(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuessOption>(b =>
        {
            b.ToTable(
                "guess_options",
                t =>
                    t.HasCheckConstraint(
                        "CK_guess_options_ReplyTarget",
                        KindIn("ReplyTarget", ReplyDeliveryTargetPersistence.Tokens)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(128);
            b.Property(x => x.SortOrder).HasDefaultValue(0);
            b.Property(x => x.ReplyTarget)
                .HasConversion(
                    target => ReplyDeliveryTargetPersistence.ToToken(target),
                    token => ReplyDeliveryTargetPersistence.FromToken(token)
                )
                .HasMaxLength(32)
                .HasDefaultValue(ReplyDeliveryTarget.Chat);
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
            b.Property(x => x.Revision).HasDefaultValue(0L);
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
                        KindIn("Status", _guessRoundStatusKinds)
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
}
