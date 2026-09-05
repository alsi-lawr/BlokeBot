using BlokeBot.Core.Features.Moments;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class MomentHubServiceTests
{
    [Test]
    public async Task Votes_ExactIdWinsAcrossRenameAndKnownLoginCollisionIsNotClaimed()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var alpha = await SeedHostAsync(database, "alpha");
        var beta = await SeedHostAsync(database, "beta");
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(database, new FakeMomentProvider(database), clock);
        var moment = await CaptureAndApproveAsync(service, alpha, "stream", "contributor", clock);
        _ = Success(
            await service.VoteAsync(alpha, moment.PublicId, new("oldlogin", "owner-id"), default)
        );
        _ = Success(
            await service.VoteAsync(alpha, moment.PublicId, new("reclaimed", "other-id"), default)
        );
        Success(
            await service.VoteAsync(alpha, moment.PublicId, new("reclaimed", "owner-id"), default)
        )
            .WasIdempotent.ShouldBeTrue();
        var collision = await service.VoteAsync(
            alpha,
            moment.PublicId,
            new("reclaimed", "stranger-id"),
            default
        );
        collision
            .Match(_ => false, value => value.Reason is MomentRejection.Conflict)
            .ShouldBeTrue();
        (await service.VoteAsync(beta, moment.PublicId, new("reclaimed", "owner-id"), default))
            .Match(_ => false, value => value.Reason is MomentRejection.NotFound)
            .ShouldBeTrue();
        Success(await service.VoteAsync(alpha, moment.PublicId, new("reclaimed"), default))
            .WasIdempotent.ShouldBeTrue();
        _ = Success(await service.VoteAsync(alpha, moment.PublicId, new("legacy"), default));
        Success(
            await service.VoteAsync(alpha, moment.PublicId, new("legacy", "adopted-id"), default)
        )
            .WasIdempotent.ShouldBeTrue();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.MomentVotes.CountAsync()).ShouldBe(3);
        (
            await verify.MomentVotes.SingleAsync(value => value.NormalizedLogin == "oldlogin")
        ).TwitchUserId.ShouldBe("owner-id");
        (
            await verify.MomentVotes.SingleAsync(value => value.NormalizedLogin == "reclaimed")
        ).TwitchUserId.ShouldBe("other-id");
        (
            await verify.MomentVotes.SingleAsync(value => value.NormalizedLogin == "legacy")
        ).TwitchUserId.ShouldBe("adopted-id");
    }

    [Test]
    public async Task DisabledSwitch_RetainsSettingsBlocksProviderAndDoesNotReplayOnReenable()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var provider = new FakeMomentProvider(database);
        var service = CreateService(database, provider);
        _ = Success(
            await service.ConfigureAsync(
                hostId,
                new ConfigureMomentHubCommand(120, true, MomentRewardPolicy.AllContributors, "10"),
                CancellationToken.None
            )
        );
        int retainedEventCount;
        await using (var disable = await database.CreateDbContextAsync())
        {
            retainedEventCount = await disable.MomentEvents.CountAsync();
            var host = await disable.Hosts.SingleAsync();
            host.EnabledFeatures &= ~HostFeatureFlags.Moments;
            _ = await disable.SaveChangesAsync();
        }

        var rejected = await service.CaptureAsync(
            hostId,
            Capture("stream-live", "viewer"),
            CancellationToken.None
        );

        _ = rejected
            .Match(
                static _ => throw new InvalidOperationException("Expected rejection."),
                static value => value.Reason
            )
            .ShouldBeOfType<MomentRejection.FeatureDisabled>();
        provider.Calls.ShouldBe(0);
        (await service.GetModeratorPageAsync(hostId, CancellationToken.None)).ShouldBeNull();
        (await service.GetEventsAsync(hostId, 0, 100, CancellationToken.None)).ShouldBeEmpty();
        await using (var verifyDisabled = await database.CreateDbContextAsync())
        {
            (await verifyDisabled.MomentHubSettings.CountAsync()).ShouldBe(1);
            (await verifyDisabled.MomentCandidates.CountAsync()).ShouldBe(0);
            (await verifyDisabled.MomentEvents.CountAsync()).ShouldBe(retainedEventCount);
            var host = await verifyDisabled.Hosts.SingleAsync();
            host.EnabledFeatures |= HostFeatureFlags.Moments;
            _ = await verifyDisabled.SaveChangesAsync();
        }

        var restored = await service.GetModeratorPageAsync(hostId, CancellationToken.None);
        _ = restored.ShouldNotBeNull();
        restored.Settings.MergeWindowSeconds.ShouldBe(120);
        provider.Calls.ShouldBe(0);
        await using var verifyEnabled = await database.CreateDbContextAsync();
        (await verifyEnabled.MomentEvents.CountAsync()).ShouldBe(retainedEventCount);
    }

    [Test]
    public async Task NearbyCaptures_ClusterByExactHostAndStreamAndPersistEveryRequest()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var alpha = await SeedHostAsync(database, "alpha");
        var beta = await SeedHostAsync(database, "beta");
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero)
        );
        var provider = new FakeMomentProvider(database);
        var service = CreateService(database, provider, clock);

        var first = Success(
            await service.CaptureAsync(alpha, Capture("stream-1", "one"), CancellationToken.None)
        );
        clock.Advance(TimeSpan.FromSeconds(90));
        var clustered = Success(
            await service.CaptureAsync(alpha, Capture("stream-1", "two"), CancellationToken.None)
        );
        var otherStream = Success(
            await service.CaptureAsync(alpha, Capture("stream-2", "one"), CancellationToken.None)
        );
        var otherHost = Success(
            await service.CaptureAsync(beta, Capture("stream-1", "one"), CancellationToken.None)
        );
        clock.Advance(TimeSpan.FromSeconds(91));
        var outsideWindow = Success(
            await service.CaptureAsync(alpha, Capture("stream-1", "three"), CancellationToken.None)
        );

        clustered.WasIdempotent.ShouldBeTrue();
        clustered.Value.PublicId.ShouldBe(first.Value.PublicId);
        otherStream.Value.PublicId.ShouldNotBe(first.Value.PublicId);
        otherHost.Value.PublicId.ShouldNotBe(first.Value.PublicId);
        outsideWindow.Value.PublicId.ShouldNotBe(first.Value.PublicId);
        provider.Calls.ShouldBe(4);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.MomentCandidates.CountAsync()).ShouldBe(4);
        (await verify.MomentCaptureRequests.CountAsync()).ShouldBe(5);
        var firstCandidateId = await verify
            .MomentCandidates.Where(value => value.PublicId == first.Value.PublicId)
            .Select(value => value.Id)
            .SingleAsync();
        (
            await verify.MomentContributors.CountAsync(value =>
                value.CandidateId == firstCandidateId
            )
        ).ShouldBe(2);
        (await verify.MomentEvents.CountAsync()).ShouldBe(5);
    }

    [Test]
    public async Task ProviderPendingAndAmbiguous_RemainTypedUntilReconciledWithoutFallback()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        var provider = new FakeMomentProvider(
            database,
            outcomes:
            [
                FakeProviderState.Pending,
                FakeProviderState.ClipReady,
                FakeProviderState.Ambiguous,
            ]
        );
        var service = CreateService(database, provider);

        var pending = Success(
            await service.CaptureAsync(host, Capture("stream-1", "one"), CancellationToken.None)
        );
        pending.Value.State.ShouldBe(MomentCandidateState.ProviderPending);
        var resolved = Success(
            await service.CaptureAsync(host, Capture("stream-1", "two"), CancellationToken.None)
        );
        resolved.Value.State.ShouldBe(MomentCandidateState.ClipReady);
        var ambiguous = Success(
            await service.CaptureAsync(host, Capture("stream-2", "one"), CancellationToken.None)
        );
        ambiguous.Value.State.ShouldBe(MomentCandidateState.ProviderPending);
        await using var verify = await database.CreateDbContextAsync();
        var ambiguousRow = await verify.MomentCandidates.SingleAsync(value =>
            value.PublicId == ambiguous.Value.PublicId
        );
        ambiguousRow.TwitchStreamMarkerId.ShouldBeNull();
        ambiguousRow.ProviderFailureReason.ShouldContain("did not confirm");
    }

    [Test]
    public async Task ApprovalVotesRewardsAndEvents_AreAtomicIdempotentAndHostIsolated()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var alpha = await SeedHostAsync(database, "alpha");
        var beta = await SeedHostAsync(database, "beta");
        var service = CreateService(database, new FakeMomentProvider(database));
        _ = Success(
            await service.ConfigureAsync(
                alpha,
                new ConfigureMomentHubCommand(90, true, MomentRewardPolicy.AllContributors, "25"),
                CancellationToken.None
            )
        );
        var candidate = Success(
            await service.CaptureAsync(
                alpha,
                Capture("stream-1", "viewer", "twitch-1", "Great play"),
                CancellationToken.None
            )
        ).Value;
        _ = Success(
            await service.CaptureAsync(
                alpha,
                Capture("stream-1", "second", "twitch-2"),
                CancellationToken.None
            )
        );
        var command = new ModerateMomentCommand(
            candidate.PublicId,
            "Great play",
            "Gameplay",
            "streamer",
            "PRIVATE-MODERATOR-NOTE"
        );

        _ = Success(await service.ApproveAsync(alpha, command, CancellationToken.None));
        _ = Success(await service.ApproveAsync(alpha, command, CancellationToken.None));
        var firstVote = Success(
            await service.VoteAsync(
                alpha,
                candidate.PublicId,
                new MomentViewerIdentity("voter"),
                CancellationToken.None
            )
        );
        var reconciledVote = Success(
            await service.VoteAsync(
                alpha,
                candidate.PublicId,
                new MomentViewerIdentity("voter", "vote-id"),
                CancellationToken.None
            )
        );
        var wrongHost = await service.VoteAsync(
            beta,
            candidate.PublicId,
            new MomentViewerIdentity("voter", "vote-id"),
            CancellationToken.None
        );
        var recap = await service.GetWeeklyRecapAsync(
            "alpha",
            DateTime.UtcNow,
            CancellationToken.None
        );

        firstVote.WasIdempotent.ShouldBeFalse();
        reconciledVote.WasIdempotent.ShouldBeTrue();
        _ = wrongHost.ShouldBeOfType<MomentResult<MomentView>.Rejected>();
        _ = recap.ShouldNotBeNull();
        recap.ToString().ShouldNotContain("PRIVATE-MODERATOR-NOTE");
        await using var verify = await database.CreateDbContextAsync();
        (await verify.MomentVotes.CountAsync()).ShouldBe(1);
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(2);
        (
            await verify
                .PointLedgerEntries.Select(static value => value.OperationKey)
                .Distinct()
                .CountAsync()
        ).ShouldBe(2);
        (await verify.PointBalances.Select(static value => value.Amount).ToArrayAsync())
            .Sum(int.Parse)
            .ShouldBe(50);
        (
            await verify.MomentEvents.CountAsync(static value =>
                value.Kind == MomentEventKind.Approved
            )
        ).ShouldBe(1);
        (await service.GetEventsAsync(alpha, 0, 1000, CancellationToken.None)).Count.ShouldBe(3);
    }

    [Test]
    public async Task RejectionReasonIsPrivateAndMergePersistsActorTimeAndCombinedContributors()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero)
        );
        var service = CreateService(database, new FakeMomentProvider(database), clock);
        var source = Success(
            await service.CaptureAsync(host, Capture("stream-1", "one"), CancellationToken.None)
        ).Value;
        clock.Advance(TimeSpan.FromSeconds(91));
        var target = Success(
            await service.CaptureAsync(host, Capture("stream-1", "two"), CancellationToken.None)
        ).Value;
        var merged = Success(
            await service.MergeAsync(
                host,
                source.PublicId,
                target.PublicId,
                "moderator",
                "PRIVATE-MERGE-REASON",
                CancellationToken.None
            )
        );
        clock.Advance(TimeSpan.FromSeconds(91));
        var rejected = Success(
            await service.CaptureAsync(host, Capture("stream-1", "three"), CancellationToken.None)
        ).Value;
        _ = Success(
            await service.RejectAsync(
                host,
                new ModerateMomentCommand(
                    rejected.PublicId,
                    "",
                    "",
                    "moderator",
                    "PRIVATE-REJECTION-REASON"
                ),
                CancellationToken.None
            )
        );
        var publicPage = await service.GetStreamRecapAsync(
            "alpha",
            "stream-1",
            CancellationToken.None
        );
        var moderatorPage = await service.GetModeratorPageAsync(host, CancellationToken.None);

        merged.Value.Public.Contributors.Count.ShouldBe(2);
        _ = publicPage.ShouldNotBeNull();
        publicPage.ToString().ShouldNotContain("PRIVATE-");
        moderatorPage!
            .Candidates.Single(value => value.Public.PublicId == rejected.PublicId)
            .PrivateRejectionReason.ShouldBe("PRIVATE-REJECTION-REASON");
        await using var verify = await database.CreateDbContextAsync();
        var merge = await verify.MomentMerges.SingleAsync();
        merge.ActorLogin.ShouldBe("moderator");
        merge.PrivateText.ShouldBe("PRIVATE-MERGE-REASON");
        (
            await verify.MomentCandidates.SingleAsync(value => value.PublicId == source.PublicId)
        ).State.ShouldBe(MomentCandidateState.Merged);
    }

    [Test]
    public async Task WeeklyFinalization_UsesDeterministicOrderAndEmitsWinnerOnce()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero)
        );
        var service = CreateService(database, new FakeMomentProvider(database), clock);
        var weekStart = clock.GetUtcNow().UtcDateTime;
        var first = await CaptureAndApproveAsync(service, host, "stream-1", "one", clock);
        var second = await CaptureAndApproveAsync(service, host, "stream-1", "two", clock);
        _ = Success(
            await service.VoteAsync(
                host,
                first.PublicId,
                new MomentViewerIdentity("voter_one"),
                CancellationToken.None
            )
        );
        _ = Success(
            await service.VoteAsync(
                host,
                second.PublicId,
                new MomentViewerIdentity("voter_one"),
                CancellationToken.None
            )
        );
        _ = Success(
            await service.VoteAsync(
                host,
                second.PublicId,
                new MomentViewerIdentity("voter_two"),
                CancellationToken.None
            )
        );
        clock.Advance(TimeSpan.FromDays(7));

        var finalized = Success(
            await service.FinalizeWeekAsync(host, weekStart, CancellationToken.None)
        );
        var retry = Success(
            await service.FinalizeWeekAsync(host, weekStart, CancellationToken.None)
        );

        finalized.Value.PublicId.ShouldBe(second.PublicId);
        retry.WasIdempotent.ShouldBeTrue();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.MomentWeeklyFinalizations.CountAsync()).ShouldBe(1);
        (
            await verify.MomentEvents.CountAsync(static value =>
                value.Kind == MomentEventKind.Winner
            )
        ).ShouldBe(1);
    }

    [Test]
    public async Task IdentityReconciliation_IsBidirectionalAndRetainsTwitchIdAsPrimary()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero)
        );
        var service = CreateService(database, new FakeMomentProvider(database), clock);

        var idFirst = Success(
            await service.CaptureAsync(
                host,
                Capture("stream-1", "viewer", "viewer-id"),
                CancellationToken.None
            )
        ).Value;
        _ = Success(
            await service.CaptureAsync(host, Capture("stream-1", "viewer"), CancellationToken.None)
        );
        clock.Advance(TimeSpan.FromSeconds(91));
        var loginFirst = Success(
            await service.CaptureAsync(host, Capture("stream-1", "other"), CancellationToken.None)
        ).Value;
        _ = Success(
            await service.CaptureAsync(
                host,
                Capture("stream-1", "other", "other-id"),
                CancellationToken.None
            )
        );
        _ = Success(
            await service.ApproveAsync(
                host,
                new ModerateMomentCommand(
                    idFirst.PublicId,
                    "Identity test",
                    "Gameplay",
                    "moderator"
                ),
                CancellationToken.None
            )
        );
        var idVote = Success(
            await service.VoteAsync(
                host,
                idFirst.PublicId,
                new MomentViewerIdentity("voter", "voter-id"),
                CancellationToken.None
            )
        );
        var loginRetry = Success(
            await service.VoteAsync(
                host,
                idFirst.PublicId,
                new MomentViewerIdentity("voter"),
                CancellationToken.None
            )
        );

        idVote.WasIdempotent.ShouldBeFalse();
        loginRetry.WasIdempotent.ShouldBeTrue();
        await using var verify = await database.CreateDbContextAsync();
        var idFirstRow = await verify.MomentCandidates.SingleAsync(value =>
            value.PublicId == idFirst.PublicId
        );
        var loginFirstRow = await verify.MomentCandidates.SingleAsync(value =>
            value.PublicId == loginFirst.PublicId
        );
        var idFirstContributor = await verify.MomentContributors.SingleAsync(value =>
            value.CandidateId == idFirstRow.Id
        );
        idFirstContributor.IdentityKey.ShouldBe("id:viewer-id");
        idFirstContributor.TwitchUserId.ShouldBe("viewer-id");
        idFirstContributor.CaptureCount.ShouldBe(2);
        var loginFirstContributor = await verify.MomentContributors.SingleAsync(value =>
            value.CandidateId == loginFirstRow.Id
        );
        loginFirstContributor.IdentityKey.ShouldBe("id:other-id");
        loginFirstContributor.TwitchUserId.ShouldBe("other-id");
        loginFirstContributor.CaptureCount.ShouldBe(2);
        var vote = await verify.MomentVotes.SingleAsync();
        vote.IdentityKey.ShouldBe("id:voter-id");
        vote.TwitchUserId.ShouldBe("voter-id");
    }

    [Test]
    public async Task ConcurrentVoteApprovalAndFinalization_ReturnIdempotentSuccess()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero)
        );
        var provider = new FakeMomentProvider(database);
        var first = CreateService(database, provider, clock);
        var second = CreateService(database, provider, clock);
        _ = Success(
            await first.ConfigureAsync(
                host,
                new ConfigureMomentHubCommand(90, true, MomentRewardPolicy.AllContributors, "25"),
                CancellationToken.None
            )
        );
        var candidate = Success(
            await first.CaptureAsync(
                host,
                Capture("stream-live", "viewer", "viewer-id"),
                CancellationToken.None
            )
        ).Value;
        var command = new ModerateMomentCommand(
            candidate.PublicId,
            "Concurrent moment",
            "Gameplay",
            "moderator"
        );

        var approvals = await Task.WhenAll(
            first.ApproveAsync(host, command, CancellationToken.None),
            second.ApproveAsync(host, command, CancellationToken.None)
        );
        var approvalSuccesses = approvals.Select(Success).ToArray();
        approvalSuccesses.Count(static value => value.WasIdempotent).ShouldBe(1);

        var votes = await Task.WhenAll(
            first.VoteAsync(
                host,
                candidate.PublicId,
                new MomentViewerIdentity("voter", "voter-id"),
                CancellationToken.None
            ),
            second.VoteAsync(
                host,
                candidate.PublicId,
                new MomentViewerIdentity("voter"),
                CancellationToken.None
            )
        );
        var voteSuccesses = votes.Select(Success).ToArray();
        voteSuccesses.Count(static value => value.WasIdempotent).ShouldBe(1);

        var weekStart = clock.GetUtcNow().UtcDateTime;
        clock.Advance(TimeSpan.FromDays(7));
        var finalizations = await Task.WhenAll(
            first.FinalizeWeekAsync(host, weekStart, CancellationToken.None),
            second.FinalizeWeekAsync(host, weekStart, CancellationToken.None)
        );
        var finalizationSuccesses = finalizations.Select(Success).ToArray();
        finalizationSuccesses.Count(static value => value.WasIdempotent).ShouldBe(1);

        await using var verify = await database.CreateDbContextAsync();
        (await verify.MomentVotes.CountAsync()).ShouldBe(1);
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(1);
        (await verify.MomentWeeklyFinalizations.CountAsync()).ShouldBe(1);
        (
            await verify.MomentEvents.CountAsync(static value =>
                value.Kind == MomentEventKind.Approved
            )
        ).ShouldBe(1);
        (
            await verify.MomentEvents.CountAsync(static value =>
                value.Kind == MomentEventKind.Winner
            )
        ).ShouldBe(1);
        var operationKeys = await verify
            .MomentEvents.Where(static value => value.OperationKey != null)
            .Select(static value => value.OperationKey)
            .ToArrayAsync();
        operationKeys.Length.ShouldBe(2);
        operationKeys.Distinct(StringComparer.Ordinal).Count().ShouldBe(2);
    }

    [Test]
    public async Task Merge_RejectsContributorAndSuggestionUnionsBeyondDurableBounds()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "alpha");
        Guid contributorSource;
        Guid contributorTarget;
        Guid suggestionSource;
        Guid suggestionTarget;
        await using (var db = await database.CreateDbContextAsync())
        {
            var now = DateTime.UtcNow;
            var contributorCandidates = CandidatePair(host, "contributors", now);
            contributorSource = contributorCandidates.Source.PublicId;
            contributorTarget = contributorCandidates.Target.PublicId;
            contributorCandidates.Source.Contributors.Add(Contributor("source-only", now));
            contributorCandidates.Target.Contributors.AddRange(
                Enumerable
                    .Range(0, MomentLimits.MaximumContributorCount)
                    .Select(index => Contributor($"target_{index:D3}", now))
            );

            var suggestionCandidates = CandidatePair(host, "suggestions", now);
            suggestionSource = suggestionCandidates.Source.PublicId;
            suggestionTarget = suggestionCandidates.Target.PublicId;
            suggestionCandidates.Source.Suggestions.Add(Suggestion("source-only", now));
            suggestionCandidates.Target.Suggestions.AddRange(
                Enumerable
                    .Range(0, MomentLimits.MaximumSuggestionCount)
                    .Select(index => Suggestion($"target_{index:D3}", now))
            );
            db.MomentCandidates.AddRange(
                contributorCandidates.Source,
                contributorCandidates.Target,
                suggestionCandidates.Source,
                suggestionCandidates.Target
            );
            _ = await db.SaveChangesAsync();
        }
        var service = CreateService(database, new FakeMomentProvider(database));

        var contributorResult = await service.MergeAsync(
            host,
            contributorSource,
            contributorTarget,
            "moderator",
            "",
            CancellationToken.None
        );
        var suggestionResult = await service.MergeAsync(
            host,
            suggestionSource,
            suggestionTarget,
            "moderator",
            "",
            CancellationToken.None
        );

        contributorResult
            .ShouldBeOfType<MomentResult<ModeratorMomentView>.Rejected>()
            .Reason.Message.ShouldContain("contributor limit");
        suggestionResult
            .ShouldBeOfType<MomentResult<ModeratorMomentView>.Rejected>()
            .Reason.Message.ShouldContain("suggestion limit");
        await using var verify = await database.CreateDbContextAsync();
        (await verify.MomentMerges.CountAsync()).ShouldBe(0);
        (await verify.MomentContributors.CountAsync()).ShouldBe(501);
        (await verify.MomentSuggestions.CountAsync()).ShouldBe(101);
    }

    private static (MomentCandidate Source, MomentCandidate Target) CandidatePair(
        int hostId,
        string stream,
        DateTime now
    ) =>
        (
            new MomentCandidate
            {
                PublicId = Guid.NewGuid(),
                HostId = hostId,
                StreamIdentity = stream,
                State = MomentCandidateState.ClipReady,
                CapturedAtUtc = now,
                LastCapturedAtUtc = now,
            },
            new MomentCandidate
            {
                PublicId = Guid.NewGuid(),
                HostId = hostId,
                StreamIdentity = stream,
                State = MomentCandidateState.ClipReady,
                CapturedAtUtc = now,
                LastCapturedAtUtc = now,
            }
        );

    private static MomentContributor Contributor(string login, DateTime now) =>
        new()
        {
            IdentityKey = $"login:{login}",
            NormalizedLogin = login,
            DisplayName = login,
            CaptureCount = 1,
            FirstCapturedAtUtc = now,
            LastCapturedAtUtc = now,
        };

    private static MomentSuggestion Suggestion(string title, DateTime now) =>
        new()
        {
            IdentityKey = $"login:{title}",
            SuggestedTitle = title,
            CreatedAtUtc = now,
        };

    private static async Task<MomentView> CaptureAndApproveAsync(
        MomentHubService service,
        int hostId,
        string stream,
        string login,
        ManualTimeProvider clock
    )
    {
        var captured = Success(
            await service.CaptureAsync(hostId, Capture(stream, login), CancellationToken.None)
        ).Value;
        var approved = Success(
            await service.ApproveAsync(
                hostId,
                new ModerateMomentCommand(
                    captured.PublicId,
                    $"Moment {login}",
                    "Gameplay",
                    "moderator"
                ),
                CancellationToken.None
            )
        ).Value.Public;
        clock.Advance(TimeSpan.FromSeconds(91));
        return approved;
    }

    private static CaptureMomentCommand Capture(
        string stream,
        string login,
        string? userId = null,
        string title = ""
    ) => new(stream, new MomentViewerIdentity(login, userId, login), title);

    private static MomentHubService CreateService(
        SqliteBlokeBotDbFactory database,
        IMomentProviderOperations provider,
        TimeProvider? clock = null
    ) => new(database, provider, TestEventBus.Create<AppEventKind>(), clock ?? TimeProvider.System);

    private static MomentResult<T>.Succeeded Success<T>(MomentResult<T> result) =>
        result.Match(
            static value => value,
            static rejected => throw new InvalidOperationException(rejected.Reason.Message)
        );

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database, string login)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = login,
            DisplayName = login,
            TwitchUserId = $"{login}-id",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class FakeMomentProvider : IMomentProviderOperations
    {
        private readonly SqliteBlokeBotDbFactory _database;
        private readonly Queue<FakeProviderState> _outcomes;
        private int _calls;

        public FakeMomentProvider(
            SqliteBlokeBotDbFactory database,
            IEnumerable<FakeProviderState>? outcomes = null
        )
        {
            _database = database;
            _outcomes = new Queue<FakeProviderState>(outcomes ?? []);
        }

        public int Calls => _calls;

        public async Task<MomentProviderOutcome> CaptureAsync(
            int hostId,
            Guid publicId,
            bool markerFallbackEnabled,
            string description,
            CancellationToken ct
        )
        {
            _ = Interlocked.Increment(ref _calls);
            FakeProviderState state;
            lock (_outcomes)
            {
                state = _outcomes.TryDequeue(out var configured)
                    ? configured
                    : FakeProviderState.ClipReady;
            }
            await using var db = await _database.CreateDbContextAsync(ct);
            return state switch
            {
                FakeProviderState.ClipReady => await AddClipAsync(
                    db,
                    hostId,
                    publicId,
                    TwitchClipStatus.Available,
                    ct
                ),
                FakeProviderState.Pending => await AddClipAsync(
                    db,
                    hostId,
                    publicId,
                    TwitchClipStatus.Pending,
                    ct
                ),
                FakeProviderState.MarkerReady => await AddMarkerAsync(db, hostId, publicId, ct),
                FakeProviderState.Ambiguous => new MomentProviderOutcome.Ambiguous(
                    null,
                    null,
                    "Twitch did not confirm the request."
                ),
                _ => new MomentProviderOutcome.Failed(null, null, "Provider failed."),
            };
        }

        private static async Task<MomentProviderOutcome> AddClipAsync(
            BlokeBotDbContext db,
            int hostId,
            Guid publicId,
            TwitchClipStatus status,
            CancellationToken ct
        )
        {
            var key = $"fake:{publicId:N}:clip";
            var row = await db.TwitchClips.SingleOrDefaultAsync(
                value => value.HostId == hostId && value.IdempotencyKey == key,
                ct
            );
            if (row is null)
            {
                row = new TwitchClip
                {
                    HostId = hostId,
                    IdempotencyKey = key,
                    RequestedAtUtc = DateTime.UtcNow,
                };
                _ = db.TwitchClips.Add(row);
            }
            row.Status = status;
            row.ResolvedAtUtc = status == TwitchClipStatus.Available ? DateTime.UtcNow : null;
            row.FinalUrl =
                status == TwitchClipStatus.Available
                    ? $"https://clips.twitch.tv/{publicId:N}"
                    : null;
            _ = await db.SaveChangesAsync(ct);
            return status == TwitchClipStatus.Available
                ? new MomentProviderOutcome.ClipReady(row.Id)
                : new MomentProviderOutcome.Pending(row.Id);
        }

        private static async Task<MomentProviderOutcome> AddMarkerAsync(
            BlokeBotDbContext db,
            int hostId,
            Guid publicId,
            CancellationToken ct
        )
        {
            var row = new TwitchStreamMarker
            {
                HostId = hostId,
                IdempotencyKey = $"fake:{publicId:N}:marker",
                Status = TwitchStreamMarkerStatus.Succeeded,
                Description = "Moment",
                MarkerUrl = $"https://twitch.test/marker/{publicId:N}",
                CreatedAtUtc = DateTime.UtcNow,
                ResolvedAtUtc = DateTime.UtcNow,
            };
            _ = db.TwitchStreamMarkers.Add(row);
            _ = await db.SaveChangesAsync(ct);
            return new MomentProviderOutcome.MarkerReady(row.Id);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now += value;
    }

    private enum FakeProviderState
    {
        ClipReady,
        Pending,
        MarkerReady,
        Ambiguous,
        Failed,
    }
}
