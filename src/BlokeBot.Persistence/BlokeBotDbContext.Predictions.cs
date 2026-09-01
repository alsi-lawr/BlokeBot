using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _twitchPredictionStatusKinds =
        PersistedEnumTokens<TwitchPredictionStatus>.Values.ToArray();

    private static void ConfigurePredictions(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<TwitchPredictionTemplate>(b =>
        {
            _ = b.ToTable("twitch_prediction_templates");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Title).HasMaxLength(45);
            _ = b.HasIndex(static x => x.HostId);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Outcomes)
                .WithOne(static x => x.Template)
                .HasForeignKey(static x => x.TwitchPredictionTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<TwitchPredictionTemplateOutcome>(b =>
        {
            _ = b.ToTable("twitch_prediction_template_outcomes");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Title).HasMaxLength(25);
            _ = b.HasIndex(static x => new { x.TwitchPredictionTemplateId, x.Position }).IsUnique();
        });
        _ = modelBuilder.Entity<TwitchPrediction>(b =>
        {
            _ = b.ToTable(
                "twitch_predictions",
                table =>
                    table.HasCheckConstraint(
                        "CK_twitch_predictions_Status",
                        KindIn(modelBuilder, "Status", _twitchPredictionStatusKinds)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.ProviderPredictionId).HasMaxLength(128);
            _ = b.Property(static x => x.Title).HasMaxLength(45);
            _ = b.Property(static x => x.OutcomesJson).HasMaxLength(16384);
            _ = b.Property(static x => x.Status)
                .HasConversion(
                    static status => PersistedEnumTokens<TwitchPredictionStatus>.Format(status),
                    static token => PersistedEnumTokens<TwitchPredictionStatus>.Parse(token)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(static x => new { x.HostId, x.ProviderPredictionId }).IsUnique();
            _ = b.HasIndex(static x => x.HostId)
                .IsUnique()
                .HasFilter("\"Status\" IN ('Active', 'Locked')");
            _ = b.HasIndex(static x => new { x.HostId, x.EndedAtUtc });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
