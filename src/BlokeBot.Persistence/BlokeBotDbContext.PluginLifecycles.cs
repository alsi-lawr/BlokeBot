using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Runtime;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    public DbSet<PluginLifecycleRecord> PluginLifecycles => Set<PluginLifecycleRecord>();

    private static void ConfigurePluginLifecycles(ModelBuilder modelBuilder) =>
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
                        "CK_plugin_lifecycles_SelectedPackageOperation",
                        "\"SelectedPackageOperationId\" <> '00000000-0000-0000-0000-000000000000'"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_ActiveRuntime",
                        "(\"ActiveVersion\" IS NULL AND \"ActiveTag\" IS NULL AND \"ActiveOperationId\" IS NULL AND \"ActivePackageOperationId\" IS NULL AND \"ActiveGeneration\" IS NULL) OR "
                            + "(\"ActiveVersion\" IS NOT NULL AND \"ActiveTag\" IS NOT NULL AND \"ActiveOperationId\" IS NOT NULL AND \"ActivePackageOperationId\" IS NOT NULL AND \"ActiveGeneration\" > 0)"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_Phase",
                        "\"Phase\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing', 'Removed', 'Faulted')"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_FaultedFrom",
                        "(\"Phase\" = 'Faulted' AND \"FaultedFrom\" IS NOT NULL AND \"FaultedFrom\" IN ('Preparing', 'Migrating', 'Activating', 'Active', 'Draining', 'Removing')) OR "
                            + "(\"Phase\" <> 'Faulted' AND \"FaultedFrom\" IS NULL)"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_FaultShutdown",
                        "\"Phase\" <> 'Faulted' OR \"ActiveOperationId\" IS NULL OR "
                            + "(\"FaultedFrom\" = 'Active' AND \"ActiveVersion\" = \"SelectedVersion\" AND \"ActiveTag\" = \"SelectedTag\" AND "
                            + "\"ActiveOperationId\" = \"OperationId\" AND \"ActivePackageOperationId\" = \"SelectedPackageOperationId\" AND \"ActiveGeneration\" = \"SelectedGeneration\")"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_OperationKind",
                        "\"OperationKind\" IN ('Activate', 'Remove', 'Replace', 'Restart')"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_OutcomeCode",
                        "\"OutcomeCode\" IN ('Preparing', 'Migrating', 'Activated', 'Removed', 'RestartScheduled', 'Restarted', 'Faulted', 'Recovered')"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_lifecycles_FailureCode",
                        "\"FailureCode\" IS NULL OR \"FailureCode\" IN ('PreparationRejected', 'PreparationFailed', 'MigrationFailed', 'ActivationFailed', 'WorkerStartFailed', 'WorkerDisposalFailed', 'WorkerExited', 'DrainTimedOut', 'CancellationFailed', 'RemovalFailed', 'RecoveryPackageUnavailable', 'RecoveryFailed', 'GenerationExhausted')"
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
}
