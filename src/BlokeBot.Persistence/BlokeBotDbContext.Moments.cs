using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

public sealed partial class BlokeBotDbContext
{
    private static readonly string[] _momentCandidateStates =
        PersistedEnumTokens<MomentCandidateState>.Values.ToArray();
    private static readonly string[] _momentRewardPolicies =
        PersistedEnumTokens<MomentRewardPolicy>.Values.ToArray();
    private static readonly string[] _momentEventKinds =
        PersistedEnumTokens<MomentEventKind>.Values.ToArray();

    private static void ConfigureMoments(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<MomentHubSettings>(b =>
        {
            _ = b.ToTable(
                "moment_hub_settings",
                t =>
                {
                    _ = t.HasCheckConstraint(
                        "CK_moment_hub_settings_MergeWindowSeconds",
                        "MergeWindowSeconds BETWEEN 15 AND 300"
                    );
                    _ = t.HasCheckConstraint(
                        "CK_moment_hub_settings_RewardPolicy",
                        KindIn("RewardPolicy", _momentRewardPolicies)
                    );
                }
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.RewardPolicy)
                .HasConversion(
                    value => PersistedEnumTokens<MomentRewardPolicy>.Format(value),
                    value => PersistedEnumTokens<MomentRewardPolicy>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(x => x.RewardAmount).HasMaxLength(128);
            _ = b.HasIndex(x => x.HostId).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<MomentCandidate>(b =>
        {
            _ = b.ToTable(
                "moment_candidates",
                t =>
                    t.HasCheckConstraint(
                        "CK_moment_candidates_State",
                        KindIn("State", _momentCandidateStates)
                    )
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.PublicId).HasConversion<string>();
            _ = b.Property(x => x.StreamIdentity).HasMaxLength(128);
            _ = b.Property(x => x.State)
                .HasConversion(
                    value => PersistedEnumTokens<MomentCandidateState>.Format(value),
                    value => PersistedEnumTokens<MomentCandidateState>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(x => x.PublicTitle).HasMaxLength(200);
            _ = b.Property(x => x.PublicCategory).HasMaxLength(64);
            _ = b.Property(x => x.ProviderFailureReason).HasMaxLength(500);
            _ = b.Property(x => x.PrivateRejectionReason).HasMaxLength(1000);
            _ = b.HasIndex(x => x.PublicId).IsUnique();
            _ = b.HasIndex(x => new
            {
                x.HostId,
                x.StreamIdentity,
                x.LastCapturedAtUtc,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(x => x.TwitchClip)
                .WithMany()
                .HasForeignKey(x => x.TwitchClipId)
                .OnDelete(DeleteBehavior.SetNull);
            _ = b.HasOne(x => x.TwitchStreamMarker)
                .WithMany()
                .HasForeignKey(x => x.TwitchStreamMarkerId)
                .OnDelete(DeleteBehavior.SetNull);
            _ = b.HasOne(x => x.MergedIntoCandidate)
                .WithMany()
                .HasForeignKey(x => x.MergedIntoCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasMany(x => x.CaptureRequests)
                .WithOne(x => x.Candidate)
                .HasForeignKey(x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(x => x.Contributors)
                .WithOne(x => x.Candidate)
                .HasForeignKey(x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(x => x.Suggestions)
                .WithOne(x => x.Candidate)
                .HasForeignKey(x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(x => x.Votes)
                .WithOne(x => x.Candidate)
                .HasForeignKey(x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<MomentCaptureRequest>(b =>
        {
            _ = b.ToTable("moment_capture_requests");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.IdentityKey).HasMaxLength(160);
            _ = b.HasIndex(x => new
            {
                x.CandidateId,
                x.CapturedAtUtc,
                x.Id,
            });
        });

        _ = modelBuilder.Entity<MomentContributor>(b =>
        {
            _ = b.ToTable("moment_contributors");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.IdentityKey).HasMaxLength(160);
            _ = b.Property(x => x.TwitchUserId).HasMaxLength(128);
            _ = b.Property(x => x.NormalizedLogin).HasMaxLength(128);
            _ = b.Property(x => x.DisplayName).HasMaxLength(128);
            _ = b.HasIndex(x => new { x.CandidateId, x.IdentityKey }).IsUnique();
            _ = b.HasIndex(x => new { x.CandidateId, x.NormalizedLogin }).IsUnique();
            _ = b.HasIndex(x => new
            {
                x.CandidateId,
                x.FirstCapturedAtUtc,
                x.Id,
            });
        });

        _ = modelBuilder.Entity<MomentSuggestion>(b =>
        {
            _ = b.ToTable("moment_suggestions");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.IdentityKey).HasMaxLength(160);
            _ = b.Property(x => x.SuggestedTitle).HasMaxLength(200);
            _ = b.Property(x => x.SuggestedCategory).HasMaxLength(64);
            _ = b.HasIndex(x => new
            {
                x.CandidateId,
                x.CreatedAtUtc,
                x.Id,
            });
        });

        _ = modelBuilder.Entity<MomentVote>(b =>
        {
            _ = b.ToTable("moment_votes");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.IdentityKey).HasMaxLength(160);
            _ = b.Property(x => x.TwitchUserId).HasMaxLength(128);
            _ = b.Property(x => x.NormalizedLogin).HasMaxLength(128);
            _ = b.HasIndex(x => new { x.CandidateId, x.IdentityKey }).IsUnique();
            _ = b.HasIndex(x => new { x.CandidateId, x.NormalizedLogin }).IsUnique();
        });

        _ = modelBuilder.Entity<MomentModerationAudit>(b =>
        {
            _ = b.ToTable("moment_moderation_audit");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Action).HasMaxLength(32);
            _ = b.Property(x => x.ActorLogin).HasMaxLength(128);
            _ = b.Property(x => x.PrivateText).HasMaxLength(1000);
            _ = b.HasIndex(x => new
            {
                x.HostId,
                x.CandidateId,
                x.Id,
            });
            _ = b.HasOne(x => x.Candidate)
                .WithMany()
                .HasForeignKey(x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<MomentDomainEvent>(b =>
        {
            _ = b.ToTable(
                "moment_events",
                t =>
                    t.HasCheckConstraint("CK_moment_events_Kind", KindIn("Kind", _momentEventKinds))
            );
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.Kind)
                .HasConversion(
                    value => PersistedEnumTokens<MomentEventKind>.Format(value),
                    value => PersistedEnumTokens<MomentEventKind>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(x => x.StreamIdentity).HasMaxLength(128);
            _ = b.Property(x => x.PublicPayload).HasMaxLength(1024);
            _ = b.Property(x => x.OperationKey).HasMaxLength(200);
            _ = b.HasIndex(x => new { x.HostId, x.Id });
            _ = b.HasIndex(x => new { x.HostId, x.OperationKey })
                .IsUnique()
                .HasFilter("\"OperationKey\" IS NOT NULL");
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne<MomentCandidate>()
                .WithMany()
                .HasForeignKey(x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<MomentMerge>(b =>
        {
            _ = b.ToTable("moment_merges");
            _ = b.HasKey(x => x.Id);
            _ = b.Property(x => x.ActorLogin).HasMaxLength(128);
            _ = b.Property(x => x.PrivateText).HasMaxLength(1000);
            _ = b.HasIndex(x => x.SourceCandidateId).IsUnique();
            _ = b.HasIndex(x => new
            {
                x.HostId,
                x.TargetCandidateId,
                x.MergedAtUtc,
            });
            _ = b.HasOne(x => x.SourceCandidate)
                .WithMany()
                .HasForeignKey(x => x.SourceCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasOne(x => x.TargetCandidate)
                .WithMany()
                .HasForeignKey(x => x.TargetCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        _ = modelBuilder.Entity<MomentWeeklyFinalization>(b =>
        {
            _ = b.ToTable("moment_weekly_finalizations");
            _ = b.HasKey(x => x.Id);
            _ = b.HasIndex(x => new { x.HostId, x.WeekStartsAtUtc }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(x => x.WinningCandidate)
                .WithMany()
                .HasForeignKey(x => x.WinningCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
