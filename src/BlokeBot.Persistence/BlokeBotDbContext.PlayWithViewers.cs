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
        modelBuilder.Entity<PlayQueue>(b =>
        {
            b.ToTable(
                "play_queues",
                t =>
                    t.HasCheckConstraint(
                        "CK_play_queues_SelectionMode",
                        KindIn("SelectionMode", _playQueueSelectionModes)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Slug).HasMaxLength(48);
            b.Property(x => x.Name).HasMaxLength(100);
            b.Property(x => x.ActivityName).HasMaxLength(100);
            b.Property(x => x.SelectionMode)
                .HasConversion(
                    value => PersistedEnumTokens<PlayQueueSelectionMode>.Format(value),
                    value => PersistedEnumTokens<PlayQueueSelectionMode>.Parse(value)
                )
                .HasMaxLength(32);
            b.HasIndex(x => new { x.HostId, x.Slug }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Fields)
                .WithOne(x => x.Queue)
                .HasForeignKey(x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.RoleRequirements)
                .WithOne(x => x.Queue)
                .HasForeignKey(x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Entries)
                .WithOne(x => x.Queue)
                .HasForeignKey(x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayQueueField>(b =>
        {
            b.ToTable("play_queue_fields");
            b.HasKey(x => x.Id);
            b.Property(x => x.Key).HasMaxLength(48);
            b.Property(x => x.Label).HasMaxLength(100);
            b.Property(x => x.Choices).HasMaxLength(1000);
            b.HasIndex(x => new { x.QueueId, x.Key }).IsUnique();
            b.HasIndex(x => new { x.QueueId, x.Position }).IsUnique();
        });

        modelBuilder.Entity<PlayQueueRoleRequirement>(b =>
        {
            b.ToTable("play_queue_role_requirements");
            b.HasKey(x => x.Id);
            b.Property(x => x.Role).HasMaxLength(64);
            b.HasIndex(x => new { x.QueueId, x.Role }).IsUnique();
        });

        modelBuilder.Entity<PlayQueueEntry>(b =>
        {
            b.ToTable(
                "play_queue_entries",
                t =>
                    t.HasCheckConstraint(
                        "CK_play_queue_entries_Status",
                        KindIn("Status", _playQueueEntryStatuses)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.IdentityKey).HasMaxLength(160);
            b.Property(x => x.TwitchUserId).HasMaxLength(128);
            b.Property(x => x.NormalizedLogin).HasMaxLength(128);
            b.Property(x => x.DisplayName).HasMaxLength(128);
            b.Property(x => x.Status)
                .HasConversion(
                    value => PersistedEnumTokens<PlayQueueEntryStatus>.Format(value),
                    value => PersistedEnumTokens<PlayQueueEntryStatus>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.PrivateModeratorNote).HasMaxLength(1000);
            b.HasIndex(x => new { x.QueueId, x.IdentityKey }).IsUnique();
            b.HasIndex(x => new { x.QueueId, x.NormalizedLogin });
            b.HasIndex(x => new
            {
                x.QueueId,
                x.Status,
                x.Priority,
                x.JoinedAtUtc,
                x.Id,
            });
            b.HasMany(x => x.Values)
                .WithOne(x => x.Entry)
                .HasForeignKey(x => x.EntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayQueueEntryValue>(b =>
        {
            b.ToTable("play_queue_entry_values");
            b.HasKey(x => x.Id);
            b.Property(x => x.Value).HasMaxLength(200);
            b.HasIndex(x => new { x.EntryId, x.FieldId }).IsUnique();
            b.HasOne(x => x.Field)
                .WithMany()
                .HasForeignKey(x => x.FieldId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlayQueueParticipation>(b =>
        {
            b.ToTable("play_queue_participation");
            b.HasKey(x => x.Id);
            b.Property(x => x.IdentityKey).HasMaxLength(160);
            b.HasIndex(x => new
            {
                x.QueueId,
                x.IdentityKey,
                x.ParticipatedAtUtc,
            });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<PlayQueue>()
                .WithMany()
                .HasForeignKey(x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayQueueExclusion>(b =>
        {
            b.ToTable("play_queue_exclusions");
            b.HasKey(x => x.Id);
            b.Property(x => x.IdentityKey).HasMaxLength(160);
            b.Property(x => x.PrivateReason).HasMaxLength(500);
            b.HasIndex(x => new
            {
                x.QueueId,
                x.IdentityKey,
                x.ExpiresAtUtc,
            });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<PlayQueue>()
                .WithMany()
                .HasForeignKey(x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayQueueDomainEvent>(b =>
        {
            b.ToTable(
                "play_queue_events",
                t =>
                    t.HasCheckConstraint(
                        "CK_play_queue_events_Kind",
                        KindIn("Kind", _playQueueEventKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind)
                .HasConversion(
                    value => PersistedEnumTokens<PlayQueueEventKind>.Format(value),
                    value => PersistedEnumTokens<PlayQueueEventKind>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.PublicPayload).HasMaxLength(1024);
            b.HasIndex(x => new { x.HostId, x.Id });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<PlayQueue>()
                .WithMany()
                .HasForeignKey(x => x.QueueId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
