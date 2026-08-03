using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _commandAliasKinds =
        PersistedEnumTokens<AppCommandKind>.Values.ToArray();

    private static readonly string[] _customCommandActionTypes =
    [
        CounterCustomCommandAction.Discriminator,
        MessageCustomCommandAction.Discriminator,
        OverlayCueCustomCommandAction.Discriminator,
    ];

    private static readonly string[] _customCommandCooldownScopes =
        PersistedEnumTokens<CustomCommandCooldownScope>.Values.ToArray();

    private static readonly string[] _customCommandInvocationLimits =
        PersistedEnumTokens<CustomCommandInvocationLimit>.Values.ToArray();

    private static readonly string[] _customCommandInvocationResetScopes =
        PersistedEnumTokens<CustomCommandInvocationResetScope>.Values.ToArray();

    private static readonly string[] _overlayCueReplyOrders =
        PersistedEnumTokens<OverlayCueReplyOrder>.Values.ToArray();

    private static readonly string[] _customMessageSelectionModes =
        PersistedEnumTokens<CustomMessageSelectionMode>.Values.ToArray();

    private static void ConfigureCommands(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<BotReplySettings>(b =>
        {
            _ = b.ToTable("reply_settings");
            _ = b.HasKey(x => x.Id);
            _ = b.HasOne(x => x.GuessRoundProfile)
                .WithOne(x => x.ReplySettings)
                .HasForeignKey<BotReplySettings>(x => x.GuessRoundProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CommandAlias>(b =>
        {
            _ = b.ToTable(
                "command_aliases",
                t =>
                    t.HasCheckConstraint(
                        "CK_command_aliases_Kind",
                        KindIn("Kind", _commandAliasKinds)
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Kind)
                .HasConversion(
                    kind => PersistedEnumTokens<AppCommandKind>.Format(kind),
                    value => PersistedEnumTokens<AppCommandKind>.Parse(value)
                )
                .HasMaxLength(64);
            _ = b.Property(x => x.Alias).HasMaxLength(64);
            _ = b.HasIndex(x => new { x.HostId, x.Alias }).IsUnique();
            _ = b.HasIndex(x => new { x.HostId, x.GuessRoundProfileId });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(x => x.GuessRoundProfile)
                .WithMany(x => x.CommandAliases)
                .HasForeignKey(x => new { x.HostId, x.GuessRoundProfileId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CustomMessageLibraryEntry>(b =>
        {
            _ = b.ToTable(
                "custom_message_library_entries",
                t =>
                    t.HasCheckConstraint(
                        "CK_custom_message_library_entries_SelectionMode",
                        KindIn("SelectionMode", _customMessageSelectionModes)
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.HasAlternateKey(x => new { x.HostId, x.Id });
            _ = b.Property(x => x.Name).HasMaxLength(128);
            _ = b.Property(x => x.SelectionMode)
                .HasConversion(
                    mode => PersistedEnumTokens<CustomMessageSelectionMode>.Format(mode),
                    value => PersistedEnumTokens<CustomMessageSelectionMode>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(x => new { x.HostId, x.Name }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(x => x.Variants)
                .WithOne(x => x.Entry)
                .HasForeignKey(x => x.CustomMessageLibraryEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CustomMessageVariant>(b =>
        {
            _ = b.ToTable("custom_message_variants");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Text).HasMaxLength(500);
            _ = b.HasIndex(x => new { x.CustomMessageLibraryEntryId, x.SortOrder }).IsUnique();
        });

        _ = modelBuilder.Entity<CustomCounter>(b =>
        {
            _ = b.ToTable("custom_counters");
            _ = b.HasKey(x => x.Id);
            _ = b.HasAlternateKey(x => new { x.HostId, x.Id });
            _ = b.Property(x => x.Name).HasMaxLength(128);
            _ = b.HasIndex(x => new { x.HostId, x.Name }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CustomCommand>(b =>
        {
            _ = b.ToTable(
                "custom_commands",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_custom_commands_CooldownScope",
                        KindIn("CooldownScope", _customCommandCooldownScopes)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_commands_InvocationLimit",
                        KindIn("InvocationLimit", _customCommandInvocationLimits)
                    );
                }
            );
            _ = b.HasKey(x => x.Id);
            _ = b.HasAlternateKey(x => new { x.HostId, x.Id });
            _ = b.Property(x => x.Name).HasMaxLength(128);
            _ = b.Property(x => x.CooldownScope)
                .HasConversion(
                    scope => PersistedEnumTokens<CustomCommandCooldownScope>.Format(scope),
                    value => PersistedEnumTokens<CustomCommandCooldownScope>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(x => x.InvocationLimit)
                .HasConversion(
                    limit => PersistedEnumTokens<CustomCommandInvocationLimit>.Format(limit),
                    value => PersistedEnumTokens<CustomCommandInvocationLimit>.Parse(value)
                )
                .HasMaxLength(32)
                .HasDefaultValue(CustomCommandInvocationLimit.Unlimited);
            _ = b.HasIndex(x => new { x.HostId, x.Name }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(x => x.Action)
                .WithOne(x => x.Command)
                .HasForeignKey<CustomCommandAction>(x => new { x.HostId, x.CustomCommandId })
                .HasPrincipalKey<CustomCommand>(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CustomCommandAction>(b =>
        {
            _ = b.ToTable(
                "custom_command_actions",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_custom_command_actions_ActionType",
                        KindIn("ActionType", _customCommandActionTypes)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_command_actions_QueuePolicy",
                        KindInOrNull("QueuePolicy", _overlayCueQueuePolicies)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_command_actions_ReplyOrder",
                        KindInOrNull("ReplyOrder", _overlayCueReplyOrders)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_custom_command_actions_Payload",
                        "(ActionType = 'Message' AND CounterId IS NULL "
                            + "AND TargetOverlayPublicId IS NULL AND CuePublicId IS NULL "
                            + "AND QueuePolicy IS NULL AND ReplyOrder IS NULL) OR "
                            + "(ActionType = 'Counter' AND CounterId IS NOT NULL "
                            + "AND TargetOverlayPublicId IS NULL AND CuePublicId IS NULL "
                            + "AND QueuePolicy IS NULL AND ReplyOrder IS NULL) OR "
                            + "(ActionType = 'OverlayCue' AND CounterId IS NULL "
                            + "AND TargetOverlayPublicId IS NOT NULL AND CuePublicId IS NOT NULL "
                            + "AND QueuePolicy IS NOT NULL AND ReplyOrder IS NOT NULL)"
                    );
                }
            );
            _ = b.HasKey(x => x.CustomCommandId);
            _ = b.Property<string>("ActionType").HasMaxLength(32);
            _ = b.HasDiscriminator<string>("ActionType")
                .HasValue<MessageCustomCommandAction>(MessageCustomCommandAction.Discriminator)
                .HasValue<CounterCustomCommandAction>(CounterCustomCommandAction.Discriminator)
                .HasValue<OverlayCueCustomCommandAction>(
                    OverlayCueCustomCommandAction.Discriminator
                );
            _ = b.HasOne(x => x.ZeroArgumentMessageLibraryEntry)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.ZeroArgumentMessageLibraryEntryId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasOne(x => x.OneArgumentMessageLibraryEntry)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.OneArgumentMessageLibraryEntryId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasOne(x => x.TwoArgumentMessageLibraryEntry)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.TwoArgumentMessageLibraryEntryId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        _ = modelBuilder.Entity<CounterCustomCommandAction>(b =>
            b.HasOne(x => x.Counter)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.CounterId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
        );

        _ = modelBuilder.Entity<OverlayCueCustomCommandAction>(b =>
        {
            _ = b.Property(x => x.TargetOverlayPublicId).HasConversion<string>();
            _ = b.Property(x => x.CuePublicId).HasConversion<string>();
            _ = b.Property(x => x.QueuePolicy)
                .HasConversion(
                    value => PersistedEnumTokens<OverlayCueQueuePolicy>.Format(value),
                    value => PersistedEnumTokens<OverlayCueQueuePolicy>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(x => x.ReplyOrder)
                .HasConversion(
                    value => PersistedEnumTokens<OverlayCueReplyOrder>.Format(value),
                    value => PersistedEnumTokens<OverlayCueReplyOrder>.Parse(value)
                )
                .HasMaxLength(16);
        });

        _ = modelBuilder.Entity<CustomCommandAlias>(b =>
        {
            _ = b.ToTable("custom_command_aliases");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Alias).HasMaxLength(64);
            _ = b.HasIndex(x => new { x.HostId, x.Alias }).IsUnique();
            _ = b.HasIndex(x => new { x.CustomCommandId, x.SortOrder }).IsUnique();
            _ = b.HasOne(x => x.Command)
                .WithMany(x => x.Aliases)
                .HasForeignKey(x => new { x.HostId, x.CustomCommandId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CustomCommandInvocationClaim>(b =>
        {
            _ = b.ToTable(
                "custom_command_invocation_claims",
                t =>
                    t.HasCheckConstraint(
                        "CK_custom_command_invocation_claims_Scope",
                        "(TwitchUserId IS NULL AND TwitchStreamId IS NOT NULL) OR "
                            + "(TwitchUserId IS NOT NULL AND TwitchStreamId IS NULL) OR "
                            + "(TwitchUserId IS NOT NULL AND TwitchStreamId IS NOT NULL)"
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.TwitchUserId).HasMaxLength(64);
            _ = b.Property(x => x.TwitchStreamId).HasMaxLength(64);
            _ = b.HasIndex(x => new
                {
                    x.HostId,
                    x.CustomCommandId,
                    x.TwitchStreamId,
                })
                .IsUnique()
                .HasFilter("TwitchUserId IS NULL AND TwitchStreamId IS NOT NULL");
            _ = b.HasIndex(x => new
                {
                    x.HostId,
                    x.CustomCommandId,
                    x.TwitchUserId,
                })
                .IsUnique()
                .HasFilter("TwitchUserId IS NOT NULL AND TwitchStreamId IS NULL");
            _ = b.HasIndex(x => new
                {
                    x.HostId,
                    x.CustomCommandId,
                    x.TwitchUserId,
                    x.TwitchStreamId,
                })
                .IsUnique()
                .HasFilter("TwitchUserId IS NOT NULL AND TwitchStreamId IS NOT NULL");
            _ = b.HasOne(x => x.Command)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.CustomCommandId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CustomCommandInvocationResetAudit>(b =>
        {
            _ = b.ToTable(
                "custom_command_invocation_reset_audits",
                t =>
                    t.HasCheckConstraint(
                        "CK_custom_command_invocation_reset_audits_Scope",
                        KindIn("Scope", _customCommandInvocationResetScopes)
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.CommandName).HasMaxLength(128);
            _ = b.Property(x => x.ActorTwitchUserId).HasMaxLength(64);
            _ = b.Property(x => x.ActorLogin).HasMaxLength(64);
            _ = b.Property(x => x.TargetTwitchUserId).HasMaxLength(64);
            _ = b.Property(x => x.TargetLogin).HasMaxLength(64);
            _ = b.Property(x => x.Scope)
                .HasConversion(
                    scope => PersistedEnumTokens<CustomCommandInvocationResetScope>.Format(scope),
                    value => PersistedEnumTokens<CustomCommandInvocationResetScope>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(x => new { x.HostId, x.ResetAtUtc });
            _ = b.HasOne(x => x.Command)
                .WithMany()
                .HasForeignKey(x => x.CustomCommandId)
                .OnDelete(DeleteBehavior.SetNull);
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
