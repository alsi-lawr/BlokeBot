using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _guessRoundStatusKinds =
        PersistedEnumTokens<GuessRoundStatus>.Values.ToArray();

    private void ConfigureGuessing(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<GuessOption>(b =>
        {
            _ = b.ToTable(
                "guess_options",
                t =>
                    t.HasCheckConstraint(
                        "CK_guess_options_ReplyTarget",
                        KindIn(modelBuilder, "ReplyTarget", ReplyDeliveryTargetPersistence.Tokens)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Name).HasMaxLength(128);
            _ = b.Property(static x => x.SortOrder).HasDefaultValue(0);
            _ = b.Property(static x => x.ReplyTarget)
                .HasConversion(
                    static target => ReplyDeliveryTargetPersistence.ToToken(target),
                    static token => ReplyDeliveryTargetPersistence.FromToken(token)
                )
                .HasMaxLength(32)
                .HasDefaultValue(ReplyDeliveryTarget.Chat);
            _ = b.HasIndex(static x => new { x.GuessRoundProfileId, x.Name }).IsUnique();
            _ = b.HasOne(static x => x.GuessRoundProfile)
                .WithMany(static x => x.Options)
                .HasForeignKey(static x => x.GuessRoundProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        var isDefaultFilter = _isPostgreSql ? "\"IsDefault\"" : "\"IsDefault\" = 1";
        _ = modelBuilder.Entity<GuessRoundProfile>(b =>
        {
            _ = b.ToTable("guess_round_profiles");
            _ = b.HasKey(static x => x.Id);
            _ = b.HasAlternateKey(static x => new { x.HostId, x.Id });
            _ = b.Property(static x => x.Name).HasMaxLength(128);
            _ = b.Property(static x => x.Slug).HasMaxLength(128);
            _ = b.Property(static x => x.Revision).HasDefaultValue(0L);
            _ = b.Property(static x => x.WinningGuessPointReward)
                .HasMaxLength(128)
                .HasDefaultValue("0");
            _ = b.HasIndex(static x => new { x.HostId, x.Slug }).IsUnique();
            _ = b.HasIndex(static x => x.HostId).IsUnique().HasFilter(isDefaultFilter);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<GuessRound>(b =>
        {
            _ = b.ToTable(
                "guess_rounds",
                t =>
                    t.HasCheckConstraint(
                        "CK_guess_rounds_Status",
                        KindIn(modelBuilder, "Status", _guessRoundStatusKinds)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Status)
                .HasConversion(
                    static status => PersistedEnumTokens<GuessRoundStatus>.Format(status),
                    static value => PersistedEnumTokens<GuessRoundStatus>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.WinningName).HasMaxLength(128);
            _ = b.HasIndex(static x => x.HostId)
                .IsUnique()
                .HasFilter("\"Status\" IN ('Open', 'Closed')");
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(static x => x.GuessRoundProfile)
                .WithMany(static x => x.Rounds)
                .HasForeignKey(static x => x.GuessRoundProfileId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasMany(static x => x.Votes)
                .WithOne(static x => x.GuessRound)
                .HasForeignKey(static x => x.GuessRoundId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<GuessVote>(b =>
        {
            _ = b.ToTable("guess_votes");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Login).HasMaxLength(128);
            _ = b.Property(static x => x.GuessName).HasMaxLength(128);
            _ = b.HasIndex(static x => new { x.GuessRoundId, x.Login }).IsUnique();
        });
    }
}
