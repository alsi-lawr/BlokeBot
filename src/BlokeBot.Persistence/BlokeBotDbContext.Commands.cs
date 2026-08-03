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
        _ = modelBuilder.Entity<BotReplySettings>(static b =>
        {
            _ = b.ToTable("reply_settings");
            _ = b.HasKey(static x => x.Id);
            _ = b.HasOne(static x => x.GuessRoundProfile)
                .WithOne(static x => x.ReplySettings)
                .HasForeignKey<BotReplySettings>(static x => x.GuessRoundProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CommandAlias>(static b =>
        {
            _ = b.ToTable(
                "command_aliases",
                static t =>
                    t.HasCheckConstraint(
                        "CK_command_aliases_Kind",
                        KindIn("Kind", _commandAliasKinds)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Kind)
                .HasConversion(
                    static kind => PersistedEnumTokens<AppCommandKind>.Format(kind),
                    static value => PersistedEnumTokens<AppCommandKind>.Parse(value)
                )
                .HasMaxLength(64);
            _ = b.Property(static x => x.Alias).HasMaxLength(64);
            _ = b.HasIndex(static x => new { x.HostId, x.Alias }).IsUnique();
            _ = b.HasIndex(static x => new { x.HostId, x.GuessRoundProfileId });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(static x => x.GuessRoundProfile)
                .WithMany(static x => x.CommandAliases)
                .HasForeignKey(static x => new { x.HostId, x.GuessRoundProfileId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CustomMessageLibraryEntry>(static b =>
        {
            _ = b.ToTable(
                "custom_message_library_entries",
                static t =>
                    t.HasCheckConstraint(
                        "CK_custom_message_library_entries_SelectionMode",
                        KindIn("SelectionMode", _customMessageSelectionModes)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.HasAlternateKey(static x => new { x.HostId, x.Id });
            _ = b.Property(static x => x.Name).HasMaxLength(128);
            _ = b.Property(static x => x.SelectionMode)
                .HasConversion(
                    static mode => PersistedEnumTokens<CustomMessageSelectionMode>.Format(mode),
                    static value => PersistedEnumTokens<CustomMessageSelectionMode>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(static x => new { x.HostId, x.Name }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Variants)
                .WithOne(static x => x.Entry)
                .HasForeignKey(static x => x.CustomMessageLibraryEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CustomMessageVariant>(static b =>
        {
            _ = b.ToTable("custom_message_variants");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Text).HasMaxLength(500);
            _ = b.HasIndex(static x => new { x.CustomMessageLibraryEntryId, x.SortOrder })
                .IsUnique();
        });

        _ = modelBuilder.Entity<CustomCounter>(static b =>
        {
            _ = b.ToTable("custom_counters");
            _ = b.HasKey(static x => x.Id);
            _ = b.HasAlternateKey(static x => new { x.HostId, x.Id });
            _ = b.Property(static x => x.Name).HasMaxLength(128);
            _ = b.HasIndex(static x => new { x.HostId, x.Name }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CustomCommand>(static b =>
        {
            _ = b.ToTable(
                "custom_commands",
                static t =>
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
            _ = b.HasKey(static x => x.Id);
            _ = b.HasAlternateKey(static x => new { x.HostId, x.Id });
            _ = b.Property(static x => x.Name).HasMaxLength(128);
            _ = b.Property(static x => x.CooldownScope)
                .HasConversion(
                    static scope => PersistedEnumTokens<CustomCommandCooldownScope>.Format(scope),
                    static value => PersistedEnumTokens<CustomCommandCooldownScope>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.InvocationLimit)
                .HasConversion(
                    static limit => PersistedEnumTokens<CustomCommandInvocationLimit>.Format(limit),
                    static value => PersistedEnumTokens<CustomCommandInvocationLimit>.Parse(value)
                )
                .HasMaxLength(32)
                .HasDefaultValue(CustomCommandInvocationLimit.Unlimited);
            _ = b.HasIndex(static x => new { x.HostId, x.Name }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(static x => x.Action)
                .WithOne(static x => x.Command)
                .HasForeignKey<CustomCommandAction>(static x => new { x.HostId, x.CustomCommandId })
                .HasPrincipalKey<CustomCommand>(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CustomCommandAction>(static b =>
        {
            _ = b.ToTable(
                "custom_command_actions",
                static t =>
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
            _ = b.HasKey(static x => x.CustomCommandId);
            _ = b.Property<string>("ActionType").HasMaxLength(32);
            _ = b.HasDiscriminator<string>("ActionType")
                .HasValue<MessageCustomCommandAction>(MessageCustomCommandAction.Discriminator)
                .HasValue<CounterCustomCommandAction>(CounterCustomCommandAction.Discriminator)
                .HasValue<OverlayCueCustomCommandAction>(
                    OverlayCueCustomCommandAction.Discriminator
                );
            _ = b.HasOne(static x => x.ZeroArgumentMessageLibraryEntry)
                .WithMany()
                .HasForeignKey(static x => new { x.HostId, x.ZeroArgumentMessageLibraryEntryId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasOne(static x => x.OneArgumentMessageLibraryEntry)
                .WithMany()
                .HasForeignKey(static x => new { x.HostId, x.OneArgumentMessageLibraryEntryId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasOne(static x => x.TwoArgumentMessageLibraryEntry)
                .WithMany()
                .HasForeignKey(static x => new { x.HostId, x.TwoArgumentMessageLibraryEntryId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        _ = modelBuilder.Entity<CounterCustomCommandAction>(static b =>
            b.HasOne(static x => x.Counter)
                .WithMany()
                .HasForeignKey(static x => new { x.HostId, x.CounterId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
        );

        _ = modelBuilder.Entity<OverlayCueCustomCommandAction>(static b =>
        {
            _ = b.Property(static x => x.TargetOverlayPublicId).HasConversion<string>();
            _ = b.Property(static x => x.CuePublicId).HasConversion<string>();
            _ = b.Property(static x => x.QueuePolicy)
                .HasConversion(
                    static value => PersistedEnumTokens<OverlayCueQueuePolicy>.Format(value),
                    static value => PersistedEnumTokens<OverlayCueQueuePolicy>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.ReplyOrder)
                .HasConversion(
                    static value => PersistedEnumTokens<OverlayCueReplyOrder>.Format(value),
                    static value => PersistedEnumTokens<OverlayCueReplyOrder>.Parse(value)
                )
                .HasMaxLength(16);
        });

        _ = modelBuilder.Entity<CustomCommandAlias>(static b =>
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

        _ = modelBuilder.Entity<CustomCommandInvocationClaim>(static b =>
        {
            _ = b.ToTable(
                "custom_command_invocation_claims",
                static t =>
                    t.HasCheckConstraint(
                        "CK_custom_command_invocation_claims_Scope",
                        "(TwitchUserId IS NULL AND TwitchStreamId IS NOT NULL) OR "
                            + "(TwitchUserId IS NOT NULL AND TwitchStreamId IS NULL) OR "
                            + "(TwitchUserId IS NOT NULL AND TwitchStreamId IS NOT NULL)"
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
                .HasFilter("TwitchUserId IS NULL AND TwitchStreamId IS NOT NULL");
            _ = b.HasIndex(static x => new
                {
                    x.HostId,
                    x.CustomCommandId,
                    x.TwitchUserId,
                })
                .IsUnique()
                .HasFilter("TwitchUserId IS NOT NULL AND TwitchStreamId IS NULL");
            _ = b.HasIndex(static x => new
                {
                    x.HostId,
                    x.CustomCommandId,
                    x.TwitchUserId,
                    x.TwitchStreamId,
                })
                .IsUnique()
                .HasFilter("TwitchUserId IS NOT NULL AND TwitchStreamId IS NOT NULL");
            _ = b.HasOne(static x => x.Command)
                .WithMany()
                .HasForeignKey(static x => new { x.HostId, x.CustomCommandId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<CustomCommandInvocationResetAudit>(static b =>
        {
            _ = b.ToTable(
                "custom_command_invocation_reset_audits",
                static t =>
                    t.HasCheckConstraint(
                        "CK_custom_command_invocation_reset_audits_Scope",
                        KindIn("Scope", _customCommandInvocationResetScopes)
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
