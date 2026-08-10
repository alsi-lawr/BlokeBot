using System.Globalization;
using System.Numerics;
using BlokeBot.Core.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Points.Balances;

internal static class PointCreditCapacity
{
    public static async Task<bool> CanCreditAsync(
        BlokeBotDbContext db,
        int hostId,
        string login,
        PointAmount current,
        BigInteger credit,
        CancellationToken ct
    )
    {
        var liability = await LoadRefundLiabilityAsync(db, hostId, login, ct);
        return current.Value + liability + credit <= PointAmount.MaximumValue;
    }

    public static Task<bool> IsExposureWithinLimitAsync(
        BlokeBotDbContext db,
        int hostId,
        string login,
        PointAmount current,
        CancellationToken ct
    ) => CanCreditAsync(db, hostId, login, current, BigInteger.Zero, ct);

    private static async Task<BigInteger> LoadRefundLiabilityAsync(
        BlokeBotDbContext db,
        int hostId,
        string login,
        CancellationToken ct
    )
    {
        var normalized = LoginName.Parse(login).Value;
        var bountyAmounts = await db
            .BountyPledges.AsNoTracking()
            .Where(value =>
                value.HostId == hostId
                && value.ContributorLogin == normalized
                && value.State == BountyPledgeState.Reserved
            )
            .Select(value => value.Amount)
            .ToListAsync(ct);
        var requestAmounts = await (
            from submission in db.RequestSubmissions.AsNoTracking()
            join board in db.RequestBoards.AsNoTracking() on submission.BoardId equals board.Id
            where
                submission.HostId == hostId
                && submission.SubmitterLogin == normalized
                && submission.PointReservationState == RequestPointReservationState.Reserved
                && board.RefundPolicy != RequestBoardRefundPolicy.Never
            select board.PointCost
        ).ToListAsync(ct);

        return bountyAmounts
            .Concat(requestAmounts)
            .Aggregate(
                BigInteger.Zero,
                static (total, amount) =>
                    total + BigInteger.Parse(amount, CultureInfo.InvariantCulture)
            );
    }
}
