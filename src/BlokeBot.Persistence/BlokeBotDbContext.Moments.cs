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
                        ProviderSql(
                            modelBuilder,
                            "MergeWindowSeconds BETWEEN 15 AND 300",
                            "\"MergeWindowSeconds\" BETWEEN 15 AND 300"
                        )
                    );
                    _ = t.HasCheckConstraint(
                        "CK_moment_hub_settings_RewardPolicy",
                        KindIn(modelBuilder, "RewardPolicy", _momentRewardPolicies)
                    );
                }
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.RewardPolicy)
                .HasConversion(
                    static value => PersistedEnumTokens<MomentRewardPolicy>.Format(value),
                    static value => PersistedEnumTokens<MomentRewardPolicy>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.RewardAmount).HasMaxLength(128);
            _ = b.HasIndex(static x => x.HostId).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<MomentCandidate>(b =>
        {
            _ = b.ToTable(
                "moment_candidates",
                t =>
                    t.HasCheckConstraint(
                        "CK_moment_candidates_State",
                        KindIn(modelBuilder, "State", _momentCandidateStates)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.HasAlternateKey(static x => new { x.HostId, x.Id });
            _ = b.Property(static x => x.PublicId).HasConversion<string>();
            _ = b.Property(static x => x.StreamIdentity).HasMaxLength(128);
            _ = b.Property(static x => x.State)
                .HasConversion(
                    static value => PersistedEnumTokens<MomentCandidateState>.Format(value),
                    static value => PersistedEnumTokens<MomentCandidateState>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.PublicTitle).HasMaxLength(200);
            _ = b.Property(static x => x.PublicCategory).HasMaxLength(64);
            _ = b.Property(static x => x.ProviderFailureReason).HasMaxLength(500);
            _ = b.Property(static x => x.PrivateRejectionReason).HasMaxLength(1000);
            _ = b.HasIndex(static x => x.PublicId).IsUnique();
            _ = b.HasIndex(static x => new
            {
                x.HostId,
                x.StreamIdentity,
                x.LastCapturedAtUtc,
            });
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(static x => x.TwitchClip)
                .WithMany()
                .HasForeignKey(static x => x.TwitchClipId)
                .OnDelete(DeleteBehavior.SetNull);
            _ = b.HasOne(static x => x.TwitchStreamMarker)
                .WithMany()
                .HasForeignKey(static x => x.TwitchStreamMarkerId)
                .OnDelete(DeleteBehavior.SetNull);
            _ = b.HasOne(static x => x.MergedIntoCandidate)
                .WithMany()
                .HasForeignKey(static x => x.MergedIntoCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasMany(static x => x.CaptureRequests)
                .WithOne(static x => x.Candidate)
                .HasForeignKey(static x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Contributors)
                .WithOne(static x => x.Candidate)
                .HasForeignKey(static x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Suggestions)
                .WithOne(static x => x.Candidate)
                .HasForeignKey(static x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasMany(static x => x.Votes)
                .WithOne(static x => x.Candidate)
                .HasForeignKey(static x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<MomentAttachment>(b =>
        {
            _ = b.ToTable(
                "moment_attachments",
                table =>
                    table.HasCheckConstraint(
                        "CK_moment_attachments_OneDestination",
                        ProviderSql(
                            modelBuilder,
                            "(BountyId IS NOT NULL AND CommunityDefinitionId IS NULL AND CompetitionMatchId IS NULL) OR ",
                            "(\"BountyId\" IS NOT NULL AND \"CommunityDefinitionId\" IS NULL AND \"CompetitionMatchId\" IS NULL) OR "
                        )
                            + ProviderSql(
                                modelBuilder,
                                "(BountyId IS NULL AND CommunityDefinitionId IS NOT NULL AND CompetitionMatchId IS NULL) OR ",
                                "(\"BountyId\" IS NULL AND \"CommunityDefinitionId\" IS NOT NULL AND \"CompetitionMatchId\" IS NULL) OR "
                            )
                            + ProviderSql(
                                modelBuilder,
                                "(BountyId IS NULL AND CommunityDefinitionId IS NULL AND CompetitionMatchId IS NOT NULL)",
                                "(\"BountyId\" IS NULL AND \"CommunityDefinitionId\" IS NULL AND \"CompetitionMatchId\" IS NOT NULL)"
                            )
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.HasIndex(static x => new
                {
                    x.HostId,
                    x.BountyId,
                    x.MomentCandidateId,
                })
                .IsUnique()
                .HasFilter("\"BountyId\" IS NOT NULL");
            _ = b.HasIndex(static x => new
                {
                    x.HostId,
                    x.CommunityDefinitionId,
                    x.MomentCandidateId,
                })
                .IsUnique()
                .HasFilter("\"CommunityDefinitionId\" IS NOT NULL");
            _ = b.HasIndex(static x => new
                {
                    x.HostId,
                    x.CompetitionMatchId,
                    x.MomentCandidateId,
                })
                .IsUnique()
                .HasFilter("\"CompetitionMatchId\" IS NOT NULL");
            _ = b.HasOne(static x => x.MomentCandidate)
                .WithMany()
                .HasForeignKey(static x => new { x.HostId, x.MomentCandidateId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(static x => x.Bounty)
                .WithMany()
                .HasForeignKey(static x => new { x.HostId, x.BountyId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(static x => x.CommunityDefinition)
                .WithMany()
                .HasForeignKey(static x => new { x.HostId, x.CommunityDefinitionId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(static x => x.CompetitionMatch)
                .WithMany()
                .HasForeignKey(static x => new { x.HostId, x.CompetitionMatchId })
                .HasPrincipalKey(static x => new { x.HostId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<MomentCaptureRequest>(b =>
        {
            _ = b.ToTable("moment_capture_requests");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.IdentityKey).HasMaxLength(160);
            _ = b.HasIndex(static x => new
            {
                x.CandidateId,
                x.CapturedAtUtc,
                x.Id,
            });
        });

        _ = modelBuilder.Entity<MomentContributor>(b =>
        {
            _ = b.ToTable("moment_contributors");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.IdentityKey).HasMaxLength(160);
            _ = b.Property(static x => x.TwitchUserId).HasMaxLength(128);
            _ = b.Property(static x => x.NormalizedLogin).HasMaxLength(128);
            _ = b.Property(static x => x.DisplayName).HasMaxLength(128);
            _ = b.HasIndex(static x => new { x.CandidateId, x.IdentityKey }).IsUnique();
            _ = b.HasIndex(static x => new { x.CandidateId, x.NormalizedLogin }).IsUnique();
            _ = b.HasIndex(static x => new
            {
                x.CandidateId,
                x.FirstCapturedAtUtc,
                x.Id,
            });
        });

        _ = modelBuilder.Entity<MomentSuggestion>(b =>
        {
            _ = b.ToTable("moment_suggestions");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.IdentityKey).HasMaxLength(160);
            _ = b.Property(static x => x.SuggestedTitle).HasMaxLength(200);
            _ = b.Property(static x => x.SuggestedCategory).HasMaxLength(64);
            _ = b.HasIndex(static x => new
            {
                x.CandidateId,
                x.CreatedAtUtc,
                x.Id,
            });
        });

        _ = modelBuilder.Entity<MomentVote>(b =>
        {
            _ = b.ToTable("moment_votes");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.IdentityKey).HasMaxLength(160);
            _ = b.Property(static x => x.TwitchUserId).HasMaxLength(128);
            _ = b.Property(static x => x.NormalizedLogin).HasMaxLength(128);
            _ = b.HasIndex(static x => new { x.CandidateId, x.IdentityKey }).IsUnique();
            _ = b.HasIndex(static x => new { x.CandidateId, x.NormalizedLogin }).IsUnique();
        });

        _ = modelBuilder.Entity<MomentModerationAudit>(b =>
        {
            _ = b.ToTable("moment_moderation_audit");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Action).HasMaxLength(32);
            _ = b.Property(static x => x.ActorLogin).HasMaxLength(128);
            _ = b.Property(static x => x.PrivateText).HasMaxLength(1000);
            _ = b.HasIndex(static x => new
            {
                x.HostId,
                x.CandidateId,
                x.Id,
            });
            _ = b.HasOne(static x => x.Candidate)
                .WithMany()
                .HasForeignKey(static x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<MomentDomainEvent>(b =>
        {
            _ = b.ToTable(
                "moment_events",
                t =>
                    t.HasCheckConstraint(
                        "CK_moment_events_Kind",
                        KindIn(modelBuilder, "Kind", _momentEventKinds)
                    )
            );
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.Kind)
                .HasConversion(
                    static value => PersistedEnumTokens<MomentEventKind>.Format(value),
                    static value => PersistedEnumTokens<MomentEventKind>.Parse(value)
                )
                .HasMaxLength(32);
            _ = b.Property(static x => x.StreamIdentity).HasMaxLength(128);
            _ = b.Property(static x => x.PublicPayload).HasMaxLength(1024);
            _ = b.Property(static x => x.OperationKey).HasMaxLength(200);
            _ = b.HasIndex(static x => new { x.HostId, x.Id });
            _ = b.HasIndex(static x => new { x.HostId, x.OperationKey })
                .IsUnique()
                .HasFilter("\"OperationKey\" IS NOT NULL");
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne<MomentCandidate>()
                .WithMany()
                .HasForeignKey(static x => x.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<MomentMerge>(b =>
        {
            _ = b.ToTable("moment_merges");
            _ = b.HasKey(static x => x.Id);
            _ = b.Property(static x => x.ActorLogin).HasMaxLength(128);
            _ = b.Property(static x => x.PrivateText).HasMaxLength(1000);
            _ = b.HasIndex(static x => x.SourceCandidateId).IsUnique();
            _ = b.HasIndex(static x => new
            {
                x.HostId,
                x.TargetCandidateId,
                x.MergedAtUtc,
            });
            _ = b.HasOne(static x => x.SourceCandidate)
                .WithMany()
                .HasForeignKey(static x => x.SourceCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = b.HasOne(static x => x.TargetCandidate)
                .WithMany()
                .HasForeignKey(static x => x.TargetCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        _ = modelBuilder.Entity<MomentWeeklyFinalization>(b =>
        {
            _ = b.ToTable("moment_weekly_finalizations");
            _ = b.HasKey(static x => x.Id);
            _ = b.HasIndex(static x => new { x.HostId, x.WeekStartsAtUtc }).IsUnique();
            _ = b.HasOne<BotHost>()
                .WithMany()
                .HasForeignKey(static x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = b.HasOne(static x => x.WinningCandidate)
                .WithMany()
                .HasForeignKey(static x => x.WinningCandidateId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
