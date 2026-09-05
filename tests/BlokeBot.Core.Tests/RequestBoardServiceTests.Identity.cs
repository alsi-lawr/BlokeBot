using System.Text.Json;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Privacy;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class RequestBoardServiceTests
{
    [Test]
    public async Task Rename_PreservesReplayLimitsSelfOwnershipAndOriginalRefundDestination()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var otherHostId = await SeedHostAsync(database, "beta");
        await SeedBalanceAsync(database, hostId, "original", "100");
        await SeedBalanceAsync(database, hostId, "renamed", "70");
        var service = CreateService(database);
        var board = Board(pointCost: "25", submissionLimit: 1, cooldownSeconds: 600);
        _ = Success(await service.ConfigureAsync(hostId, board, CancellationToken.None));
        _ = Success(await service.ConfigureAsync(otherHostId, board, CancellationToken.None));
        var original = RequestBoardTestActor.Identified("stable-owner", "original");
        var renamed = RequestBoardTestActor.Identified("stable-owner", "renamed");
        var reclaimed = RequestBoardTestActor.Identified("different-owner", "original");
        var command = Submission(Guid.NewGuid(), "original", "First") with { Actor = original };
        var first = Success(
            await service.SubmitAsync(hostId, "games", command, CancellationToken.None)
        ).Value;

        Success(
            await service.SubmitAsync(
                hostId,
                "games",
                command with
                {
                    Actor = renamed,
                },
                CancellationToken.None
            )
        )
            .WasIdempotent.ShouldBeTrue();
        _ = Rejection(
                await service.SubmitAsync(
                    hostId,
                    "games",
                    command with
                    {
                        Actor = reclaimed,
                    },
                    CancellationToken.None
                )
            )
            .ShouldBeOfType<RequestBoardRejection.Conflict>();
        _ = Rejection(
                await service.SubmitAsync(
                    hostId,
                    "games",
                    command with
                    {
                        OperationId = Guid.NewGuid(),
                        Actor = renamed,
                    },
                    CancellationToken.None
                )
            )
            .ShouldBeOfType<RequestBoardRejection.LimitReached>();
        _ = Rejection(
                await service.WithdrawAsync(hostId, first.Id, reclaimed, CancellationToken.None)
            )
            .ShouldBeOfType<RequestBoardRejection.NotFound>();
        var own = (
            await service.GetSelfAsync(hostId, "games", renamed, [first.Id], CancellationToken.None)
        )!;
        own.ActiveSubmissionCount.ShouldBe(1);
        own.WithdrawableSubmissionIds.ShouldBe([first.Id]);
        (
            await service.GetSelfAsync(
                hostId,
                "games",
                reclaimed,
                [first.Id],
                CancellationToken.None
            )
        )!.ActiveSubmissionCount.ShouldBe(0);
        (
            await service.GetSelfAsync(
                otherHostId,
                "games",
                renamed,
                [first.Id],
                CancellationToken.None
            )
        )!.WithdrawableSubmissionIds.ShouldBeEmpty();
        _ = Success(
            await service.SubmitAsync(
                hostId,
                "games",
                command with
                {
                    OperationId = Guid.NewGuid(),
                    Actor = reclaimed,
                },
                CancellationToken.None
            )
        );

        _ = Success(await service.WithdrawAsync(hostId, first.Id, renamed, CancellationToken.None));
        Success(await service.WithdrawAsync(hostId, first.Id, renamed, CancellationToken.None))
            .WasIdempotent.ShouldBeTrue();
        _ = Rejection(
                await service.SubmitAsync(
                    hostId,
                    "games",
                    command with
                    {
                        OperationId = Guid.NewGuid(),
                        Actor = renamed,
                    },
                    CancellationToken.None
                )
            )
            .ShouldBeOfType<RequestBoardRejection.Cooldown>();
        await using var verify = await database.CreateDbContextAsync();
        (
            await verify.PointBalances.SingleAsync(row =>
                row.HostId == hostId && row.Login == "original"
            )
        ).Amount.ShouldBe("75");
        (
            await verify.PointBalances.SingleAsync(row =>
                row.HostId == hostId && row.Login == "renamed"
            )
        ).Amount.ShouldBe("70");
        var ledger = await verify
            .PointLedgerEntries.Where(row => row.RequestSubmissionId == first.Id)
            .OrderBy(row => row.Id)
            .ToListAsync();
        ledger.Select(row => row.Delta).ShouldBe(["-25", "25"]);
        ledger.Select(row => row.Login).ShouldBe(["original", "original"]);
        var publicJson = JsonSerializer.Serialize(
            await service.GetPublicPageAsync("alpha", "games", CancellationToken.None)
        );
        publicJson.ShouldNotContain(original.TwitchUserId);
        publicJson.ShouldNotContain(reclaimed.TwitchUserId);
        JsonSerializer
            .Serialize(await service.GetEventsAsync(hostId, 0, 100, CancellationToken.None))
            .ShouldNotContain(original.TwitchUserId);
        await using var privacy = await database.CreateDbContextAsync();
        var subject = PrivacySubject.Create(original.TwitchUserId, renamed.Login);
        var exported = await ViewerPrivacyService.ExportAsync(
            privacy,
            subject,
            hostId,
            CancellationToken.None
        );
        exported.Sections["request-boards.submissions"].Count.ShouldBe(1);
        _ = await ViewerPrivacyService.EraseAsync(privacy, subject, hostId, CancellationToken.None);
        (
            await privacy.RequestSubmissions.AsNoTracking().SingleAsync()
        ).SubmitterTwitchUserId.ShouldBe(reclaimed.TwitchUserId);
    }

    [Test]
    public async Task Rename_VotesDeduplicateByIdentityThroughMergeAndPrivacyErasure()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database);
        _ = Success(
            await service.ConfigureAsync(hostId, Board(voteLimit: 2), CancellationToken.None)
        );
        var first = Success(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(Guid.NewGuid(), "submitter", "First"),
                CancellationToken.None
            )
        ).Value;
        var second = Success(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(Guid.NewGuid(), "submitter", "Second"),
                CancellationToken.None
            )
        ).Value;
        var third = Success(
            await service.SubmitAsync(
                hostId,
                "games",
                Submission(Guid.NewGuid(), "submitter", "Third"),
                CancellationToken.None
            )
        ).Value;
        foreach (var id in new[] { first.Id, second.Id, third.Id })
        {
            _ = Success(
                await service.ModerateAsync(
                    hostId,
                    Moderate(id, RequestSubmissionStatus.Approved),
                    CancellationToken.None
                )
            );
        }
        var original = RequestBoardTestActor.Identified("voter-id", "original");
        var renamed = RequestBoardTestActor.Identified("voter-id", "renamed");
        var reclaimed = RequestBoardTestActor.Identified("another-id", "original");
        _ = Success(await service.VoteAsync(hostId, first.Id, original, CancellationToken.None));
        Success(await service.VoteAsync(hostId, first.Id, renamed, CancellationToken.None))
            .WasIdempotent.ShouldBeTrue();
        _ = Success(await service.VoteAsync(hostId, second.Id, renamed, CancellationToken.None));
        _ = Rejection(await service.VoteAsync(hostId, third.Id, renamed, CancellationToken.None))
            .ShouldBeOfType<RequestBoardRejection.LimitReached>();
        _ = Success(await service.VoteAsync(hostId, first.Id, reclaimed, CancellationToken.None));
        _ = Success(
            await service.MergeAsync(hostId, second.Id, first.Id, "", "", CancellationToken.None)
        );
        var self = (
            await service.GetSelfAsync(
                hostId,
                "games",
                renamed,
                [first.Id, second.Id],
                CancellationToken.None
            )
        )!;
        self.VotesUsed.ShouldBe(1);
        self.VotesRemaining.ShouldBe(1);
        self.VotedSubmissionIds.ShouldBe([first.Id]);
        await using (var privacy = await database.CreateDbContextAsync())
        {
            var exported = await ViewerPrivacyService.ExportAsync(
                privacy,
                PrivacySubject.Create("voter-id", "renamed"),
                hostId,
                CancellationToken.None
            );
            exported.Sections["request-boards.votes"].Count.ShouldBe(1);
            var erased = await ViewerPrivacyService.EraseAsync(
                privacy,
                PrivacySubject.Create("voter-id", "renamed"),
                hostId,
                CancellationToken.None
            );
            erased.ChangedRows["request-boards.votes"].ShouldBe(1);
        }
        await using var verify = await database.CreateDbContextAsync();
        (await verify.RequestSubmissions.SingleAsync(row => row.Id == first.Id)).VoteCount.ShouldBe(
            1
        );
        (await verify.RequestSubmissionVotes.SingleAsync()).VoterTwitchUserId.ShouldBe(
            "another-id"
        );
    }
}
