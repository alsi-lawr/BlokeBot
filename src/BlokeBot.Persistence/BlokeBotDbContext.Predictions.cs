using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _twitchPredictionStatusKinds =
        PersistedEnumTokens<TwitchPredictionStatus>.Values.ToArray();

    private static void ConfigurePredictions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TwitchPredictionTemplate>(b =>
        {
            b.ToTable("twitch_prediction_templates");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(45);
            b.HasIndex(x => x.HostId);
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Outcomes)
                .WithOne(x => x.Template)
                .HasForeignKey(x => x.TwitchPredictionTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<TwitchPredictionTemplateOutcome>(b =>
        {
            b.ToTable("twitch_prediction_template_outcomes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(25);
            b.HasIndex(x => new { x.TwitchPredictionTemplateId, x.Position }).IsUnique();
        });
        modelBuilder.Entity<TwitchPrediction>(b =>
        {
            b.ToTable(
                "twitch_predictions",
                table =>
                    table.HasCheckConstraint(
                        "CK_twitch_predictions_Status",
                        KindIn("Status", _twitchPredictionStatusKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.ProviderPredictionId).HasMaxLength(128);
            b.Property(x => x.Title).HasMaxLength(45);
            b.Property(x => x.OutcomesJson).HasMaxLength(16384);
            b.Property(x => x.Status)
                .HasConversion(
                    status => PersistedEnumTokens<TwitchPredictionStatus>.Format(status),
                    token => PersistedEnumTokens<TwitchPredictionStatus>.Parse(token)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new { x.HostId, x.ProviderPredictionId }).IsUnique();
            b.HasIndex(x => x.HostId).IsUnique().HasFilter("\"Status\" IN ('Active', 'Locked')");
            b.HasIndex(x => new { x.HostId, x.EndedAtUtc });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
