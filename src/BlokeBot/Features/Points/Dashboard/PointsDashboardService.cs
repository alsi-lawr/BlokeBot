using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Identity;

namespace BlokeBot.Features.Points.Dashboard;

public sealed class PointsDashboardService(
    PointBalanceService balances,
    PointsGiveawayService giveaways,
    PointsChangeNotifier changes,
    IPointTargetUserLookup users
)
{
    public async Task<PointsDashboardState> LoadAsync(int hostId, CancellationToken ct)
    {
        return new(
            await balances.GetLeaderboardAsync(hostId, 25, ct),
            await balances.GetRecentLedgerAsync(hostId, 25, ct),
            await giveaways.GetActiveGiveawayAsync(hostId, ct)
        );
    }

    public async Task<PointBalanceEntry> LookupAsync(int hostId, string login, CancellationToken ct)
    {
        return await balances.GetBalanceAsync(hostId, login, ct);
    }

    public async Task<PointOperationResult> AddAsync(
        int hostId,
        string targetLogin,
        string amountText,
        string actorLogin,
        CancellationToken ct
    )
    {
        return await PointAmountArgumentParser
            .ParseAbsolute(amountText)
            .Match(AddParsedAsync, _ => Task.FromResult(InvalidAmount()));

        async Task<PointOperationResult> AddParsedAsync(PointAmount amount)
        {
            var target = LoginName.Parse(targetLogin).Value;
            if (!await users.ExistsAsync(target, ct))
            {
                return PointOperationResult.Failure(
                    PointOperationFailureReason.UnknownUser,
                    $"Twitch user @{target} was not found."
                );
            }

            var result = await balances.AddAsync(
                hostId,
                target,
                amount,
                actorLogin,
                "dashboard",
                ct
            );
            await changes.NotifyChangedAsync(ct);
            return result.Success
                ? result with
                {
                    Message = "Points added.",
                }
                : result with
                {
                    Message = "Could not add points.",
                };
        }
    }

    public async Task<PointOperationResult> GiveAsync(
        int hostId,
        string fromLogin,
        string toLogin,
        string amountText,
        CancellationToken ct
    )
    {
        var source = await balances.GetBalanceAsync(hostId, fromLogin, ct);
        return await PointAmountArgumentParser
            .ParseSpend(amountText, source.Balance)
            .Match(GiveParsedAsync, _ => Task.FromResult(InvalidAmount()));

        async Task<PointOperationResult> GiveParsedAsync(PointAmount amount)
        {
            var target = LoginName.Parse(toLogin).Value;
            if (!await users.ExistsAsync(target, ct))
            {
                return PointOperationResult.Failure(
                    PointOperationFailureReason.UnknownUser,
                    $"Twitch user @{target} was not found."
                );
            }

            var result = await balances.TransferAsync(hostId, fromLogin, target, amount, ct);
            await changes.NotifyChangedAsync(ct);
            return result.Success
                ? result with
                {
                    Message = "Points transferred.",
                }
                : result with
                {
                    Message = "Could not transfer points.",
                };
        }
    }

    public async Task<PointOperationResult> RemoveAsync(
        int hostId,
        string targetLogin,
        string amountText,
        string actorLogin,
        CancellationToken ct
    )
    {
        var target = await balances.GetBalanceAsync(hostId, targetLogin, ct);
        return await PointAmountArgumentParser
            .ParseSpend(amountText, target.Balance)
            .Match(RemoveParsedAsync, _ => Task.FromResult(InvalidAmount()));

        async Task<PointOperationResult> RemoveParsedAsync(PointAmount amount)
        {
            var result = await balances.RemoveAsync(
                hostId,
                targetLogin,
                amount,
                actorLogin,
                "dashboard",
                ct
            );
            await changes.NotifyChangedAsync(ct);
            return result.Success
                ? result with
                {
                    Message = "Points removed.",
                }
                : result with
                {
                    Message = "Could not remove points.",
                };
        }
    }

    public async Task<PointOperationResult> RemoveBalanceAsync(
        int hostId,
        string targetLogin,
        string actorLogin,
        CancellationToken ct
    )
    {
        var result = await balances.DeleteBalanceAsync(
            hostId,
            targetLogin,
            actorLogin,
            "dashboard",
            ct
        );
        if (result.Success)
        {
            await changes.NotifyChangedAsync(ct);
        }

        return result.Success
            ? result with
            {
                Message = "Point balance removed.",
            }
            : result with
            {
                Message = "No point balance found.",
            };
    }

    public Task<PointOperationResult> StartGiveawayAsync(
        int hostId,
        string hostLogin,
        CancellationToken ct
    )
    {
        return giveaways.StartAsync(hostId, hostLogin, null, ct);
    }

    public Task<PointOperationResult> EndGiveawayAsync(
        int hostId,
        string hostLogin,
        CancellationToken ct
    )
    {
        return giveaways.EndAsync(hostId, hostLogin, ct);
    }

    public Task<PointOperationResult> CancelGiveawayAsync(int hostId, CancellationToken ct)
    {
        return giveaways.CancelAsync(hostId, ct);
    }

    private static PointOperationResult InvalidAmount()
    {
        return PointOperationResult.Failure(
            PointOperationFailureReason.InvalidAmount,
            "Invalid amount."
        );
    }
}
