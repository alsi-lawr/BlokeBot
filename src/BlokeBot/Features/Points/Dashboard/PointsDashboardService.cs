using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Identity;

namespace BlokeBot.Features.Points.Dashboard;

public sealed class PointsDashboardService(
    PointBalanceService balances,
    PointsGiveawayService giveaways,
    PointsChangeNotifier changes
)
{
    public async Task<PointsDashboardState> LoadAsync(int hostId, CancellationToken ct) =>
        new(
            await balances.GetLeaderboardAsync(hostId, 25, ct),
            await balances.GetRecentLedgerAsync(hostId, 25, ct),
            await giveaways.GetActiveGiveawayAsync(hostId, ct)
        );

    public async Task<PointBalanceEntry> LookupAsync(
        int hostId,
        string login,
        CancellationToken ct
    ) => await balances.GetBalanceAsync(hostId, login, ct);

    public async Task<PointOperationResult> AddAsync(
        int hostId,
        string targetLogin,
        string amountText,
        string actorLogin,
        CancellationToken ct
    )
    {
        if (!PointAmount.TryParseAbsolute(amountText, out var amount) || amount.IsZero)
            return new PointOperationResult(false, "Invalid amount.");

        var result = await balances.AddAsync(
            hostId,
            targetLogin,
            amount,
            actorLogin,
            "dashboard",
            ct
        );
        await changes.NotifyChangedAsync();
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

    public async Task<PointOperationResult> GiveAsync(
        int hostId,
        string fromLogin,
        string toLogin,
        string amountText,
        CancellationToken ct
    )
    {
        var source = await balances.GetBalanceAsync(hostId, fromLogin, ct);
        if (!TryParseSpend(amountText, source.Balance, out var amount))
            return new PointOperationResult(false, "Invalid amount.");

        var result = await balances.TransferAsync(hostId, fromLogin, toLogin, amount, ct);
        await changes.NotifyChangedAsync();
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

    public async Task<PointOperationResult> RemoveAsync(
        int hostId,
        string targetLogin,
        string amountText,
        string actorLogin,
        CancellationToken ct
    )
    {
        var target = await balances.GetBalanceAsync(hostId, targetLogin, ct);
        if (!TryParseSpend(amountText, target.Balance, out var amount))
            return new PointOperationResult(false, "Invalid amount.");

        var result = await balances.RemoveAsync(
            hostId,
            targetLogin,
            amount,
            actorLogin,
            "dashboard",
            ct
        );
        await changes.NotifyChangedAsync();
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

    public Task<PointOperationResult> StartGiveawayAsync(
        int hostId,
        string hostLogin,
        CancellationToken ct
    ) => giveaways.StartAsync(hostId, hostLogin, null, ct);

    public Task<PointOperationResult> EndGiveawayAsync(
        int hostId,
        string hostLogin,
        CancellationToken ct
    ) => giveaways.EndAsync(hostId, hostLogin, ct);

    public Task<PointOperationResult> CancelGiveawayAsync(int hostId, CancellationToken ct) =>
        giveaways.CancelAsync(hostId, ct);

    private static bool TryParseSpend(
        string value,
        PointAmount sourceBalance,
        out PointAmount amount
    )
    {
        try
        {
            amount = PointAmountArgumentParser.ParseSpendAmount(value, sourceBalance);
            return true;
        }
        catch
        {
            amount = PointAmount.Zero;
            return false;
        }
    }
}
