using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _playQueueSelectionModes =
        PersistedEnumTokens<PlayQueueSelectionMode>.Values.ToArray();
    private static readonly string[] _playQueueEntryStatuses =
        PersistedEnumTokens<PlayQueueEntryStatus>.Values.ToArray();
    private static readonly string[] _playQueueEventKinds =
        PersistedEnumTokens<PlayQueueEventKind>.Values.ToArray();

    private static void ConfigurePlayWithViewers(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<PlayQueue>(b =>
        {
            _ = b.ToTable(
                "play_queues",
                t =>
                    t.HasCheckConstraint(
                        "CK_play_queues_SelectionMode",
                        KindIn(modelBuilder, "SelectionMode", _playQueueSelectionModes)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Slug).HasMaxLength(48);
            _ = b.Property(static x => x.Name).HasMaxLength(100);
            _ = b.Property(static x => x.ActivityName).HasMaxLength(100);
            _ = b.Property(static x => x.SelectionMode)
                .HasConversion(
                    static value => PersistedEnumTokens<PlayQueueSelectionMode>.Format(value),
                    static value => PersistedEnumTokens<PlayQueueSelectionMode>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(static x => new { x.HostId, x.Slug }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Fields)
                .WithOne(static x => x.Queue)
                .HasForeignKey(static x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.RoleRequirements)
                .WithOne(static x => x.Queue)
                .HasForeignKey(static x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Entries)
                .WithOne(static x => x.Queue)
                .HasForeignKey(static x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PlayQueueField>(b =>
        {
            _ = b.ToTable("play_queue_fields");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Key).HasMaxLength(48);
            _ = b.Property(static x => x.Label).HasMaxLength(100);
            _ = b.Property(static x => x.Choices).HasMaxLength(1000);
            _ = b.HasIndex(static x => new { x.QueueId, x.Key }).IsUnique();
            _ = b.HasIndex(static x => new { x.QueueId, x.Position }).IsUnique();
        });

        _ = modelBuilder.Entity<PlayQueueRoleRequirement>(b =>
        {
            _ = b.ToTable("play_queue_role_requirements");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Role).HasMaxLength(64);
            _ = b.HasIndex(static x => new { x.QueueId, x.Role }).IsUnique();
        });

        _ = modelBuilder.Entity<PlayQueueEntry>(b =>
        {
            _ = b.ToTable(
                "play_queue_entries",
                t =>
                    t.HasCheckConstraint(
                        "CK_play_queue_entries_Status",
                        KindIn(modelBuilder, "Status", _playQueueEntryStatuses)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.IdentityKey).HasMaxLength(160);
            _ = b.Property(static x => x.TwitchUserId).HasMaxLength(128);
            _ = b.Property(static x => x.NormalizedLogin).HasMaxLength(128);
            _ = b.Property(static x => x.DisplayName).HasMaxLength(128);
            _ = b.Property(static x => x.Status)
                .HasConversion(
                    static value => PersistedEnumTokens<PlayQueueEntryStatus>.Format(value),
                    static value => PersistedEnumTokens<PlayQueueEntryStatus>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.PrivateModeratorNote).HasMaxLength(1000);
            _ = b.HasIndex(static x => new { x.QueueId, x.IdentityKey }).IsUnique();
            _ = b.HasIndex(static x => new { x.QueueId, x.NormalizedLogin });
            _ = b.HasIndex(static x => new
            {
                x.QueueId,
                x.Status,
                x.Priority,
                x.JoinedAtUtc,
                x.Id,
            });
            _ = b.HasMany(static x => x.Values)
                .WithOne(static x => x.Entry)
                .HasForeignKey(static x => x.EntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PlayQueueEntryValue>(b =>
        {
            _ = b.ToTable("play_queue_entry_values");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Value).HasMaxLength(200);
            _ = b.HasIndex(static x => new { x.EntryId, x.FieldId }).IsUnique();
            _ = b.HasOne(static x => x.Field)
                .WithMany()
                .HasForeignKey(static x => x.FieldId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        _ = modelBuilder.Entity<PlayQueueParticipation>(b =>
        {
            _ = b.ToTable("play_queue_participation");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.IdentityKey).HasMaxLength(160);
            _ = b.HasIndex(static x => new
            {
                x.QueueId,
                x.IdentityKey,
                x.ParticipatedAtUtc,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne<PlayQueue>()
                .WithMany()
                .HasForeignKey(static x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PlayQueueExclusion>(b =>
        {
            _ = b.ToTable("play_queue_exclusions");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.IdentityKey).HasMaxLength(160);
            _ = b.Property(static x => x.PrivateReason).HasMaxLength(500);
            _ = b.HasIndex(static x => new
            {
                x.QueueId,
                x.IdentityKey,
                x.ExpiresAtUtc,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne<PlayQueue>()
                .WithMany()
                .HasForeignKey(static x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PlayQueueDomainEvent>(b =>
        {
            _ = b.ToTable(
                "play_queue_events",
                t =>
                    t.HasCheckConstraint(
                        "CK_play_queue_events_Kind",
                        KindIn(modelBuilder, "Kind", _playQueueEventKinds)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Kind)
                .HasConversion(
                    static value => PersistedEnumTokens<PlayQueueEventKind>.Format(value),
                    static value => PersistedEnumTokens<PlayQueueEventKind>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.PublicPayload).HasMaxLength(1024);
            _ = b.HasIndex(static x => new { x.HostId, x.Id });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne<PlayQueue>()
                .WithMany()
                .HasForeignKey(static x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
