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
    ];

    private static readonly string[] _customCommandCooldownScopes =
        PersistedEnumTokens<CustomCommandCooldownScope>.Values.ToArray();

    private static readonly string[] _customMessageSelectionModes =
        PersistedEnumTokens<CustomMessageSelectionMode>.Values.ToArray();

    private static void ConfigureCommands(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BotReplySettings>(b =>
        {
            b.ToTable("reply_settings");
            b.HasKey(x => x.Id);
            b.HasOne(x => x.GuessRoundProfile)
                .WithOne(x => x.ReplySettings)
                .HasForeignKey<BotReplySettings>(x => x.GuessRoundProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommandAlias>(b =>
        {
            b.ToTable(
                "command_aliases",
                t =>
                    t.HasCheckConstraint(
                        "CK_command_aliases_Kind",
                        KindIn("Kind", _commandAliasKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind)
                .HasConversion(
                    kind => PersistedEnumTokens<AppCommandKind>.Format(kind),
                    value => PersistedEnumTokens<AppCommandKind>.Parse(value)
                )
                .HasMaxLength(64);
            b.Property(x => x.Alias).HasMaxLength(64);
            b.HasIndex(x => new { x.HostId, x.Alias }).IsUnique();
            b.HasIndex(x => new { x.HostId, x.GuessRoundProfileId });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.GuessRoundProfile)
                .WithMany(x => x.CommandAliases)
                .HasForeignKey(x => new { x.HostId, x.GuessRoundProfileId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomMessageLibraryEntry>(b =>
        {
            b.ToTable(
                "custom_message_library_entries",
                t =>
                    t.HasCheckConstraint(
                        "CK_custom_message_library_entries_SelectionMode",
                        KindIn("SelectionMode", _customMessageSelectionModes)
                    )
            );
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.HostId, x.Id });
            b.Property(x => x.Name).HasMaxLength(128);
            b.Property(x => x.SelectionMode)
                .HasConversion(
                    mode => PersistedEnumTokens<CustomMessageSelectionMode>.Format(mode),
                    value => PersistedEnumTokens<CustomMessageSelectionMode>.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new { x.HostId, x.Name }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Variants)
                .WithOne(x => x.Entry)
                .HasForeignKey(x => x.CustomMessageLibraryEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomMessageVariant>(b =>
        {
            b.ToTable("custom_message_variants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Text).HasMaxLength(500);
            b.HasIndex(x => new { x.CustomMessageLibraryEntryId, x.SortOrder }).IsUnique();
        });

        modelBuilder.Entity<CustomCounter>(b =>
        {
            b.ToTable("custom_counters");
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.HostId, x.Id });
            b.Property(x => x.Name).HasMaxLength(128);
            b.HasIndex(x => new { x.HostId, x.Name }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomCommand>(b =>
        {
            b.ToTable(
                "custom_commands",
                t =>
                    t.HasCheckConstraint(
                        "CK_custom_commands_CooldownScope",
                        KindIn("CooldownScope", _customCommandCooldownScopes)
                    )
            );
            b.HasKey(x => x.Id);
            b.HasAlternateKey(x => new { x.HostId, x.Id });
            b.Property(x => x.Name).HasMaxLength(128);
            b.Property(x => x.CooldownScope)
                .HasConversion(
                    scope => PersistedEnumTokens<CustomCommandCooldownScope>.Format(scope),
                    value => PersistedEnumTokens<CustomCommandCooldownScope>.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new { x.HostId, x.Name }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Action)
                .WithOne(x => x.Command)
                .HasForeignKey<CustomCommandAction>(x => new { x.HostId, x.CustomCommandId })
                .HasPrincipalKey<CustomCommand>(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomCommandAction>(b =>
        {
            b.ToTable(
                "custom_command_actions",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_custom_command_actions_ActionType",
                        KindIn("ActionType", _customCommandActionTypes)
                    );
                    t.HasCheckConstraint(
                        "CK_custom_command_actions_Payload",
                        "(ActionType = 'Message' AND CounterId IS NULL) OR "
                            + "(ActionType = 'Counter' AND CounterId IS NOT NULL)"
                    );
                }
            );
            b.HasKey(x => x.CustomCommandId);
            b.Property<string>("ActionType").HasMaxLength(32);
            b.HasDiscriminator<string>("ActionType")
                .HasValue<MessageCustomCommandAction>(MessageCustomCommandAction.Discriminator)
                .HasValue<CounterCustomCommandAction>(CounterCustomCommandAction.Discriminator);
            b.HasOne(x => x.MessageLibraryEntry)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.MessageLibraryEntryId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CounterCustomCommandAction>(b =>
            b.HasOne(x => x.Counter)
                .WithMany()
                .HasForeignKey(x => new { x.HostId, x.CounterId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
        );

        modelBuilder.Entity<CustomCommandAlias>(b =>
        {
            b.ToTable("custom_command_aliases");
            b.HasKey(x => x.Id);
            b.Property(x => x.Alias).HasMaxLength(64);
            b.HasIndex(x => new { x.HostId, x.Alias }).IsUnique();
            b.HasOne(x => x.Command)
                .WithMany(x => x.Aliases)
                .HasForeignKey(x => new { x.HostId, x.CustomCommandId })
                .HasPrincipalKey(x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
