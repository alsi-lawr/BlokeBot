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
        modelBuilder.Entity<RequestBoard>(b =>
        {
            b.ToTable(
                "request_boards",
                t =>
                    t.HasCheckConstraint(
                        "CK_request_boards_RefundPolicy",
                        KindIn("RefundPolicy", _requestBoardRefundPolicies)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Slug).HasMaxLength(48);
            b.Property(x => x.Title).HasMaxLength(100);
            b.Property(x => x.Description).HasMaxLength(1000);
            b.Property(x => x.PointCost).HasMaxLength(128);
            b.Property(x => x.RefundPolicy)
                .HasConversion(
                    value => PersistedEnumTokens<RequestBoardRefundPolicy>.Format(value),
                    value => PersistedEnumTokens<RequestBoardRefundPolicy>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.OrderingDescription).HasMaxLength(300);
            b.HasIndex(x => new { x.HostId, x.Slug }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Fields)
                .WithOne(x => x.Board)
                .HasForeignKey(x => x.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Submissions)
                .WithOne(x => x.Board)
                .HasForeignKey(x => x.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RequestBoardField>(b =>
        {
            b.ToTable(
                "request_board_fields",
                t =>
                    t.HasCheckConstraint(
                        "CK_request_board_fields_Kind",
                        KindIn("Kind", _requestBoardFieldKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Key).HasMaxLength(48);
            b.Property(x => x.Label).HasMaxLength(100);
            b.Property(x => x.Kind)
                .HasConversion(
                    value => PersistedEnumTokens<RequestBoardFieldKind>.Format(value),
                    value => PersistedEnumTokens<RequestBoardFieldKind>.Parse(value)
                )
                .HasMaxLength(16);
            b.Property(x => x.ChoiceOptions).HasMaxLength(1000);
            b.HasIndex(x => new { x.BoardId, x.Key }).IsUnique();
            b.HasIndex(x => new { x.BoardId, x.Position }).IsUnique();
        });

        modelBuilder.Entity<RequestSubmission>(b =>
        {
            b.ToTable(
                "request_submissions",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_request_submissions_Status",
                        KindIn("Status", _requestSubmissionStatuses)
                    );
                    t.HasCheckConstraint(
                        "CK_request_submissions_PointReservationState",
                        KindIn("PointReservationState", _requestPointReservationStates)
                    );
                }
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.SubmitterLogin).HasMaxLength(128);
            b.Property(x => x.Title).HasMaxLength(200);
            b.Property(x => x.NormalizedTitle).HasMaxLength(200);
            b.Property(x => x.NormalizedUrl).HasMaxLength(2048);
            b.Property(x => x.Status)
                .HasConversion(
                    value => PersistedEnumTokens<RequestSubmissionStatus>.Format(value),
                    value => PersistedEnumTokens<RequestSubmissionStatus>.Parse(value)
                )
                .HasMaxLength(16);
            b.Property(x => x.Category).HasMaxLength(64);
            b.Property(x => x.Tags).HasMaxLength(500);
            b.Property(x => x.PublicNote).HasMaxLength(500);
            b.Property(x => x.PrivateModeratorNote).HasMaxLength(1000);
            b.Property(x => x.PrivateRejectionReason).HasMaxLength(1000);
            b.Property(x => x.PointReservationState)
                .HasConversion(
                    value => PersistedEnumTokens<RequestPointReservationState>.Format(value),
                    value => PersistedEnumTokens<RequestPointReservationState>.Parse(value)
                )
                .HasMaxLength(16);
            b.HasIndex(x => new { x.HostId, x.OperationId }).IsUnique();
            b.HasIndex(x => new
            {
                x.BoardId,
                x.Status,
                x.Priority,
                x.QueuePosition,
            });
            b.HasIndex(x => new { x.BoardId, x.NormalizedTitle });
            b.HasIndex(x => new { x.BoardId, x.NormalizedUrl });
            b.HasOne(x => x.MergedIntoSubmission)
                .WithMany()
                .HasForeignKey(x => x.MergedIntoSubmissionId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Values)
                .WithOne(x => x.Submission)
                .HasForeignKey(x => x.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Votes)
                .WithOne(x => x.Submission)
                .HasForeignKey(x => x.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RequestSubmissionValue>(b =>
        {
            b.ToTable("request_submission_values");
            b.HasKey(x => x.Id);
            b.Property(x => x.Value).HasMaxLength(2048);
            b.HasIndex(x => new { x.SubmissionId, x.FieldId }).IsUnique();
            b.HasOne(x => x.Field)
                .WithMany()
                .HasForeignKey(x => x.FieldId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RequestSubmissionVote>(b =>
        {
            b.ToTable("request_submission_votes");
            b.HasKey(x => x.Id);
            b.Property(x => x.VoterLogin).HasMaxLength(128);
            b.HasIndex(x => new { x.SubmissionId, x.VoterLogin }).IsUnique();
        });

        modelBuilder.Entity<RequestBoardDomainEvent>(b =>
        {
            b.ToTable(
                "request_board_events",
                t =>
                    t.HasCheckConstraint(
                        "CK_request_board_events_Kind",
                        KindIn("Kind", _requestBoardEventKinds)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind)
                .HasConversion(
                    value => PersistedEnumTokens<RequestBoardEventKind>.Format(value),
                    value => PersistedEnumTokens<RequestBoardEventKind>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.PublicPayload).HasMaxLength(1024);
            b.HasIndex(x => new { x.HostId, x.Id });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<RequestBoard>()
                .WithMany()
                .HasForeignKey(x => x.BoardId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
