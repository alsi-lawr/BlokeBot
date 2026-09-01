using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _blokeRaidCampaignStatuses =
        PersistedEnumTokens<BlokeRaidCampaignStatus>.Values.ToArray();
    private static readonly string[] _blokeRaidResetPolicies =
        PersistedEnumTokens<BlokeRaidResetPolicy>.Values.ToArray();
    private static readonly string[] _blokeRaidActionKinds =
        PersistedEnumTokens<BlokeRaidActionKind>.Values.ToArray();
    private static readonly string[] _blokeRaidActionSources =
        PersistedEnumTokens<BlokeRaidActionSource>.Values.ToArray();
    private static readonly string[] _blokeRaidEventKinds =
        PersistedEnumTokens<BlokeRaidEventKind>.Values.ToArray();

    private static void ConfigureBlokeRaid(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<BlokeRaidConfiguration>(builder =>
        {
            _ = builder.ToTable(
                "bloke_raid_configurations",
                table =>
                    table.HasCheckConstraint(
                        "CK_bloke_raid_configurations_ResetPolicy",
                        KindIn(modelBuilder, "ResetPolicy", _blokeRaidResetPolicies)
                    )
            );
            _ = builder.HasKey(static value => value.Id);
            _ = builder.Property(static value => value.BossName).HasMaxLength(120);
            _ = builder.Property(static value => value.SpecialPointCost).HasMaxLength(128);
            _ = builder.Property(static value => value.VictoryPointReward).HasMaxLength(128);
            _ = builder.Property(static value => value.PhaseOneResponse).HasMaxLength(500);
            _ = builder.Property(static value => value.PhaseTwoResponse).HasMaxLength(500);
            _ = builder.Property(static value => value.PhaseThreeResponse).HasMaxLength(500);
            _ = builder.Property(static value => value.VictoryResponse).HasMaxLength(500);
            _ = builder.Property(static value => value.ExpiryResponse).HasMaxLength(500);
            _ = builder
                .Property(static value => value.ResetPolicy)
                .HasConversion(
                    static value => PersistedEnumTokens<BlokeRaidResetPolicy>.Format(value),
                    static value => PersistedEnumTokens<BlokeRaidResetPolicy>.Parse(value)
                )
                .HasMaxLength(32);
            _ = builder.Property(static value => value.Revision).IsConcurrencyToken();
            _ = builder.HasIndex(static value => value.HostId).IsUnique();
            _ = builder
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BlokeRaidCampaign>(builder =>
        {
            _ = builder.ToTable(
                "bloke_raid_campaigns",
                table =>
                {
                    _ = table.HasCheckConstraint(
                        "CK_bloke_raid_campaigns_Status",
                        KindIn(modelBuilder, "Status", _blokeRaidCampaignStatuses)
                    );
                    _ = table.HasCheckConstraint(
                        "CK_bloke_raid_campaigns_ResetPolicy",
                        KindIn(modelBuilder, "ResetPolicy", _blokeRaidResetPolicies)
                    );
                }
            );
            _ = builder.HasKey(static value => value.Id);
            _ = builder.HasAlternateKey(static value => new { value.HostId, value.Id });
            _ = builder.Property(static value => value.PublicId).HasConversion<string>();
            _ = builder.Property(static value => value.StartOperationKey).HasMaxLength(200);
            _ = builder.Property(static value => value.BossName).HasMaxLength(120);
            _ = builder.Property(static value => value.VictoryPointReward).HasMaxLength(128);
            _ = builder
                .Property(static value => value.Status)
                .HasConversion(
                    static value => PersistedEnumTokens<BlokeRaidCampaignStatus>.Format(value),
                    static value => PersistedEnumTokens<BlokeRaidCampaignStatus>.Parse(value)
                )
                .HasMaxLength(32);
            _ = builder
                .Property(static value => value.ResetPolicy)
                .HasConversion(
                    static value => PersistedEnumTokens<BlokeRaidResetPolicy>.Format(value),
                    static value => PersistedEnumTokens<BlokeRaidResetPolicy>.Parse(value)
                )
                .HasMaxLength(32);
            _ = builder.Property(static value => value.Revision).IsConcurrencyToken();
            _ = builder.HasIndex(static value => value.PublicId).IsUnique();
            _ = builder
                .HasIndex(static value => new { value.HostId, value.StartOperationKey })
                .IsUnique();
            _ = builder
                .HasIndex(static value => value.HostId)
                .IsUnique()
                .HasFilter("\"Status\" = 'Active'");
            _ = builder
                .HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static value => value.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BlokeRaidAction>(builder =>
        {
            _ = builder.ToTable(
                "bloke_raid_actions",
                table =>
                {
                    _ = table.HasCheckConstraint(
                        "CK_bloke_raid_actions_Kind",
                        KindIn(modelBuilder, "Kind", _blokeRaidActionKinds)
                    );
                    _ = table.HasCheckConstraint(
                        "CK_bloke_raid_actions_Source",
                        KindIn(modelBuilder, "Source", _blokeRaidActionSources)
                    );
                }
            );
            _ = builder.HasKey(static value => value.Id);
            _ = builder.Property(static value => value.OperationKey).HasMaxLength(200);
            _ = builder
                .Property(static value => value.Kind)
                .HasConversion(
                    static value => PersistedEnumTokens<BlokeRaidActionKind>.Format(value),
                    static value => PersistedEnumTokens<BlokeRaidActionKind>.Parse(value)
                )
                .HasMaxLength(32);
            _ = builder
                .Property(static value => value.Source)
                .HasConversion(
                    static value => PersistedEnumTokens<BlokeRaidActionSource>.Format(value),
                    static value => PersistedEnumTokens<BlokeRaidActionSource>.Parse(value)
                )
                .HasMaxLength(32);
            _ = builder.Property(static value => value.ViewerTwitchUserId).HasMaxLength(128);
            _ = builder.Property(static value => value.ViewerLogin).HasMaxLength(128);
            _ = builder.Property(static value => value.ViewerDisplayName).HasMaxLength(128);
            _ = builder.Property(static value => value.StreamKey).HasMaxLength(160);
            _ = builder.Property(static value => value.PointCost).HasMaxLength(128);
            _ = builder.Property(static value => value.Response).HasMaxLength(500);
            _ = builder
                .HasIndex(static value => new { value.HostId, value.OperationKey })
                .IsUnique();
            _ = builder.HasIndex(static value => new
            {
                value.HostId,
                value.CampaignId,
                value.ViewerTwitchUserId,
                value.Kind,
                value.OccurredAtUtc,
            });
            _ = builder
                .HasOne(static value => value.Campaign)
                .WithMany(static value => value.Actions)
                .HasForeignKey(static value => new { value.HostId, value.CampaignId })
                .HasPrincipalKey(static value => new { value.HostId, value.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BlokeRaidContribution>(builder =>
        {
            _ = builder.ToTable("bloke_raid_contributions");
            _ = builder.HasKey(static value => value.Id);
            _ = builder.Property(static value => value.ViewerTwitchUserId).HasMaxLength(128);
            _ = builder.Property(static value => value.ViewerLogin).HasMaxLength(128);
            _ = builder.Property(static value => value.ViewerDisplayName).HasMaxLength(128);
            _ = builder
                .HasIndex(static value => new
                {
                    value.HostId,
                    value.CampaignId,
                    value.ViewerTwitchUserId,
                })
                .IsUnique();
            _ = builder
                .HasOne(static value => value.Campaign)
                .WithMany(static value => value.Contributions)
                .HasForeignKey(static value => new { value.HostId, value.CampaignId })
                .HasPrincipalKey(static value => new { value.HostId, value.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<BlokeRaidDomainEvent>(builder =>
        {
            _ = builder.ToTable(
                "bloke_raid_events",
                table =>
                    table.HasCheckConstraint(
                        "CK_bloke_raid_events_Kind",
                        KindIn(modelBuilder, "Kind", _blokeRaidEventKinds)
                    )
            );
            _ = builder.HasKey(static value => value.Id);
            _ = builder
                .Property(static value => value.Kind)
                .HasConversion(
                    static value => PersistedEnumTokens<BlokeRaidEventKind>.Format(value),
                    static value => PersistedEnumTokens<BlokeRaidEventKind>.Parse(value)
                )
                .HasMaxLength(32);
            _ = builder.Property(static value => value.OperationKey).HasMaxLength(200);
            _ = builder.Property(static value => value.PublicPayload).HasMaxLength(4096);
            _ = builder
                .HasIndex(static value => new { value.HostId, value.OperationKey })
                .IsUnique();
            _ = builder
                .HasOne(static value => value.Campaign)
                .WithMany(static value => value.Events)
                .HasForeignKey(static value => new { value.HostId, value.CampaignId })
                .HasPrincipalKey(static value => new { value.HostId, value.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
