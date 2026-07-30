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
        modelBuilder.Entity<MomentHubSettings>(b =>
        {
            b.ToTable(
                "moment_hub_settings",
                t =>
                {
                    t.HasCheckConstraint(
                        "CK_moment_hub_settings_MergeWindowSeconds",
                        "MergeWindowSeconds BETWEEN 15 AND 300"
                    );
                    t.HasCheckConstraint(
                        "CK_moment_hub_settings_RewardPolicy",
                        KindIn("RewardPolicy", _momentRewardPolicies)
                    );
                }
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.RewardPolicy)
                .HasConversion(
                    value => PersistedEnumTokens<MomentRewardPolicy>.Format(value),
                    value => PersistedEnumTokens<MomentRewardPolicy>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.RewardAmount).HasMaxLength(128);
            b.HasIndex(x => x.HostId).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MomentCandidate>(b =>
        {
            b.ToTable(
                "moment_candidates",
                t =>
                    t.HasCheckConstraint(
                        "CK_moment_candidates_State",
                        KindIn("State", _momentCandidateStates)
                    )
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.PublicId).HasConversion<string>();
            b.Property(x => x.StreamIdentity).HasMaxLength(128);
            b.Property(x => x.State)
                .HasConversion(
                    value => PersistedEnumTokens<MomentCandidateState>.Format(value),
                    value => PersistedEnumTokens<MomentCandidateState>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.PublicTitle).HasMaxLength(200);
            b.Property(x => x.PublicCategory).HasMaxLength(64);
            b.Property(x => x.ProviderFailureReason).HasMaxLength(500);
            b.Property(x => x.PrivateRejectionReason).HasMaxLength(1000);
            b.HasIndex(x => x.PublicId).IsUnique();
            b.HasIndex(x => new
            {
                x.HostId,
                x.StreamIdentity,
                x.LastCapturedAtUtc,
            });
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.TwitchClip)
                .WithMany()
                .HasForeignKey(x => x.TwitchClipId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.TwitchStreamMarker)
                .WithMany()
                .HasForeignKey(x => x.TwitchStreamMarkerId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.MergedIntoCandidate)
                .WithMany()
                .HasForeignKey(x => x.MergedIntoCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.CaptureRequests)
                .WithOne(x => x.Candidate)
                .HasForeignKey(x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Contributors)
                .WithOne(x => x.Candidate)
                .HasForeignKey(x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Suggestions)
                .WithOne(x => x.Candidate)
                .HasForeignKey(x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Votes)
                .WithOne(x => x.Candidate)
                .HasForeignKey(x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MomentCaptureRequest>(b =>
        {
            b.ToTable("moment_capture_requests");
            b.HasKey(x => x.Id);
            b.Property(x => x.IdentityKey).HasMaxLength(160);
            b.HasIndex(x => new
            {
                x.CandidateId,
                x.CapturedAtUtc,
                x.Id,
            });
        });

        modelBuilder.Entity<MomentContributor>(b =>
        {
            b.ToTable("moment_contributors");
            b.HasKey(x => x.Id);
            b.Property(x => x.IdentityKey).HasMaxLength(160);
            b.Property(x => x.TwitchUserId).HasMaxLength(128);
            b.Property(x => x.NormalizedLogin).HasMaxLength(128);
            b.Property(x => x.DisplayName).HasMaxLength(128);
            b.HasIndex(x => new { x.CandidateId, x.IdentityKey }).IsUnique();
            b.HasIndex(x => new { x.CandidateId, x.NormalizedLogin }).IsUnique();
            b.HasIndex(x => new
            {
                x.CandidateId,
                x.FirstCapturedAtUtc,
                x.Id,
            });
        });

        modelBuilder.Entity<MomentSuggestion>(b =>
        {
            b.ToTable("moment_suggestions");
            b.HasKey(x => x.Id);
            b.Property(x => x.IdentityKey).HasMaxLength(160);
            b.Property(x => x.SuggestedTitle).HasMaxLength(200);
            b.Property(x => x.SuggestedCategory).HasMaxLength(64);
            b.HasIndex(x => new
            {
                x.CandidateId,
                x.CreatedAtUtc,
                x.Id,
            });
        });

        modelBuilder.Entity<MomentVote>(b =>
        {
            b.ToTable("moment_votes");
            b.HasKey(x => x.Id);
            b.Property(x => x.IdentityKey).HasMaxLength(160);
            b.Property(x => x.TwitchUserId).HasMaxLength(128);
            b.Property(x => x.NormalizedLogin).HasMaxLength(128);
            b.HasIndex(x => new { x.CandidateId, x.IdentityKey }).IsUnique();
            b.HasIndex(x => new { x.CandidateId, x.NormalizedLogin }).IsUnique();
        });

        modelBuilder.Entity<MomentModerationAudit>(b =>
        {
            b.ToTable("moment_moderation_audit");
            b.HasKey(x => x.Id);
            b.Property(x => x.Action).HasMaxLength(32);
            b.Property(x => x.ActorLogin).HasMaxLength(128);
            b.Property(x => x.PrivateText).HasMaxLength(1000);
            b.HasIndex(x => new
            {
                x.HostId,
                x.CandidateId,
                x.Id,
            });
            b.HasOne(x => x.Candidate)
                .WithMany()
                .HasForeignKey(x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MomentDomainEvent>(b =>
        {
            b.ToTable(
                "moment_events",
                t =>
                    t.HasCheckConstraint("CK_moment_events_Kind", KindIn("Kind", _momentEventKinds))
            );
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind)
                .HasConversion(
                    value => PersistedEnumTokens<MomentEventKind>.Format(value),
                    value => PersistedEnumTokens<MomentEventKind>.Parse(value)
                )
                .HasMaxLength(32);
            b.Property(x => x.StreamIdentity).HasMaxLength(128);
            b.Property(x => x.PublicPayload).HasMaxLength(1024);
            b.Property(x => x.OperationKey).HasMaxLength(200);
            b.HasIndex(x => new { x.HostId, x.Id });
            b.HasIndex(x => new { x.HostId, x.OperationKey })
                .IsUnique()
                .HasFilter("\"OperationKey\" IS NOT NULL");
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<MomentCandidate>()
                .WithMany()
                .HasForeignKey(x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MomentMerge>(b =>
        {
            b.ToTable("moment_merges");
            b.HasKey(x => x.Id);
            b.Property(x => x.ActorLogin).HasMaxLength(128);
            b.Property(x => x.PrivateText).HasMaxLength(1000);
            b.HasIndex(x => x.SourceCandidateId).IsUnique();
            b.HasIndex(x => new
            {
                x.HostId,
                x.TargetCandidateId,
                x.MergedAtUtc,
            });
            b.HasOne(x => x.SourceCandidate)
                .WithMany()
                .HasForeignKey(x => x.SourceCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.TargetCandidate)
                .WithMany()
                .HasForeignKey(x => x.TargetCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MomentWeeklyFinalization>(b =>
        {
            b.ToTable("moment_weekly_finalizations");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.HostId, x.WeekStartsAtUtc }).IsUnique();
            b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.WinningCandidate)
                .WithMany()
                .HasForeignKey(x => x.WinningCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
