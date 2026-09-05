using BlokeBot.Core;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Tests;

public sealed class RequestsIdentityMigrationTests
{
    [Test]
    public async Task SqliteUpgrade_RetainsUnclaimedRequestsAndDistinctReclaimedLoginVotes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"blokebot-requests-{Guid.NewGuid():N}");
        try
        {
            _ = Directory.CreateDirectory(directory);
            var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory, "requests.db"),
                Pooling = false,
            }.ToString();
            var options = new DbContextOptionsBuilder<BlokeBotDbContext>().UseSqlite(
                connectionString
            );
            _ = options.AddInterceptors(new WeeklyAnnouncementMigrationInterceptor());
            await VerifyUpgradeAsync(options.Options, "20260826174307_v0.13.0");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test, DatabaseCutoverIntegration]
    public async Task PostgreSqlUpgrade_RetainsUnclaimedRequestsAndDistinctReclaimedLoginVotes()
    {
        await using var postgres =
            await DatabaseCutoverIntegrationFixture.DisposablePostgreSql.StartAsync();
        var options = new DbContextOptionsBuilder<BlokeBotDbContext>().UseNpgsql(
            postgres.AdminConnectionString,
            provider => provider.MigrationsAssembly("BlokeBot.Persistence.PostgreSql")
        );
        await VerifyUpgradeAsync(options.Options, "20260901145930_20260901_v0_14_0_Baseline");
    }

    private static async Task VerifyUpgradeAsync(
        DbContextOptions<BlokeBotDbContext> options,
        string previousMigration
    )
    {
        var factory = new RequestsDatabaseFactory(options);
        var now = DateTime.UtcNow;
        int hostId;
        int boardId;
        var legacyOperation = Guid.NewGuid();
        await using (var prior = factory.CreateDbContext())
        {
            await prior.Database.MigrateAsync(previousMigration);
            var host = new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.All,
                CreatedAtUtc = now,
            };
            _ = prior.Hosts.Add(host);
            _ = await prior.SaveChangesAsync();
            hostId = host.Id;
            var board = new RequestBoard
            {
                HostId = hostId,
                Slug = "games",
                Title = "Games",
                IsOpen = true,
                VotingEnabled = true,
                PointCost = "25",
                RefundPolicy = RequestBoardRefundPolicy.AnyUnfulfilledClosure,
                SubmissionLimitPerUser = 3,
                VoteLimitPerUser = 5,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            _ = prior.RequestBoards.Add(board);
            _ = prior.PointBalances.Add(
                new PointBalance
                {
                    HostId = hostId,
                    Login = "shared",
                    Amount = "75",
                    UpdatedAtUtc = now,
                }
            );
            _ = await prior.SaveChangesAsync();
            boardId = board.Id;
            _ = await prior.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO request_submissions (
                    "HostId", "BoardId", "OperationId", "SubmitterLogin", "Title", "NormalizedTitle", "Status",
                    "Category", "Tags", "Priority", "QueuePosition", "VoteCount", "PublicNote", "PrivateModeratorNote",
                    "PrivateRejectionReason", "PointReservationState", "CreatedAtUtc", "UpdatedAtUtc"
                ) VALUES (
                    {hostId}, {boardId}, {legacyOperation}, 'shared', 'Legacy', 'legacy', 'Approved',
                    '', '', 0, 0, 1, '', 'legacy private note', '', 'Reserved', {now}, {now}
                )
                """
            );
            var id = await prior.RequestSubmissions.Select(row => row.Id).SingleAsync();
            _ = await prior.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO request_submission_votes ("SubmissionId", "VoterLogin", "CreatedAtUtc")
                VALUES ({id}, 'shared', {now})
                """
            );
        }
        await using (var upgrade = factory.CreateDbContext())
        {
            await upgrade.Database.MigrateAsync();
            var legacy = await upgrade.RequestSubmissions.SingleAsync();
            legacy.SubmitterTwitchUserId.ShouldBeNull();
            legacy.PointReservationState.ShouldBe(RequestPointReservationState.Reserved);
            (await upgrade.RequestSubmissionVotes.SingleAsync()).VoterTwitchUserId.ShouldBeNull();
        }
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddEventBus<AppEventKind>(
            ObserverBoundary.Named("requests-upgrade"),
            kind => ObserverEventIdentity.Named(kind.ToString())
        );
        await using var provider = services.BuildServiceProvider();
        var service = new RequestBoardService(
            factory,
            provider.GetRequiredService<EventBus<AppEventKind>>(),
            TimeProvider.System
        );
        var actor = Actor("current-owner-id", "shared");
        var renamed = Actor("current-owner-id", "renamed");
        var other = Actor("another-owner-id", "shared");
        var legacyView = (
            await service.GetPublicPageAsync("streamer", "games", CancellationToken.None)
        )!.Submissions.Single();
        var legacyId = legacyView.Id;
        (
            await service.GetSelfAsync(hostId, "games", actor, [legacyId], CancellationToken.None)
        )!.ActiveSubmissionCount.ShouldBe(0);
        _ = (
            await service.WithdrawAsync(hostId, legacyId, actor, CancellationToken.None)
        ).ShouldBeOfType<RequestBoardResult<PublicRequestSubmissionView>.Rejected>();
        _ = (
            await service.SubmitAsync(
                hostId,
                "games",
                Submit(actor, legacyOperation),
                CancellationToken.None
            )
        ).ShouldBeOfType<RequestBoardResult<PublicRequestSubmissionView>.Rejected>();
        var known = Succeeded(
            await service.SubmitAsync(
                hostId,
                "games",
                Submit(actor, Guid.NewGuid()),
                CancellationToken.None
            )
        );
        _ = Succeeded(
            await service.ModerateAsync(
                hostId,
                Moderate(known.Id, RequestSubmissionStatus.Approved),
                CancellationToken.None
            )
        );
        _ = Succeeded(await service.VoteAsync(hostId, legacyId, actor, CancellationToken.None));
        _ = Succeeded(await service.VoteAsync(hostId, legacyId, other, CancellationToken.None));
        _ = Succeeded(await service.VoteAsync(hostId, known.Id, renamed, CancellationToken.None));
        (await service.VoteAsync(hostId, legacyId, renamed, CancellationToken.None))
            .ShouldBeOfType<RequestBoardResult<PublicRequestSubmissionView>.Succeeded>()
            .WasIdempotent.ShouldBeTrue();
        _ = Succeeded(
            await service.MergeAsync(hostId, legacyId, known.Id, "", "", CancellationToken.None)
        );
        var merged = (
            await service.GetModeratorSubmissionAsync(hostId, known.Id, CancellationToken.None)
        )!;
        merged.Public.VoteCount.ShouldBe(3);
        (
            await service.GetSelfAsync(hostId, "games", renamed, [known.Id], CancellationToken.None)
        )!.VotesUsed.ShouldBe(1);
        await using (var privacy = factory.CreateDbContext())
        {
            var exported = await ViewerPrivacyService.ExportAsync(
                privacy,
                PrivacySubject.Create(actor.TwitchUserId, "renamed"),
                hostId,
                CancellationToken.None
            );
            exported.Sections["request-boards.submissions"].Count.ShouldBe(1);
            exported.Sections["request-boards.votes"].Count.ShouldBe(1);
            // A distinct known ID can erase its vote without selecting same-login owned submissions or legacy votes.
            var erased = await ViewerPrivacyService.EraseAsync(
                privacy,
                PrivacySubject.Create(other.TwitchUserId, "shared"),
                hostId,
                CancellationToken.None
            );
            erased.ChangedRows["request-boards.votes"].ShouldBe(1);
            erased.ChangedRows.GetValueOrDefault("request-boards.submissions").ShouldBe(0);
        }
        await using (var verify = factory.CreateDbContext())
        {
            (
                await verify.RequestSubmissions.SingleAsync(row => row.Id == known.Id)
            ).VoteCount.ShouldBe(2);
            (
                await verify.RequestSubmissionVotes.CountAsync(row => row.VoterTwitchUserId == null)
            ).ShouldBe(1);
            (
                await verify.RequestSubmissions.SingleAsync(row => row.Id == legacyId)
            ).PointReservationState.ShouldBe(RequestPointReservationState.Refunded);
            (await verify.PointBalances.SingleAsync(row => row.Login == "shared")).Amount.ShouldBe(
                "75"
            );
            var refund = await verify.PointLedgerEntries.SingleAsync(row =>
                row.RequestSubmissionId == legacyId
            );
            refund.Kind.ShouldBe(PointLedgerKind.RequestRefund);
            refund.Login.ShouldBe("shared");
            _ = verify.ViewerPassportLogins.Add(
                new ViewerPassportLogin
                {
                    HostId = hostId,
                    Login = "shared",
                    FirstSeenAtUtc = now,
                    LastSeenAtUtc = now,
                    Passport = new ViewerPassport
                    {
                        HostId = hostId,
                        TwitchUserId = "legacy-privacy-id",
                        Login = "shared",
                        DisplayName = "Legacy",
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now,
                    },
                }
            );
            _ = await verify.SaveChangesAsync();
        }
        await using (var legacyPrivacy = factory.CreateDbContext())
        {
            var subject = PrivacySubject.Create("legacy-privacy-id", "shared");
            var exported = await ViewerPrivacyService.ExportAsync(
                legacyPrivacy,
                subject,
                hostId,
                CancellationToken.None
            );
            exported.Sections["request-boards.submissions"].Count.ShouldBe(1);
            exported.Sections["request-boards.votes"].Count.ShouldBe(1);
            _ = await ViewerPrivacyService.EraseAsync(
                legacyPrivacy,
                subject,
                hostId,
                CancellationToken.None
            );
        }
        await using var final = factory.CreateDbContext();
        (await final.RequestSubmissions.SingleAsync()).SubmitterTwitchUserId.ShouldBe(
            actor.TwitchUserId
        );
        (await final.RequestSubmissionVotes.SingleAsync()).VoterTwitchUserId.ShouldBe(
            actor.TwitchUserId
        );
        (await final.RequestSubmissions.SingleAsync()).VoteCount.ShouldBe(1);
        _ = final.PointBalances.Add(
            new PointBalance
            {
                HostId = hostId,
                Login = "survivor",
                Amount = "50",
                UpdatedAtUtc = now,
            }
        );
        _ = await final.SaveChangesAsync();
        var survivingSource = Succeeded(
            await service.SubmitAsync(
                hostId,
                "games",
                Submit(Actor(other.TwitchUserId, "survivor"), Guid.NewGuid()),
                CancellationToken.None
            )
        );
        _ = Succeeded(
            await service.MergeAsync(
                hostId,
                survivingSource.Id,
                known.Id,
                "",
                "retained moderator note",
                CancellationToken.None
            )
        );
        await using (var privacy = factory.CreateDbContext())
        {
            _ = await ViewerPrivacyService.EraseAsync(
                privacy,
                PrivacySubject.Create(actor.TwitchUserId, "renamed"),
                hostId,
                CancellationToken.None
            );
        }
        await using var retained = factory.CreateDbContext();
        var survivor = await retained.RequestSubmissions.SingleAsync();
        survivor.Id.ShouldBe(survivingSource.Id);
        survivor.SubmitterTwitchUserId.ShouldBe(other.TwitchUserId);
        survivor.Status.ShouldBe(RequestSubmissionStatus.Merged);
        survivor.MergedIntoSubmissionId.ShouldBeNull();
        survivor.PrivateModeratorNote.ShouldBe("retained moderator note");
        survivor.VoteCount.ShouldBe(0);
        (await retained.RequestSubmissionVotes.CountAsync()).ShouldBe(0);
    }

    private static RequestActor Actor(string userId, string login) =>
        RequestActor.FromSession(
            new AuthenticatedSession
            {
                IsAuthenticated = true,
                UserId = userId,
                Login = login,
            }
        )!;

    private static SubmitRequestCommand Submit(RequestActor actor, Guid operationId) =>
        new(operationId, actor, "Known request", "", [], new Dictionary<string, string>());

    private static ModerateRequestCommand Moderate(long id, RequestSubmissionStatus status) =>
        new(id, status, "", "", "", 0, "", []);

    private static T Succeeded<T>(RequestBoardResult<T> result) =>
        result.Match(
            value => value.Value,
            rejected => throw new InvalidOperationException(rejected.Reason.Message)
        );

    private sealed class RequestsDatabaseFactory(DbContextOptions<BlokeBotDbContext> options)
        : IDbContextFactory<BlokeBotDbContext>
    {
        public BlokeBotDbContext CreateDbContext() => new(options);
    }
}
