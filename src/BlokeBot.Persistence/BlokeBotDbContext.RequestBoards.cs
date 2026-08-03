using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _requestBoardFieldKinds =
        PersistedEnumTokens<RequestBoardFieldKind>.Values.ToArray();
    private static readonly string[] _requestBoardRefundPolicies =
        PersistedEnumTokens<RequestBoardRefundPolicy>.Values.ToArray();
    private static readonly string[] _requestSubmissionStatuses =
        PersistedEnumTokens<RequestSubmissionStatus>.Values.ToArray();
    private static readonly string[] _requestPointReservationStates =
        PersistedEnumTokens<RequestPointReservationState>.Values.ToArray();
    private static readonly string[] _requestBoardEventKinds =
        PersistedEnumTokens<RequestBoardEventKind>.Values.ToArray();

    private static void ConfigureRequestBoards(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<RequestBoard>(static b =>
        {
            _ = b.ToTable(
                "request_boards",
                static t =>
                    t.HasCheckConstraint(
                        "CK_request_boards_RefundPolicy",
                        KindIn("RefundPolicy", _requestBoardRefundPolicies)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Slug).HasMaxLength(48);
            _ = b.Property(static x => x.Title).HasMaxLength(100);
            _ = b.Property(static x => x.Description).HasMaxLength(1000);
            _ = b.Property(static x => x.PointCost).HasMaxLength(128);
            _ = b.Property(static x => x.RefundPolicy)
                .HasConversion(
                    static value => PersistedEnumTokens<RequestBoardRefundPolicy>.Format(value),
                    static value => PersistedEnumTokens<RequestBoardRefundPolicy>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.OrderingDescription).HasMaxLength(300);
            _ = b.HasIndex(static x => new { x.HostId, x.Slug }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Fields)
                .WithOne(static x => x.Board)
                .HasForeignKey(static x => x.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Submissions)
                .WithOne(static x => x.Board)
                .HasForeignKey(static x => x.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<RequestBoardField>(static b =>
        {
            _ = b.ToTable(
                "request_board_fields",
                static t =>
                    t.HasCheckConstraint(
                        "CK_request_board_fields_Kind",
                        KindIn("Kind", _requestBoardFieldKinds)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Key).HasMaxLength(48);
            _ = b.Property(static x => x.Label).HasMaxLength(100);
            _ = b.Property(static x => x.Kind)
                .HasConversion(
                    static value => PersistedEnumTokens<RequestBoardFieldKind>.Format(value),
                    static value => PersistedEnumTokens<RequestBoardFieldKind>.Parse(value)
                )
                .HasMaxLength(16);
            _ = b.Property(static x => x.ChoiceOptions).HasMaxLength(1000);
            _ = b.HasIndex(static x => new { x.BoardId, x.Key }).IsUnique();
            _ = b.HasIndex(static x => new { x.BoardId, x.Position }).IsUnique();
        });

        _ = modelBuilder.Entity<RequestSubmission>(static b =>
        {
            _ = b.ToTable(
                "request_submissions",
                static t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_request_submissions_Status",
                        KindIn("Status", _requestSubmissionStatuses)
                    );
                    _ = t.HasCheckConstraint(
                        "CK_request_submissions_PointReservationState",
                        KindIn("PointReservationState", _requestPointReservationStates)
                    );
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.SubmitterLogin).HasMaxLength(128);
            _ = b.Property(static x => x.Title).HasMaxLength(200);
            _ = b.Property(static x => x.NormalizedTitle).HasMaxLength(200);
            _ = b.Property(static x => x.NormalizedUrl).HasMaxLength(2048);
            _ = b.Property(static x => x.Status)
                .HasConversion(
                    static value => PersistedEnumTokens<RequestSubmissionStatus>.Format(value),
                    static value => PersistedEnumTokens<RequestSubmissionStatus>.Parse(value)
                )
                .HasMaxLength(16);
            _ = b.Property(static x => x.Category).HasMaxLength(64);
            _ = b.Property(static x => x.Tags).HasMaxLength(500);
            _ = b.Property(static x => x.PublicNote).HasMaxLength(500);
            _ = b.Property(static x => x.PrivateModeratorNote).HasMaxLength(1000);
            _ = b.Property(static x => x.PrivateRejectionReason).HasMaxLength(1000);
            _ = b.Property(static x => x.PointReservationState)
                .HasConversion(
                    static value => PersistedEnumTokens<RequestPointReservationState>.Format(value),
                    static value => PersistedEnumTokens<RequestPointReservationState>.Parse(value)
                )
                .HasMaxLength(16);
            _ = b.HasIndex(static x => new { x.HostId, x.OperationId }).IsUnique();
            _ = b.HasIndex(static x => new
            {
                x.BoardId,
                x.Status,
                x.Priority,
                x.QueuePosition,
            });
            _ = b.HasIndex(static x => new { x.BoardId, x.NormalizedTitle });
            _ = b.HasIndex(static x => new { x.BoardId, x.NormalizedUrl });
            _ = b.HasOne(static x => x.MergedIntoSubmission)
                .WithMany()
                .HasForeignKey(static x => x.MergedIntoSubmissionId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasMany(static x => x.Values)
                .WithOne(static x => x.Submission)
                .HasForeignKey(static x => x.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Votes)
                .WithOne(static x => x.Submission)
                .HasForeignKey(static x => x.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<RequestSubmissionValue>(static b =>
        {
            _ = b.ToTable("request_submission_values");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Value).HasMaxLength(2048);
            _ = b.HasIndex(static x => new { x.SubmissionId, x.FieldId }).IsUnique();
            _ = b.HasOne(static x => x.Field)
                .WithMany()
                .HasForeignKey(static x => x.FieldId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        _ = modelBuilder.Entity<RequestSubmissionVote>(static b =>
        {
            _ = b.ToTable("request_submission_votes");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.VoterLogin).HasMaxLength(128);
            _ = b.HasIndex(static x => new { x.SubmissionId, x.VoterLogin }).IsUnique();
        });

        _ = modelBuilder.Entity<RequestBoardDomainEvent>(static b =>
        {
            _ = b.ToTable(
                "request_board_events",
                static t =>
                    t.HasCheckConstraint(
                        "CK_request_board_events_Kind",
                        KindIn("Kind", _requestBoardEventKinds)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Kind)
                .HasConversion(
                    static value => PersistedEnumTokens<RequestBoardEventKind>.Format(value),
                    static value => PersistedEnumTokens<RequestBoardEventKind>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.PublicPayload).HasMaxLength(1024);
            _ = b.HasIndex(static x => new { x.HostId, x.Id });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne<RequestBoard>()
                .WithMany()
                .HasForeignKey(static x => x.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
