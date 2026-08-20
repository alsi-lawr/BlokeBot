using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _configurationActivationStatuses =
        PersistedEnumTokens<ConfigurationActivationStatus>.Values.ToArray();

    private static void ConfigureConfigurationTransfer(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<ConfigurationImportAudit>(static b =>
        {
            _ = b.ToTable("configuration_import_audits");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.OperationId).HasConversion<string>();
            _ = b.Property(static x => x.ActorTwitchUserId).HasMaxLength(128);
            _ = b.Property(static x => x.ActorLogin).HasMaxLength(128);
            _ = b.Property(static x => x.SummaryJson).HasMaxLength(2048);
            _ = b.HasIndex(static x => x.OperationId).IsUnique();
            _ = b.HasIndex(static x => new { x.HostId, x.OccurredAtUtc });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<ConfigurationActivation>(static b =>
        {
            _ = b.ToTable(
                "configuration_activations",
                static t =>
                    t.HasCheckConstraint(
                        "CK_configuration_activations_Status",
                        KindIn("Status", _configurationActivationStatuses)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Id).HasConversion<string>();
            _ = b.Property(static x => x.EnabledChanges)
                .HasConversion(static x => (long)x, static x => (HostFeatureFlags)(ulong)x);
            _ = b.Property(static x => x.DisabledChanges)
                .HasConversion(static x => (long)x, static x => (HostFeatureFlags)(ulong)x);
            _ = b.Property(static x => x.Status)
                .HasConversion(
                    static x => PersistedEnumTokens<ConfigurationActivationStatus>.Format(x),
                    static x => PersistedEnumTokens<ConfigurationActivationStatus>.Parse(x)
                )
                .HasMaxLength(16);
            _ = b.Property(static x => x.FailureCode).HasMaxLength(64);
            _ = b.HasIndex(static x => new { x.HostId, x.Status });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
