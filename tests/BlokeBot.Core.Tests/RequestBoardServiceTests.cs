using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class RequestBoardServiceTests
{
    [Test]
    public async Task DisabledSwitch_RetainsBoardsBlocksEffectsAndDoesNotReplayOnReenable()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database);
        _ = Success(await service.ConfigureAsync(hostId, Board(), CancellationToken.None));
        int retainedEventCount;
        await using (var disable = await database.CreateDbContextAsync())
        {
            retainedEventCount = await disable.RequestBoardEvents.CountAsync();
            var host = await disable.Hosts.SingleAsync();
            host.EnabledFeatures &= ~HostFeatureFlags.RequestBoards;
            await disable.SaveChangesAsync();
        }

        var rejected = Rejection(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(Guid.NewGuid(), "viewer", "Suppressed request"),
                CancellationToken.None
            )
        );

        rejected.ShouldBeOfType<RequestBoardRejection.FeatureDisabled>();
        (await service.GetPublicPageAsync("alpha", "games", CancellationToken.None)).ShouldBeNull();
        (await service.GetEventsAsync(hostId, 0, 100, CancellationToken.None)).ShouldBeEmpty();
        await using (var verifyDisabled = await database.CreateDbContextAsync())
        {
            (await verifyDisabled.RequestBoards.CountAsync()).ShouldBe(1);
            (await verifyDisabled.RequestSubmissions.CountAsync()).ShouldBe(0);
            (await verifyDisabled.RequestBoardEvents.CountAsync()).ShouldBe(retainedEventCount);
            var host = await verifyDisabled.Hosts.SingleAsync();
            host.EnabledFeatures |= HostFeatureFlags.RequestBoards;
            await verifyDisabled.SaveChangesAsync();
        }

        (
            await service.GetPublicPageAsync("alpha", "games", CancellationToken.None)
        ).ShouldNotBeNull();
        await using var verifyEnabled = await database.CreateDbContextAsync();
        (await verifyEnabled.RequestBoardEvents.CountAsync()).ShouldBe(retainedEventCount);
    }

    [Test]
    public async Task BoardConfiguration_ReplacesUnusedFieldShapeButPreservesSubmittedShape()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database);
        _ = Success(await service.ConfigureAsync(hostId, Board(), CancellationToken.None));
        var changed = Board() with
        {
            Fields =
            [
                new RequestBoardFieldCommand(
                    "prompt",
                    "Prompt",
                    RequestBoardFieldKind.Text,
                    true,
                    200
                ),
            ],
        };

        var configured = Success(
            await service.ConfigureAsync(hostId, changed, CancellationToken.None)
        );
        _ = Success(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(
                    Guid.NewGuid(),
                    "viewer",
                    "First request",
                    new Dictionary<string, string> { ["prompt"] = "Please review." }
                ),
                CancellationToken.None
            )
        );
        var rejected = Rejection(
            await service.ConfigureAsync(hostId, Board(), CancellationToken.None)
        );

        configured.Value.Fields.Select(value => value.Key).ShouldBe(["prompt"]);
        rejected.ShouldBeOfType<RequestBoardRejection.Conflict>();
        (await service.GetPublicPageAsync("alpha", "games", CancellationToken.None))!
            .Board.Fields.Select(value => value.Key)
            .ShouldBe(["prompt"]);
    }

    [Test]
    public async Task ConcurrentSubmissionAndWithdrawalRetries_ConvergeOnOneAccountingResult()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        await SeedBalanceAsync(database, hostId, "viewer", "100");
        var service = CreateService(database);
        var concurrentService = CreateService(database);
        _ = Success(
            await service.ConfigureAsync(hostId, Board(pointCost: "25"), CancellationToken.None)
        );
        var operationId = Guid.NewGuid();
        var command = Submission(operationId, "viewer", "A useful game");

        var submissions = (
            await Task.WhenAll(
                service.SubmitAsync(hostId, "games", command, CancellationToken.None),
                concurrentService.SubmitAsync(hostId, "games", command, CancellationToken.None)
            )
        )
            .Select(Success)
            .ToArray();
        var first = submissions.Single(value => !value.WasIdempotent);
        var retry = submissions.Single(value => value.WasIdempotent);
        var withdrawal = Success(
            await service.WithdrawAsync(hostId, first.Value.Id, "viewer", CancellationToken.None)
        );
        var withdrawalRetry = Success(
            await concurrentService.WithdrawAsync(
                hostId,
                first.Value.Id,
                "viewer",
                CancellationToken.None
            )
        );

        retry.WasIdempotent.ShouldBeTrue();
        retry.Value.Id.ShouldBe(first.Value.Id);
        withdrawal.WasIdempotent.ShouldBeFalse();
        withdrawalRetry.WasIdempotent.ShouldBeTrue();
        withdrawalRetry.Value.Id.ShouldBe(first.Value.Id);
        await using var verify = await database.CreateDbContextAsync();
        (
            await verify.PointBalances.SingleAsync(value =>
                value.HostId == hostId && value.Login == "viewer"
            )
        ).Amount.ShouldBe("100");
        var ledger = await verify
            .PointLedgerEntries.Where(value => value.RequestSubmissionId == first.Value.Id)
            .OrderBy(value => value.Id)
            .ToListAsync();
        ledger
            .Select(value => value.Kind)
            .ShouldBe([PointLedgerKind.RequestReservation, PointLedgerKind.RequestRefund]);
        ledger.Select(value => value.Delta).ShouldBe(["-25", "25"]);
        (
            await verify.RequestSubmissions.SingleAsync(value => value.Id == first.Value.Id)
        ).PointReservationState.ShouldBe(RequestPointReservationState.Refunded);

        var events = await service.GetEventsAsync(hostId, 0, 2000, CancellationToken.None);
        events.ShouldAllBe(value =>
            value.HostId == hostId && value.SchemaVersion == 1 && value.PublicPayload.Length <= 1024
        );
        events.Count.ShouldBeLessThanOrEqualTo(200);
        events.Count(value => value.Kind == RequestBoardEventKind.Submitted).ShouldBe(1);
        events.Count(value => value.Kind == RequestBoardEventKind.PointsReserved).ShouldBe(1);
        events.Count(value => value.Kind == RequestBoardEventKind.PointsRefunded).ShouldBe(1);
    }

    [Test]
    public async Task TypedFields_ValidateBoundsAndNeverFetchUrls()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database);
        _ = Success(await service.ConfigureAsync(hostId, Board(), CancellationToken.None));

        var invalidUrl = Rejection(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(
                    Guid.NewGuid(),
                    "viewer",
                    "Bad URL",
                    new Dictionary<string, string>
                    {
                        ["details"] = "Review this",
                        ["link"] = "file:///etc/passwd",
                        ["format"] = "Video",
                        ["rating"] = "5",
                    }
                ),
                CancellationToken.None
            )
        );
        var invalidClip = Rejection(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(
                    Guid.NewGuid(),
                    "viewer",
                    "Bad clip",
                    new Dictionary<string, string>
                    {
                        ["details"] = "Review this",
                        ["link"] = "https://example.com/watch",
                        ["clip"] = "https://example.com/clip/anything",
                        ["format"] = "Video",
                        ["rating"] = "5",
                    }
                ),
                CancellationToken.None
            )
        );
        var valid = Success(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(
                    Guid.NewGuid(),
                    "viewer",
                    "Good clip",
                    new Dictionary<string, string>
                    {
                        ["details"] = "Review this",
                        ["link"] = "HTTPS://Example.com:443/watch#fragment",
                        ["clip"] = "https://clips.twitch.tv/UsefulClip",
                        ["format"] = "video",
                        ["rating"] = "5.5",
                    }
                ),
                CancellationToken.None
            )
        );

        invalidUrl.Message.ShouldContain("valid HTTP or HTTPS URL");
        invalidClip.Message.ShouldContain("valid Twitch clip URL");
        valid
            .Value.Values.Single(value => value.Key == "link")
            .Value.ShouldBe("https://example.com/watch");
        valid.Value.Values.Single(value => value.Key == "format").Value.ShouldBe("Video");
        valid.Value.Values.Single(value => value.Key == "rating").Value.ShouldBe("5.5");
    }

    [Test]
    public async Task RepeatedModeratorClosure_ReturnsExistingRefundedResult()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        await SeedBalanceAsync(database, hostId, "viewer", "100");
        var service = CreateService(database);
        _ = Success(
            await service.ConfigureAsync(hostId, Board(pointCost: "25"), CancellationToken.None)
        );
        var submission = Success(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(Guid.NewGuid(), "viewer", "Rejected request"),
                CancellationToken.None
            )
        ).Value;

        var rejection = Moderate(
            submission.Id,
            RequestSubmissionStatus.Rejected,
            "Not for this stream.",
            "Already covered.",
            "Duplicate."
        );
        var first = Success(await service.ModerateAsync(hostId, rejection, CancellationToken.None));
        var retry = Success(await service.ModerateAsync(hostId, rejection, CancellationToken.None));

        first.WasIdempotent.ShouldBeFalse();
        retry.WasIdempotent.ShouldBeTrue();
        retry.Value.Public.Status.ShouldBe(RequestSubmissionStatus.Rejected);
        retry.Value.PointReservationState.ShouldBe(RequestPointReservationState.Refunded);
        await using var verify = await database.CreateDbContextAsync();
        (
            await verify.PointLedgerEntries.CountAsync(value =>
                value.RequestSubmissionId == submission.Id
                && value.Kind == PointLedgerKind.RequestRefund
            )
        ).ShouldBe(1);
        (
            await verify.PointBalances.SingleAsync(value =>
                value.HostId == hostId && value.Login == "viewer"
            )
        ).Amount.ShouldBe("100");
    }

    [Test]
    public async Task Workflow_PublicProjection_NeverIncludesPrivateModerationFields()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database);
        _ = Success(await service.ConfigureAsync(hostId, Board(), CancellationToken.None));
        var submission = Success(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(Guid.NewGuid(), "viewer", "A useful game"),
                CancellationToken.None
            )
        ).Value;

        _ = Success(
            await service.ModerateAsync(
                hostId,
                Moderate(
                    submission.Id,
                    RequestSubmissionStatus.Approved,
                    "Public explanation",
                    "Private note",
                    "Private rejection"
                ),
                CancellationToken.None
            )
        );
        _ = Success(
            await service.ModerateAsync(
                hostId,
                Moderate(
                    submission.Id,
                    RequestSubmissionStatus.Queued,
                    "Public explanation",
                    "Private note",
                    "Private rejection"
                ),
                CancellationToken.None
            )
        );
        _ = Success(
            await service.ModerateAsync(
                hostId,
                Moderate(
                    submission.Id,
                    RequestSubmissionStatus.Completed,
                    "Finished on stream",
                    "Never public",
                    "Also never public"
                ),
                CancellationToken.None
            )
        );

        var publicPage = await service.GetPublicPageAsync("alpha", "games", CancellationToken.None);
        var publicRequest = publicPage!.Submissions.Single();
        publicRequest.Status.ShouldBe(RequestSubmissionStatus.Completed);
        publicRequest.PublicNote.ShouldBe("Finished on stream");
        publicRequest.ToString().ShouldNotContain("Never public");
        publicRequest.ToString().ShouldNotContain("Also never public");

        var moderator = await service.GetModeratorPageAsync(
            hostId,
            "games",
            CancellationToken.None
        );
        moderator!.Submissions.Single().PrivateModeratorNote.ShouldBe("Never public");
    }

    [Test]
    public async Task DuplicateDetection_AssistsModeratorAndMergeTransfersUniqueVotes()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database);
        _ = Success(await service.ConfigureAsync(hostId, Board(), CancellationToken.None));
        var target = Success(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(Guid.NewGuid(), "first", "  Great GAME! "),
                CancellationToken.None
            )
        ).Value;
        var duplicate = Success(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(Guid.NewGuid(), "second", "Great game"),
                CancellationToken.None
            )
        ).Value;
        await ApproveAsync(service, hostId, target.Id);
        await ApproveAsync(service, hostId, duplicate.Id);
        _ = Success(
            await service.VoteAsync(hostId, target.Id, "voter_one", CancellationToken.None)
        );
        _ = Success(
            await service.VoteAsync(hostId, duplicate.Id, "voter_one", CancellationToken.None)
        );
        _ = Success(
            await service.VoteAsync(hostId, duplicate.Id, "voter_two", CancellationToken.None)
        );

        var beforeMerge = await service.GetModeratorSubmissionAsync(
            hostId,
            duplicate.Id,
            CancellationToken.None
        );
        var merged = Success(
            await service.MergeAsync(
                hostId,
                duplicate.Id,
                target.Id,
                "Combined duplicate requests.",
                "Same request.",
                CancellationToken.None
            )
        );
        var mergeRetry = Success(
            await service.MergeAsync(
                hostId,
                duplicate.Id,
                target.Id,
                "Combined duplicate requests.",
                "Same request.",
                CancellationToken.None
            )
        );

        beforeMerge!.PossibleDuplicateIds.ShouldContain(target.Id);
        merged.Value.Public.Status.ShouldBe(RequestSubmissionStatus.Merged);
        merged.Value.Public.MergedIntoSubmissionId.ShouldBe(target.Id);
        mergeRetry.WasIdempotent.ShouldBeTrue();
        mergeRetry.Value.Public.MergedIntoSubmissionId.ShouldBe(target.Id);
        (
            await service.GetModeratorSubmissionAsync(hostId, target.Id, CancellationToken.None)
        )!.Public.VoteCount.ShouldBe(2);
    }

    [Test]
    public async Task HostIsolation_AppliesToQueriesVotesModerationAndEvents()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstHost = await SeedHostAsync(database, "alpha");
        var secondHost = await SeedHostAsync(database, "beta");
        var service = CreateService(database);
        _ = Success(await service.ConfigureAsync(firstHost, Board(), CancellationToken.None));
        _ = Success(await service.ConfigureAsync(secondHost, Board(), CancellationToken.None));
        var submission = Success(
            await service.SubmitAsync(
                firstHost,
                "games",
                Submission(Guid.NewGuid(), "viewer", "Alpha only"),
                CancellationToken.None
            )
        ).Value;

        Rejection(
                await service.VoteAsync(secondHost, submission.Id, "other", CancellationToken.None)
            )
            .ShouldBeOfType<RequestBoardRejection.NotFound>();
        Rejection(
                await service.ModerateAsync(
                    secondHost,
                    Moderate(submission.Id, RequestSubmissionStatus.Approved),
                    CancellationToken.None
                )
            )
            .ShouldBeOfType<RequestBoardRejection.NotFound>();
        (
            await service.GetPublicPageAsync("beta", "games", CancellationToken.None)
        )!.Submissions.ShouldBeEmpty();
        (await service.GetEventsAsync(secondHost, 0, 200, CancellationToken.None)).ShouldAllBe(
            value => value.HostId == secondHost
        );
    }

    [Test]
    public async Task LimitsCooldownVotingAndQueueOrder_AreDeterministic()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.Zero)
        );
        var service = CreateService(database, clock);
        _ = Success(
            await service.ConfigureAsync(
                hostId,
                Board(submissionLimit: 2, cooldownSeconds: 30, voteLimit: 1),
                CancellationToken.None
            )
        );
        var first = Success(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(Guid.NewGuid(), "viewer", "First"),
                CancellationToken.None
            )
        ).Value;
        var cooldown = Rejection(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(Guid.NewGuid(), "viewer", "Too soon"),
                CancellationToken.None
            )
        );
        clock.Advance(TimeSpan.FromSeconds(30));
        var second = Success(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(Guid.NewGuid(), "viewer", "Second"),
                CancellationToken.None
            )
        ).Value;
        clock.Advance(TimeSpan.FromSeconds(30));
        var limit = Rejection(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(Guid.NewGuid(), "viewer", "Too many"),
                CancellationToken.None
            )
        );
        await ApproveAsync(service, hostId, first.Id, priority: 1);
        await ApproveAsync(service, hostId, second.Id, priority: 10);
        var votes = (
            await Task.WhenAll(
                service.VoteAsync(hostId, first.Id, "voter", CancellationToken.None),
                CreateService(database).VoteAsync(hostId, first.Id, "voter", CancellationToken.None)
            )
        )
            .Select(Success)
            .ToArray();
        var voteRetry = votes.Single(value => value.WasIdempotent);
        var voteLimit = Rejection(
            await service.VoteAsync(hostId, second.Id, "voter", CancellationToken.None)
        );

        cooldown.ShouldBeOfType<RequestBoardRejection.Cooldown>();
        limit.ShouldBeOfType<RequestBoardRejection.LimitReached>();
        voteRetry.WasIdempotent.ShouldBeTrue();
        voteRetry.Value.VoteCount.ShouldBe(1);
        voteLimit.ShouldBeOfType<RequestBoardRejection.LimitReached>();
        var page = await service.GetPublicPageAsync("alpha", "games", CancellationToken.None);
        page!.Submissions.Select(value => value.Id).ShouldBe([second.Id, first.Id]);
        await using var verify = await database.CreateDbContextAsync();
        (
            await verify.RequestSubmissionVotes.CountAsync(value =>
                value.SubmissionId == first.Id && value.VoterLogin == "voter"
            )
        ).ShouldBe(1);
    }

    private static ConfigureRequestBoardCommand Board(
        string pointCost = "0",
        int submissionLimit = 3,
        int cooldownSeconds = 0,
        int voteLimit = 10
    )
    {
        return new ConfigureRequestBoardCommand(
            "games",
            "Game requests",
            "Suggest something to play.",
            true,
            pointCost,
            RequestBoardRefundPolicy.RejectedOrWithdrawn,
            submissionLimit,
            cooldownSeconds,
            voteLimit,
            true,
            [
                new RequestBoardFieldCommand(
                    "details",
                    "Details",
                    RequestBoardFieldKind.Text,
                    true,
                    500
                ),
                new RequestBoardFieldCommand(
                    "link",
                    "Link",
                    RequestBoardFieldKind.Url,
                    false,
                    2048
                ),
                new RequestBoardFieldCommand(
                    "clip",
                    "Twitch clip",
                    RequestBoardFieldKind.TwitchClip,
                    false,
                    2048
                ),
                new RequestBoardFieldCommand(
                    "format",
                    "Format",
                    RequestBoardFieldKind.Choice,
                    false,
                    100,
                    Choices: ["Video", "Article"]
                ),
                new RequestBoardFieldCommand(
                    "rating",
                    "Rating",
                    RequestBoardFieldKind.Number,
                    false,
                    128,
                    0,
                    10
                ),
            ]
        );
    }

    private static SubmitRequestCommand Submission(
        Guid operationId,
        string login,
        string title,
        IReadOnlyDictionary<string, string>? fields = null
    )
    {
        return new SubmitRequestCommand(
            operationId,
            login,
            title,
            "Games",
            ["community"],
            fields
                ?? new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["details"] = "Please consider this.",
                }
        );
    }

    private static ModerateRequestCommand Moderate(
        long submissionId,
        RequestSubmissionStatus status,
        string publicNote = "",
        string privateNote = "",
        string privateReason = "",
        int priority = 0
    )
    {
        return new ModerateRequestCommand(
            submissionId,
            status,
            publicNote,
            privateNote,
            privateReason,
            priority,
            "Games",
            ["community"]
        );
    }

    private static async Task ApproveAsync(
        RequestBoardService service,
        int hostId,
        long submissionId,
        int priority = 0
    )
    {
        _ = Success(
            await service.ModerateAsync(
                hostId,
                Moderate(submissionId, RequestSubmissionStatus.Approved, priority: priority),
                CancellationToken.None
            )
        );
    }

    private static RequestBoardService CreateService(
        SqliteBlokeBotDbFactory database,
        TimeProvider? clock = null
    )
    {
        return new RequestBoardService(
            database,
            TestEventBus.Create<AppEventKind>(),
            clock ?? TimeProvider.System
        );
    }

    private static RequestBoardResult<T>.Succeeded Success<T>(RequestBoardResult<T> result)
    {
        return result.Match(
            value => value,
            rejected =>
                throw new InvalidOperationException(
                    $"Expected success but received: {rejected.Reason.Message}"
                )
        );
    }

    private static RequestBoardRejection Rejection<T>(RequestBoardResult<T> result)
    {
        return result.Match(
            _ => throw new InvalidOperationException("Expected rejection."),
            rejected => rejected.Reason
        );
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database, string login)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SeedBalanceAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        string login,
        string amount
    )
    {
        await using var db = await database.CreateDbContextAsync();
        db.PointBalances.Add(
            new PointBalance
            {
                HostId = hostId,
                Login = login,
                Amount = amount,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan duration)
        {
            _now = _now.Add(duration);
        }
    }
}
