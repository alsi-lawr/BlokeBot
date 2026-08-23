using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Runtime;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    public DbSet<PluginLifecycleRecord> PluginLifecycles => Set<PluginLifecycleRecord>();

    public DbSet<PluginLifecycleOutcomeRecord> PluginLifecycleOutcomes =>
        Set<PluginLifecycleOutcomeRecord>();

    private static void ConfigurePluginLifecycles(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<PluginLifecycleRecord>(static entity =>
        {
            _ = entity.ToTable(
                "plugin_lifecycles",
                table =>
                {
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_SelectedGeneration",
                        "\"SelectedGeneration\" > 0"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_ActiveRuntime",
                        "(\"ActiveVersion\" IS NULL AND \"ActiveTag\" IS NULL AND \"ActiveOperationId\" IS NULL AND \"ActiveGeneration\" IS NULL) OR "
                            + "(\"ActiveVersion\" IS NOT NULL AND \"ActiveTag\" IS NOT NULL AND \"ActiveOperationId\" IS NOT NULL AND \"ActiveGeneration\" > 0)"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_Phase",
                        "\"Phase\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Removed', 'Purging', 'Faulted')"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_FaultedFrom",
                        "\"FaultedFrom\" IS NULL OR \"FaultedFrom\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Removed', 'Purging', 'Faulted')"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_FaultShutdown",
                        "\"Phase\" <> 'Faulted' OR \"ActiveOperationId\" IS NULL OR \"FaultedFrom\" = 'Active'"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_OperationKind",
                        "\"OperationKind\" IN ('Activate', 'Remove', 'Purge', 'Restart')"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_OutcomeCode",
                        "\"OutcomeCode\" IN ('Preparing', 'Migrating', 'Activated', 'Removed', 'Purged', 'RestartScheduled', 'Restarted', 'Faulted', 'Recovered')"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_FailureCode",
                        "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('PreparationRejected', 'PreparationFailed', 'MigrationFailed', 'ActivationFailed', 'WorkerStartFailed', 'WorkerDisposalFailed', 'WorkerExited', 'DrainTimedOut', 'CancellationFailed', 'RemovalFailed', 'PurgeFailed', 'RecoveryPackageUnavailable', 'RecoveryFailed', 'GenerationExhausted')"
                    );
                }
            );
            _ = entity.HasKey(value => value.PluginId);
            _ = entity.Property(value => value.PluginId).HasMaxLength(128);
            _ = entity.Property(value => value.SelectedVersion).HasMaxLength(128);
            _ = entity.Property(value => value.SelectedTag).HasMaxLength(128);
            _ = entity.Property(value => value.ActiveVersion).HasMaxLength(128);
            _ = entity.Property(value => value.ActiveTag).HasMaxLength(128);
            _ = entity.Property(value => value.Phase).HasConversion<string>().HasMaxLength(16);
            _ = entity
                .Property(value => value.OperationKind)
                .HasConversion<string>()
                .HasMaxLength(16);
            _ = entity
                .Property(value => value.FaultedFrom)
                .HasConversion<string>()
                .HasMaxLength(16);
            _ = entity
                .Property(value => value.OutcomeCode)
                .HasConversion<string>()
                .HasMaxLength(24);
            _ = entity
                .Property(value => value.FailureCode)
                .HasConversion<string>()
                .HasMaxLength(40);
            _ = entity
                .Property(value => value.OutcomeDetail)
                .HasMaxLength(PluginLifecycleSafeDetail.MaximumLength);
            _ = entity.Property(value => value.Revision).IsConcurrencyToken();
        });

        _ = modelBuilder.Entity<PluginLifecycleOutcomeRecord>(static entity =>
        {
            _ = entity.ToTable(
                "plugin_lifecycle_outcomes",
                table =>
                {
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycle_outcomes_OutcomeCode",
                        "\"OutcomeCode\" IN ('Preparing', 'Migrating', 'Activated', 'Removed', 'Purged', 'RestartScheduled', 'Restarted', 'Faulted', 'Recovered')"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycle_outcomes_FailureCode",
                        "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('PreparationRejected', 'PreparationFailed', 'MigrationFailed', 'ActivationFailed', 'WorkerStartFailed', 'WorkerDisposalFailed', 'WorkerExited', 'DrainTimedOut', 'CancellationFailed', 'RemovalFailed', 'PurgeFailed', 'RecoveryPackageUnavailable', 'RecoveryFailed', 'GenerationExhausted')"
                    );
                }
            );
            _ = entity.HasKey(value => value.PluginId);
            _ = entity.Property(value => value.PluginId).HasMaxLength(128);
            _ = entity
                .Property(value => value.OutcomeCode)
                .HasConversion<string>()
                .HasMaxLength(24);
            _ = entity
                .Property(value => value.FailureCode)
                .HasConversion<string>()
                .HasMaxLength(40);
            _ = entity
                .Property(value => value.OutcomeDetail)
                .HasMaxLength(PluginLifecycleSafeDetail.MaximumLength);
        });
    }
}
