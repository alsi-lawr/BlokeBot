using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static void ConfigureCommandInvocations(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<CustomCommandAlias>(b =>
        {
            _ = b.ToTable("custom_command_aliases");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Alias).HasMaxLength(64);
            _ = b.HasIndex(static x => new { x.HostId, x.Alias }).IsUnique();
            _ = b.HasIndex(static x => new { x.CustomCommandId, x.SortOrder }).IsUnique();
            _ = b.HasOne(static x => x.Command)
                .WithMany(static x => x.Aliases)
                .HasForeignKey(static x => new { x.HostId, x.CustomCommandId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CustomCommandInvocationClaim>(b =>
        {
            _ = b.ToTable(
                "custom_command_invocation_claims",
                t =>
                    t.HasCheckConstraint(
                        "CK_custom_command_invocation_claims_Scope",
                        ProviderSql(
                            modelBuilder,
                            "(TwitchUserId IS NULL AND TwitchStreamId IS NOT NULL) OR ",
                            "(\"TwitchUserId\" IS NULL AND \"TwitchStreamId\" IS NOT NULL) OR "
                        )
                            + ProviderSql(
                                modelBuilder,
                                "(TwitchUserId IS NOT NULL AND TwitchStreamId IS NULL) OR ",
                                "(\"TwitchUserId\" IS NOT NULL AND \"TwitchStreamId\" IS NULL) OR "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "(TwitchUserId IS NOT NULL AND TwitchStreamId IS NOT NULL)",
                                "(\"TwitchUserId\" IS NOT NULL AND \"TwitchStreamId\" IS NOT NULL)"
                            )
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.TwitchUserId).HasMaxLength(64);
            _ = b.Property(static x => x.TwitchStreamId).HasMaxLength(64);
            _ = b.HasIndex(static x => new
                {
                    x.HostId,
                    x.CustomCommandId,
                    x.TwitchStreamId,
                })
                .IsUnique()
                .HasFilter(
                    ProviderSql(
                        modelBuilder,
                        "TwitchUserId IS NULL AND TwitchStreamId IS NOT NULL",
                        "\"TwitchUserId\" IS NULL AND \"TwitchStreamId\" IS NOT NULL"
                    )
                );
            _ = b.HasIndex(static x => new
                {
                    x.HostId,
                    x.CustomCommandId,
                    x.TwitchUserId,
                })
                .IsUnique()
                .HasFilter(
                    ProviderSql(
                        modelBuilder,
                        "TwitchUserId IS NOT NULL AND TwitchStreamId IS NULL",
                        "\"TwitchUserId\" IS NOT NULL AND \"TwitchStreamId\" IS NULL"
                    )
                );
            _ = b.HasIndex(static x => new
                {
                    x.HostId,
                    x.CustomCommandId,
                    x.TwitchUserId,
                    x.TwitchStreamId,
                })
                .IsUnique()
                .HasFilter(
                    ProviderSql(
                        modelBuilder,
                        "TwitchUserId IS NOT NULL AND TwitchStreamId IS NOT NULL",
                        "\"TwitchUserId\" IS NOT NULL AND \"TwitchStreamId\" IS NOT NULL"
                    )
                );
            _ = b.HasOne(static x => x.Command)
                .WithMany()
                .HasForeignKey(static x => new { x.HostId, x.CustomCommandId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CustomCommandInvocationResetAudit>(b =>
        {
            _ = b.ToTable(
                "custom_command_invocation_reset_audits",
                t =>
                    t.HasCheckConstraint(
                        "CK_custom_command_invocation_reset_audits_Scope",
                        KindIn(modelBuilder, "Scope", _customCommandInvocationResetScopes)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.CommandName).HasMaxLength(128);
            _ = b.Property(static x => x.ActorTwitchUserId).HasMaxLength(64);
            _ = b.Property(static x => x.ActorLogin).HasMaxLength(64);
            _ = b.Property(static x => x.TargetTwitchUserId).HasMaxLength(64);
            _ = b.Property(static x => x.TargetLogin).HasMaxLength(64);
            _ = b.Property(static x => x.Scope)
                .HasConversion(
                    static scope =>
                        PersistedEnumTokens<CustomCommandInvocationResetScope>.Format(scope),
                    static value =>
                        PersistedEnumTokens<CustomCommandInvocationResetScope>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(static x => new { x.HostId, x.ResetAtUtc });
            _ = b.HasOne(static x => x.Command)
                .WithMany()
                .HasForeignKey(static x => x.CustomCommandId)
                .OnDelete(DeleteBehavior.SetNull);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
