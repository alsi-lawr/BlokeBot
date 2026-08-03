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
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Title).HasMaxLength(45);
            _ = b.HasIndex(x => x.HostId);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(x => x.Outcomes)
                .WithOne(x => x.Template)
                .HasForeignKey(x => x.TwitchPredictionTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        _ = modelBuilder.Entity<TwitchPredictionTemplateOutcome>(b =>
        {
            _ = b.ToTable("twitch_prediction_template_outcomes");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Title).HasMaxLength(25);
            _ = b.HasIndex(x => new { x.TwitchPredictionTemplateId, x.Position }).IsUnique();
        });
        _ = modelBuilder.Entity<TwitchPrediction>(b =>
        {
            _ = b.ToTable(
                "twitch_predictions",
                table =>
                    table.HasCheckConstraint(
                        "CK_twitch_predictions_Status",
                        KindIn("Status", _twitchPredictionStatusKinds)
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.ProviderPredictionId).HasMaxLength(128);
            _ = b.Property(x => x.Title).HasMaxLength(45);
            _ = b.Property(x => x.OutcomesJson).HasMaxLength(16384);
            _ = b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<TwitchPredictionStatus>.Format(status),
                    token => PersistedEnumTokens<TwitchPredictionStatus>.Parse(token)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(x => new { x.HostId, x.ProviderPredictionId }).IsUnique();
            _ = b.HasIndex(x => x.HostId)
                .IsUnique()
                .HasFilter("\"Status\" IN ('Active', 'Locked')");
            _ = b.HasIndex(x => new { x.HostId, x.EndedAtUtc });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
