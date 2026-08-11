using System.Text.Json;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Competitions;
using BlokeBot.Core.Features.MomentAttachments;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class MomentAttachmentServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task HostIsolationAndAuthority_BlockStaleSelectionAndCrossHostSources()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedAsync(database);
        var authority = new GrantingAuthority();
        var service = Service(database, authority);
        var destination = new MomentAttachmentDestination.Bounty(fixture.PublicBountyId);

        var staleSelection = await service.AttachAsync(
            Session(fixture.BetaHostId, "beta"),
            fixture.AlphaHostId,
            destination,
            fixture.AlphaMomentId,
            default
        );
        var crossHostSource = await service.AttachAsync(
            Session(fixture.AlphaHostId, "alpha"),
            fixture.AlphaHostId,
            destination,
            fixture.BetaMomentId,
            default
        );
        var deniedAuthority = await Service(database, new DenyingAuthority())
            .AttachAsync(
                Session(fixture.AlphaHostId, "alpha"),
                fixture.AlphaHostId,
                destination,
                fixture.AlphaMomentId,
                default
            );

        _ = staleSelection
            .ShouldBeOfType<MomentAttachmentMutationOutcome.Rejected>()
            .Reason.ShouldBeOfType<MomentAttachmentRejection.Unauthorized>();
        _ = crossHostSource
            .ShouldBeOfType<MomentAttachmentMutationOutcome.Rejected>()
            .Reason.ShouldBeOfType<MomentAttachmentRejection.MomentUnavailable>();
        _ = deniedAuthority
            .ShouldBeOfType<MomentAttachmentMutationOutcome.Rejected>()
            .Reason.ShouldBeOfType<MomentAttachmentRejection.Unauthorized>();
        authority.Calls.ShouldBe(1);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.MomentAttachments.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task AttachAndDetachRetries_AreIdempotentAndPublishOnlyRealChanges()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedAsync(database);
        var events = TestEventBus.Create<AppEventKind>();
        var eventCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.MomentAttachmentsChanged,
            ObserverIdentity.Named("MomentAttachmentServiceTests.Idempotency"),
            (_, _) =>
            {
                eventCount++;
                return ValueTask.CompletedTask;
            }
        );
        var service = Service(database, new GrantingAuthority(), events);
        var session = Session(fixture.AlphaHostId, "alpha");
        var destination = new MomentAttachmentDestination.Bounty(fixture.PublicBountyId);

        var concurrent = await Task.WhenAll(
            service.AttachAsync(
                session,
                fixture.AlphaHostId,
                destination,
                fixture.AlphaMomentId,
                default
            ),
            service.AttachAsync(
                session,
                fixture.AlphaHostId,
                destination,
                fixture.AlphaMomentId,
                default
            )
        );
        var retry = await service.AttachAsync(
            session,
            fixture.AlphaHostId,
            destination,
            fixture.AlphaMomentId,
            default
        );
        var removed = await service.DetachAsync(
            session,
            fixture.AlphaHostId,
            destination,
            fixture.AlphaMomentId,
            default
        );
        var removeRetry = await service.DetachAsync(
            session,
            fixture.AlphaHostId,
            destination,
            fixture.AlphaMomentId,
            default
        );

        concurrent.Select(WasIdempotent).Order().ShouldBe([false, true]);
        WasIdempotent(retry).ShouldBeTrue();
        WasIdempotent(removed).ShouldBeFalse();
        WasIdempotent(removeRetry).ShouldBeTrue();
        eventCount.ShouldBe(2);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.MomentAttachments.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task SourceLifecycle_SuppressesUnavailableReferencesAndNeverExposesOrphans()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedAsync(database);
        var service = Service(database, new GrantingAuthority());
        var destination = new MomentAttachmentDestination.Bounty(fixture.PublicBountyId);
        _ = Succeeded(
            await service.AttachAsync(
                Session(fixture.AlphaHostId, "alpha"),
                fixture.AlphaHostId,
                destination,
                fixture.AlphaMomentId,
                default
            )
        );

        await SetMomentStateAsync(database, fixture.AlphaMomentId, MomentCandidateState.Rejected);

        (
            await service.GetManagementAsync(fixture.AlphaHostId, destination, default)
        ).Attached.ShouldBeEmpty();
        (await service.GetPublicAsync("alpha", destination, default))!.Moments.ShouldBeEmpty();

        await SetMomentStateAsync(database, fixture.AlphaMomentId, MomentCandidateState.Approved);

        _ = (
            await service.GetManagementAsync(fixture.AlphaHostId, destination, default)
        ).Attached.ShouldHaveSingleItem();

        await using (var delete = await database.CreateDbContextAsync())
        {
            var source = await delete.MomentCandidates.SingleAsync(value =>
                value.PublicId == fixture.AlphaMomentId
            );
            _ = delete.MomentCandidates.Remove(source);
            _ = await delete.SaveChangesAsync();
        }

        (
            await service.GetManagementAsync(fixture.AlphaHostId, destination, default)
        ).Attached.ShouldBeEmpty();
        (await service.GetPublicAsync("alpha", destination, default))!.Moments.ShouldBeEmpty();

        await using (var delete = await database.CreateDbContextAsync())
        {
            var bounty = await delete.Bounties.SingleAsync(value =>
                value.PublicId == fixture.PublicBountyId
            );
            _ = delete.Bounties.Remove(bounty);
            _ = await delete.SaveChangesAsync();
        }

        (
            await service.GetManagementAsync(fixture.AlphaHostId, destination, default)
        ).Availability.ShouldBe(MomentAttachmentSectionAvailability.DestinationUnavailable);
        (await service.GetPublicAsync("alpha", destination, default)).ShouldBeNull();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.MomentAttachments.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task ConfirmedResultCorrection_RetainsStableAttachmentIdentity()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedAsync(database);
        var events = TestEventBus.Create<AppEventKind>();
        var attachments = Service(database, new GrantingAuthority(), events);
        var destination = new MomentAttachmentDestination.TournamentResult(fixture.ResultId);
        _ = Succeeded(
            await attachments.AttachAsync(
                Session(fixture.AlphaHostId, "alpha"),
                fixture.AlphaHostId,
                destination,
                fixture.AlphaMomentId,
                default
            )
        );
        var competitions = new CompetitionService(
            database,
            events,
            new UnusedAchievementGrantService(),
            [],
            new FixedTimeProvider(_now)
        );
        var competition = (await competitions.GetModeratorAsync(fixture.AlphaHostId, default))
            .Single()
            .Competition;
        var match = competition.Matches.Single(value => value.Id == fixture.ResultId);

        _ = (
            await competitions.ConfirmResultAsync(
                fixture.AlphaHostId,
                new(
                    Guid.NewGuid(),
                    competition.Id,
                    match.Id,
                    competition.Revision,
                    1,
                    3,
                    new("alpha-id", "alpha"),
                    "Corrected score"
                ),
                default
            )
        ).ShouldBeOfType<CompetitionOutcome.Succeeded>();

        var projection = await attachments.GetManagementAsync(
            fixture.AlphaHostId,
            destination,
            default
        );
        projection.Attached.ShouldHaveSingleItem().Id.ShouldBe(fixture.AlphaMomentId);
        projection.Destination!.Title.ShouldContain("1–3");
    }

    [Test]
    public async Task ParentGates_RetainValidLinksWithoutMutationVisibilityEventsOrReplay()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedAsync(database);
        var events = TestEventBus.Create<AppEventKind>();
        var eventCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.MomentAttachmentsChanged,
            ObserverIdentity.Named("MomentAttachmentServiceTests.Gates"),
            (_, _) =>
            {
                eventCount++;
                return ValueTask.CompletedTask;
            }
        );
        var service = Service(database, new GrantingAuthority(), events);
        var session = Session(fixture.AlphaHostId, "alpha");
        var destinations = new MomentAttachmentDestination[]
        {
            new MomentAttachmentDestination.Bounty(fixture.PublicBountyId),
            new MomentAttachmentDestination.Achievement(fixture.AchievementId),
            new MomentAttachmentDestination.TournamentResult(fixture.ResultId),
        };
        foreach (var destination in destinations)
        {
            _ = Succeeded(
                await service.AttachAsync(
                    session,
                    fixture.AlphaHostId,
                    destination,
                    fixture.AlphaMomentId,
                    default
                )
            );
        }
        eventCount.ShouldBe(3);

        await SetFeaturesAsync(
            database,
            fixture.AlphaHostId,
            _fixtureFeatures & ~HostFeatureFlags.Moments
        );

        foreach (var destination in destinations)
        {
            (
                await service.GetManagementAsync(fixture.AlphaHostId, destination, default)
            ).Availability.ShouldBe(MomentAttachmentSectionAvailability.ParentDisabled);
            (await service.GetPublicAsync("alpha", destination, default)).ShouldBeNull();
            _ = (
                await service.DetachAsync(
                    session,
                    fixture.AlphaHostId,
                    destination,
                    fixture.AlphaMomentId,
                    default
                )
            )
                .ShouldBeOfType<MomentAttachmentMutationOutcome.Rejected>()
                .Reason.ShouldBeOfType<MomentAttachmentRejection.ParentDisabled>();
        }
        eventCount.ShouldBe(3);

        await SetFeaturesAsync(database, fixture.AlphaHostId, _fixtureFeatures);

        foreach (var destination in destinations)
        {
            _ = (
                await service.GetManagementAsync(fixture.AlphaHostId, destination, default)
            ).Attached.ShouldHaveSingleItem();
        }
        eventCount.ShouldBe(3);

        foreach (
            var (destination, feature) in new[]
            {
                (destinations[0], HostFeatureFlags.Bounties),
                (destinations[1], HostFeatureFlags.CommunityProgression),
                (destinations[2], HostFeatureFlags.Competitions),
            }
        )
        {
            await SetFeaturesAsync(database, fixture.AlphaHostId, _fixtureFeatures & ~feature);
            (
                await service.GetManagementAsync(fixture.AlphaHostId, destination, default)
            ).Availability.ShouldBe(MomentAttachmentSectionAvailability.ParentDisabled);
            (await service.GetPublicAsync("alpha", destination, default)).ShouldBeNull();
            await SetFeaturesAsync(database, fixture.AlphaHostId, _fixtureFeatures);
        }
        eventCount.ShouldBe(3);
    }

    [Test]
    public async Task PublicProjection_RespectsDestinationPrivacyAndExcludesPrivateSourceMetadata()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedAsync(database);
        var service = Service(database, new GrantingAuthority());
        var session = Session(fixture.AlphaHostId, "alpha");
        var publicBounty = new MomentAttachmentDestination.Bounty(fixture.PublicBountyId);
        var privateBounty = new MomentAttachmentDestination.Bounty(fixture.PrivateBountyId);
        var achievement = new MomentAttachmentDestination.Achievement(fixture.AchievementId);
        var result = new MomentAttachmentDestination.TournamentResult(fixture.ResultId);
        foreach (
            var destination in new MomentAttachmentDestination[]
            {
                publicBounty,
                privateBounty,
                achievement,
                result,
            }
        )
        {
            _ = Succeeded(
                await service.AttachAsync(
                    session,
                    fixture.AlphaHostId,
                    destination,
                    fixture.AlphaMomentId,
                    default
                )
            );
        }

        var publicProjection = await service.GetPublicAsync("alpha", publicBounty, default);

        _ = publicProjection.ShouldNotBeNull().Moments.ShouldHaveSingleItem();
        var json = JsonSerializer.Serialize(publicProjection);
        json.ShouldNotContain(_fixturePrivateText);
        (await service.GetPublicAsync("alpha", privateBounty, default)).ShouldBeNull();

        await using (var suppress = await database.CreateDbContextAsync())
        {
            var season = await suppress.CommunitySeasons.SingleAsync(value =>
                value.PublicId == fixture.SeasonId
            );
            season.Visibility = CommunityVisibility.Hidden;
            var match = await suppress.CompetitionMatches.SingleAsync(value =>
                value.PublicId == fixture.ResultId.Value
            );
            match.Status = CompetitionMatchStatus.Pending;
            _ = await suppress.SaveChangesAsync();
        }

        (await service.GetPublicAsync("alpha", achievement, default)).ShouldBeNull();
        (await service.GetPublicAsync("alpha", result, default)).ShouldBeNull();
    }

    private const string _fixturePrivateText = "PRIVATE-MOMENT-MODERATOR-NOTE";
    private const HostFeatureFlags _fixtureFeatures =
        HostFeatureFlags.Moments
        | HostFeatureFlags.Bounties
        | HostFeatureFlags.CommunityProgression
        | HostFeatureFlags.Competitions;

    private static MomentAttachmentService Service(
        SqliteBlokeBotDbFactory database,
        IModeratorAuthorityService authority,
        EventBus<AppEventKind>? events = null
    ) =>
        new(
            database,
            authority,
            events ?? TestEventBus.Create<AppEventKind>(),
            new FixedTimeProvider(_now)
        );

    private static AuthenticatedSession Session(int hostId, string login)
    {
        var host = new BotHostChoice(hostId, login, login, AuthRole.Streamer);
        return new()
        {
            IsAuthenticated = true,
            UserId = $"{login}-id",
            Login = login,
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private static bool WasIdempotent(MomentAttachmentMutationOutcome outcome) =>
        outcome.ShouldBeOfType<MomentAttachmentMutationOutcome.Succeeded>().WasIdempotent;

    private static MomentAttachmentMutationOutcome.Succeeded Succeeded(
        MomentAttachmentMutationOutcome outcome
    ) => outcome.ShouldBeOfType<MomentAttachmentMutationOutcome.Succeeded>();

    private static async Task SetMomentStateAsync(
        SqliteBlokeBotDbFactory database,
        Guid momentId,
        MomentCandidateState state
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var moment = await db.MomentCandidates.SingleAsync(value => value.PublicId == momentId);
        moment.State = state;
        _ = await db.SaveChangesAsync();
    }

    private static async Task SetFeaturesAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        HostFeatureFlags features
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
        host.EnabledFeatures = features;
        _ = await db.SaveChangesAsync();
    }

    private static async Task<Fixture> SeedAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var alpha = Host("alpha");
        var beta = Host("beta");
        db.Hosts.AddRange(alpha, beta);
        _ = await db.SaveChangesAsync();

        var alphaMoment = Moment(alpha.Id, "Alpha public Moment");
        var betaMoment = Moment(beta.Id, "Beta public Moment");
        var publicBounty = Bounty(alpha.Id, BountyVisibility.Public, "Public bounty");
        var privateBounty = Bounty(alpha.Id, BountyVisibility.Private, "Private bounty");
        var season = new CommunitySeason
        {
            PublicId = Guid.NewGuid(),
            HostId = alpha.Id,
            CreationOperationId = Guid.NewGuid(),
            Name = "Public season",
            Description = "Public season description",
            ModeratorNotes = _fixturePrivateText,
            Status = CommunitySeasonStatus.Open,
            Visibility = CommunityVisibility.Public,
            StartsAtUtc = _now.AddDays(-1).UtcDateTime,
            EndsAtUtc = _now.AddDays(10).UtcDateTime,
            OpenedAtUtc = _now.AddDays(-1).UtcDateTime,
            Revision = 1,
            CreatedAtUtc = _now.AddDays(-2).UtcDateTime,
            UpdatedAtUtc = _now.UtcDateTime,
        };
        var achievement = Definition(alpha.Id, CommunityDefinitionKind.Achievement, "Achievement");
        var quest = Definition(alpha.Id, CommunityDefinitionKind.Quest, "Quest");
        season.Definitions.AddRange([achievement, quest]);

        var competition = Competition(alpha.Id);
        var entrantA = Entrant(alpha.Id, "One");
        var entrantB = Entrant(alpha.Id, "Two");
        competition.Entrants.AddRange([entrantA, entrantB]);
        var match = new CompetitionMatch
        {
            PublicId = Guid.NewGuid(),
            HostId = alpha.Id,
            Round = 1,
            Position = 0,
            EntrantA = entrantA,
            EntrantB = entrantB,
            ScoreA = 3,
            ScoreB = 1,
            WinnerEntrant = entrantA,
            Status = CompetitionMatchStatus.Confirmed,
            ConfirmedAtUtc = _now.UtcDateTime,
        };
        competition.Matches.Add(match);

        db.MomentCandidates.AddRange(alphaMoment, betaMoment);
        db.Bounties.AddRange(publicBounty, privateBounty);
        _ = db.CommunitySeasons.Add(season);
        _ = db.Competitions.Add(competition);
        _ = db.MomentModerationAudit.Add(
            new MomentModerationAudit
            {
                HostId = alpha.Id,
                CandidateId = alphaMoment.Id,
                Candidate = alphaMoment,
                Action = "Approved",
                ActorLogin = "alpha",
                PrivateText = _fixturePrivateText,
                OccurredAtUtc = _now.UtcDateTime,
            }
        );
        _ = await db.SaveChangesAsync();

        return new(
            alpha.Id,
            beta.Id,
            alphaMoment.PublicId,
            betaMoment.PublicId,
            publicBounty.PublicId,
            privateBounty.PublicId,
            season.PublicId,
            new(achievement.PublicId),
            new(match.PublicId)
        );
    }

    private static BotHost Host(string login) =>
        new()
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            EnabledFeatures = _fixtureFeatures,
            CreatedAtUtc = _now.UtcDateTime,
        };

    private static MomentCandidate Moment(int hostId, string title) =>
        new()
        {
            PublicId = Guid.NewGuid(),
            HostId = hostId,
            StreamIdentity = $"stream-{hostId}",
            State = MomentCandidateState.Approved,
            PublicTitle = title,
            PublicCategory = "Highlights",
            ProviderFailureReason = _fixturePrivateText,
            CapturedAtUtc = _now.AddMinutes(-5).UtcDateTime,
            LastCapturedAtUtc = _now.AddMinutes(-5).UtcDateTime,
            ApprovedAtUtc = _now.AddMinutes(-4).UtcDateTime,
        };

    private static Bounty Bounty(int hostId, BountyVisibility visibility, string title) =>
        new()
        {
            PublicId = Guid.NewGuid(),
            HostId = hostId,
            CreationOperationId = Guid.NewGuid(),
            CreationFingerprint = Guid.NewGuid().ToString("N"),
            Title = title,
            Description = "Public bounty description",
            Status = BountyStatus.Funding,
            Visibility = visibility,
            FailurePledgePolicy = BountyFailurePledgePolicy.Refund,
            RewardDistribution = BountyRewardDistribution.Equal,
            FundingTarget = "100",
            PledgedAmount = "0",
            CompletionReward = "0",
            ExpiresAtUtc = _now.AddDays(1).UtcDateTime,
            Revision = 1,
            CreatedAtUtc = _now.UtcDateTime,
            UpdatedAtUtc = _now.UtcDateTime,
        };

    private static CommunityDefinition Definition(
        int hostId,
        CommunityDefinitionKind kind,
        string name
    ) =>
        new()
        {
            PublicId = Guid.NewGuid(),
            HostId = hostId,
            Key = Guid.NewGuid().ToString("N"),
            Name = name,
            Kind = kind,
            Scope = CommunityProgressScope.Viewer,
            CompletionMode = CommunityCompletionMode.OneTime,
            EventRule = CommunityEventRuleKind.ExternalGrant,
            Increment = CommunityProgressIncrement.Occurrence,
            Target = 1,
            PointsReward = "0",
            ResetCadence = CommunityResetCadence.None,
            ResetLocalTime = "00:00",
            ScheduleRevision = 1,
            CreatedAtUtc = _now.UtcDateTime,
        };

    private static Competition Competition(int hostId) =>
        new()
        {
            PublicId = Guid.NewGuid(),
            HostId = hostId,
            CreationOperationId = Guid.NewGuid(),
            Name = "Public competition",
            Description = "Public competition description",
            Format = CompetitionFormat.RoundRobin,
            EntryKind = CompetitionEntryKind.Individual,
            Status = CompetitionStatus.Running,
            Seeding = CompetitionSeeding.Random,
            Tiebreak = CompetitionTiebreak.ScoreDifferenceThenScoreFor,
            Capacity = 2,
            TeamSize = 1,
            WinPoints = 3,
            DrawPoints = 1,
            LossPoints = 0,
            Seed = "seed",
            AlgorithmVersion = "algorithm",
            ReminderMessage = "Reminder",
            PrivateLobbyInformation = _fixturePrivateText,
            Revision = 1,
            CreatedAtUtc = _now.UtcDateTime,
            UpdatedAtUtc = _now.UtcDateTime,
            StartedAtUtc = _now.UtcDateTime,
        };

    private static CompetitionEntrant Entrant(int hostId, string name) =>
        new()
        {
            PublicId = Guid.NewGuid(),
            HostId = hostId,
            RegistrationOperationId = Guid.NewGuid(),
            Name = name,
            RegisteredAtUtc = _now.UtcDateTime,
        };

    private sealed record Fixture(
        int AlphaHostId,
        int BetaHostId,
        Guid AlphaMomentId,
        Guid BetaMomentId,
        Guid PublicBountyId,
        Guid PrivateBountyId,
        Guid SeasonId,
        CommunityDefinitionId AchievementId,
        CompetitionMatchId ResultId
    );

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class GrantingAuthority : IModeratorAuthorityService
    {
        public int Calls { get; private set; }

        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        )
        {
            Calls++;
            return Task.FromResult<ModeratorAuthorityOutcome>(
                new ModeratorAuthorityOutcome.Granted()
            );
        }
    }

    private sealed class DenyingAuthority : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        ) => Task.FromResult<ModeratorAuthorityOutcome>(new ModeratorAuthorityOutcome.Revoked());
    }

    private sealed class UnusedAchievementGrantService : ICommunityAchievementGrantService
    {
        public Task<CommunityExternalGrantOutcome> GrantAsync(
            CommunityExternalGrantRequest request,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Result correction must not grant achievements.");
    }
}
