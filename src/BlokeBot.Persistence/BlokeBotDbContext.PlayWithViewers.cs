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
                        KindIn("SelectionMode", _playQueueSelectionModes)
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Slug).HasMaxLength(48);
            _ = b.Property(x => x.Name).HasMaxLength(100);
            _ = b.Property(x => x.ActivityName).HasMaxLength(100);
            _ = b.Property(x => x.SelectionMode)
                .HasConversion(
                    value => PersistedEnumTokens<PlayQueueSelectionMode>.Format(value),
                    value => PersistedEnumTokens<PlayQueueSelectionMode>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.HasIndex(x => new { x.HostId, x.Slug }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(x => x.Fields)
                .WithOne(x => x.Queue)
                .HasForeignKey(x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(x => x.RoleRequirements)
                .WithOne(x => x.Queue)
                .HasForeignKey(x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(x => x.Entries)
                .WithOne(x => x.Queue)
                .HasForeignKey(x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PlayQueueField>(b =>
        {
            _ = b.ToTable("play_queue_fields");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Key).HasMaxLength(48);
            _ = b.Property(x => x.Label).HasMaxLength(100);
            _ = b.Property(x => x.Choices).HasMaxLength(1000);
            _ = b.HasIndex(x => new { x.QueueId, x.Key }).IsUnique();
            _ = b.HasIndex(x => new { x.QueueId, x.Position }).IsUnique();
        });

        _ = modelBuilder.Entity<PlayQueueRoleRequirement>(b =>
        {
            _ = b.ToTable("play_queue_role_requirements");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Role).HasMaxLength(64);
            _ = b.HasIndex(x => new { x.QueueId, x.Role }).IsUnique();
        });

        _ = modelBuilder.Entity<PlayQueueEntry>(b =>
        {
            _ = b.ToTable(
                "play_queue_entries",
                t =>
                    t.HasCheckConstraint(
                        "CK_play_queue_entries_Status",
                        KindIn("Status", _playQueueEntryStatuses)
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.IdentityKey).HasMaxLength(160);
            _ = b.Property(x => x.TwitchUserId).HasMaxLength(128);
            _ = b.Property(x => x.NormalizedLogin).HasMaxLength(128);
            _ = b.Property(x => x.DisplayName).HasMaxLength(128);
            _ = b.Property(x => x.Status)
                .HasConversion(
                    value => PersistedEnumTokens<PlayQueueEntryStatus>.Format(value),
                    value => PersistedEnumTokens<PlayQueueEntryStatus>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(x => x.PrivateModeratorNote).HasMaxLength(1000);
            _ = b.HasIndex(x => new { x.QueueId, x.IdentityKey }).IsUnique();
            _ = b.HasIndex(x => new { x.QueueId, x.NormalizedLogin });
            _ = b.HasIndex(x => new
            {
                x.QueueId,
                x.Status,
                x.Priority,
                x.JoinedAtUtc,
                x.Id,
            });
            _ = b.HasMany(x => x.Values)
                .WithOne(x => x.Entry)
                .HasForeignKey(x => x.EntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PlayQueueEntryValue>(b =>
        {
            _ = b.ToTable("play_queue_entry_values");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Value).HasMaxLength(200);
            _ = b.HasIndex(x => new { x.EntryId, x.FieldId }).IsUnique();
            _ = b.HasOne(x => x.Field)
                .WithMany()
                .HasForeignKey(x => x.FieldId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        _ = modelBuilder.Entity<PlayQueueParticipation>(b =>
        {
            _ = b.ToTable("play_queue_participation");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.IdentityKey).HasMaxLength(160);
            _ = b.HasIndex(x => new
            {
                x.QueueId,
                x.IdentityKey,
                x.ParticipatedAtUtc,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne<PlayQueue>()
                .WithMany()
                .HasForeignKey(x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PlayQueueExclusion>(b =>
        {
            _ = b.ToTable("play_queue_exclusions");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.IdentityKey).HasMaxLength(160);
            _ = b.Property(x => x.PrivateReason).HasMaxLength(500);
            _ = b.HasIndex(x => new
            {
                x.QueueId,
                x.IdentityKey,
                x.ExpiresAtUtc,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne<PlayQueue>()
                .WithMany()
                .HasForeignKey(x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<PlayQueueDomainEvent>(b =>
        {
            _ = b.ToTable(
                "play_queue_events",
                t =>
                    t.HasCheckConstraint(
                        "CK_play_queue_events_Kind",
                        KindIn("Kind", _playQueueEventKinds)
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Kind)
                .HasConversion(
                    value => PersistedEnumTokens<PlayQueueEventKind>.Format(value),
                    value => PersistedEnumTokens<PlayQueueEventKind>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(x => x.PublicPayload).HasMaxLength(1024);
            _ = b.HasIndex(x => new { x.HostId, x.Id });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne<PlayQueue>()
                .WithMany()
                .HasForeignKey(x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
