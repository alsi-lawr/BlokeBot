using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    public DbSet<PluginInstallationConfigurationRecord> PluginInstallationConfigurations =>
        Set<PluginInstallationConfigurationRecord>();

    public DbSet<PluginInstallationSecretRecord> PluginInstallationSecrets =>
        Set<PluginInstallationSecretRecord>();

    public DbSet<PluginFeatureConfigurationRecord> PluginFeatureConfigurations =>
        Set<PluginFeatureConfigurationRecord>();

    public DbSet<PluginFeatureSecretRecord> PluginFeatureSecrets =>
        Set<PluginFeatureSecretRecord>();

    public DbSet<PluginFeatureStateRecord> PluginFeatureStates => Set<PluginFeatureStateRecord>();

    private static void ConfigurePluginFeatures(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<PluginInstallationConfigurationRecord>(static entity =>
        {
            _ = entity.ToTable(
                "plugin_installation_configurations",
                table =>
                {
                    _ = table.HasCheckConstraint(
                        "CK_plugin_installation_configurations_ValuesJson",
                        $"json_valid(\"ValuesJson\") AND json_type(\"ValuesJson\") = 'array' AND length(CAST(\"ValuesJson\" AS BLOB)) <= {PluginContractLimits.MaximumOrdinarySettingsJsonBytes}"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_installation_configurations_Revision",
                        "\"Revision\" >= 0"
                    );
                }
            );
            _ = entity.HasKey(static value => value.PluginId);
            _ = entity.Property(static value => value.PluginId).HasMaxLength(64);
            _ = entity.Property(static value => value.Revision).IsConcurrencyToken();
        });

        _ = modelBuilder.Entity<PluginInstallationSecretRecord>(static entity =>
        {
            _ = entity.ToTable(
                "plugin_installation_secrets",
                table =>
                    table.HasCheckConstraint(
                        "CK_plugin_installation_secrets_ProtectedValue",
                        "length(\"ProtectedValue\") > 0 AND length(\"ProtectedValue\") <= 32768"
                    )
            );
            _ = entity.HasKey(static value => new { value.PluginId, value.SettingId });
            _ = entity.Property(static value => value.PluginId).HasMaxLength(64);
            _ = entity.Property(static value => value.SettingId).HasMaxLength(64);
            _ = entity
                .HasOne<PluginInstallationConfigurationRecord>()
                .WithMany()
                .HasForeignKey(static value => value.PluginId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PluginFeatureConfigurationRecord>(static entity =>
        {
            _ = entity.ToTable(
                "plugin_feature_configurations",
                table =>
                {
                    _ = table.HasCheckConstraint(
                        "CK_plugin_feature_configurations_ValuesJson",
                        $"json_valid(\"ValuesJson\") AND json_type(\"ValuesJson\") = 'array' AND length(CAST(\"ValuesJson\" AS BLOB)) <= {PluginContractLimits.MaximumOrdinarySettingsJsonBytes}"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_feature_configurations_Revision",
                        "\"Revision\" >= 0"
                    );
                }
            );
            _ = entity.HasKey(static value => new
            {
                value.PluginId,
                value.FeatureId,
                value.HostId,
            });
            _ = entity.Property(static value => value.PluginId).HasMaxLength(64);
            _ = entity.Property(static value => value.FeatureId).HasMaxLength(64);
            _ = entity.Property(static value => value.Revision).IsConcurrencyToken();
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PluginFeatureSecretRecord>(static entity =>
        {
            _ = entity.ToTable(
                "plugin_feature_secrets",
                table =>
                    table.HasCheckConstraint(
                        "CK_plugin_feature_secrets_ProtectedValue",
                        "length(\"ProtectedValue\") > 0 AND length(\"ProtectedValue\") <= 32768"
                    )
            );
            _ = entity.HasKey(static value => new
            {
                value.PluginId,
                value.FeatureId,
                value.HostId,
                value.SettingId,
            });
            _ = entity.Property(static value => value.PluginId).HasMaxLength(64);
            _ = entity.Property(static value => value.FeatureId).HasMaxLength(64);
            _ = entity.Property(static value => value.SettingId).HasMaxLength(64);
            _ = entity
                .HasOne<PluginFeatureConfigurationRecord>()
                .WithMany()
                .HasForeignKey(static value => new
                {
                    value.PluginId,
                    value.FeatureId,
                    value.HostId,
                })
                .OnDelete(DeleteBehavior.Cascade);
        });

        ConfigurePluginFeatureStates(modelBuilder);
    }

    private static void ConfigurePluginFeatureStates(ModelBuilder modelBuilder) =>
        _ = modelBuilder.Entity<PluginFeatureStateRecord>(static entity =>
        {
            _ = entity.ToTable(
                "plugin_feature_states",
                table =>
                {
                    _ = table.HasCheckConstraint(
                        "CK_plugin_feature_states_Generations",
                        "\"WorkerGeneration\" > 0 AND \"FeatureGeneration\" > 0"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_feature_states_Revision",
                        "\"Revision\" > 0"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_feature_states_Readiness",
                        "\"Readiness\" IN ('Disabled', 'EnabledDegraded', 'Ready')"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_feature_states_Reason",
                        "(\"Readiness\" = 'EnabledDegraded' AND \"ReasonCode\" IS NOT NULL AND \"RecoveryAction\" IS NOT NULL AND \"ReasonDetail\" IS NOT NULL) OR "
                            + "(\"Readiness\" <> 'EnabledDegraded' AND \"ReasonCode\" IS NULL AND \"RecoveryAction\" IS NULL AND \"ReasonDetail\" IS NULL)"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_feature_states_ReasonCode",
                        "\"ReasonCode\" IS NULL OR \"ReasonCode\" IN ('MissingScopes', 'ReconciliationPending', 'ReconciliationFailed')"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_feature_states_RecoveryAction",
                        "\"RecoveryAction\" IS NULL OR \"RecoveryAction\" IN ('ReconnectTwitch', 'Retry')"
                    );
                    _ = table.HasCheckConstraint(
                        "CK_plugin_feature_states_ReasonDetail",
                        $"\"ReasonDetail\" IS NULL OR length(trim(\"ReasonDetail\")) BETWEEN 1 AND {PluginContractLimits.MaximumReadinessReasonCharacters}"
                    );
                }
            );
            _ = entity.HasKey(static value => new
            {
                value.PluginId,
                value.FeatureId,
                value.HostId,
            });
            _ = entity.Property(static value => value.PluginId).HasMaxLength(64);
            _ = entity.Property(static value => value.FeatureId).HasMaxLength(64);
            _ = entity.Property(static value => value.Readiness).HasConversion<string>();
            _ = entity.Property(static value => value.ReasonCode).HasConversion<string>();
            _ = entity.Property(static value => value.RecoveryAction).HasConversion<string>();
            _ = entity
                .Property(static value => value.ReasonDetail)
                .HasMaxLength(PluginContractLimits.MaximumReadinessReasonCharacters);
            _ = entity.Property(static value => value.Revision).IsConcurrencyToken();
            _ = entity
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}
