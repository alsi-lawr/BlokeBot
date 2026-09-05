using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.RequestBoards;

public sealed record RequestBoardSelfView(
    int ActiveSubmissionCount,
    int VotesUsed,
    int VotesRemaining,
    IReadOnlyList<long> WithdrawableSubmissionIds,
    IReadOnlyList<long> VotedSubmissionIds
);

public sealed partial class RequestBoardService
{
    public async Task<RequestBoardSelfView?> GetSelfAsync(
        int hostId,
        string boardSlug,
        RequestActor actor,
        IReadOnlyCollection<long> visibleSubmissionIds,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var slug = CommunityInput.NormalizeSlug(boardSlug);
        var board = await db
            .RequestBoards.AsNoTracking()
            .SingleOrDefaultAsync(value => value.HostId == hostId && value.Slug == slug, ct);
        if (board is null)
        {
            return null;
        }

        var visibleIds = visibleSubmissionIds
            .Take(RequestBoardLimits.MaximumPublicPageSize)
            .ToArray();
        var ownSubmissions = db
            .RequestSubmissions.AsNoTracking()
            .Where(value =>
                value.HostId == hostId
                && value.BoardId == board.Id
                && value.SubmitterTwitchUserId == actor.TwitchUserId
            );
        var activeStatuses = ActiveSubmissionStatuses();
        var activeCount = await ownSubmissions.CountAsync(
            value => activeStatuses.Contains(value.Status),
            ct
        );
        var withdrawable =
            visibleIds.Length == 0
                ? []
                : await ownSubmissions
                    .Where(value =>
                        visibleIds.Contains(value.Id)
                        && (
                            value.Status == RequestSubmissionStatus.Pending
                            || value.Status == RequestSubmissionStatus.Approved
                            || value.Status == RequestSubmissionStatus.Queued
                        )
                    )
                    .Select(value => value.Id)
                    .ToListAsync(ct);
        var ownVotes = db
            .RequestSubmissionVotes.AsNoTracking()
            .Where(value =>
                value.Submission!.HostId == hostId
                && value.Submission.BoardId == board.Id
                && value.VoterTwitchUserId == actor.TwitchUserId
            );
        var votesUsed = await ownVotes.CountAsync(ct);
        var votedIds =
            visibleIds.Length == 0
                ? []
                : await ownVotes
                    .Where(value => visibleIds.Contains(value.SubmissionId))
                    .Select(value => value.SubmissionId)
                    .ToListAsync(ct);
        return new RequestBoardSelfView(
            activeCount,
            votesUsed,
            Math.Max(0, board.VoteLimitPerUser - votesUsed),
            withdrawable,
            votedIds
        );
    }
}
