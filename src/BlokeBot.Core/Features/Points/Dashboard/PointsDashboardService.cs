using System.Diagnostics;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Identity;

namespace BlokeBot.Core.Features.Points.Dashboard;

public sealed class PointsDashboardService(
    PointBalanceService balances,
    PointsGiveawayService giveaways,
    PointsChangeNotifier changes,
    IPointTargetUserLookup users
)
{
    public async Task<PointsDashboardState> LoadAsync(int hostId, CancellationToken ct)
    {
        var giveawayResult = await giveaways.GetActiveGiveaway(hostId).ExecuteAsync(ct);
        var giveaway = giveawayResult.Match(
            option => option.Match<PointsGiveawayView?>(value => value, () => null),
            _ => throw new UnreachableException()
        );
        return new(
            await balances.GetLeaderboardAsync(hostId, 25, ct),
            await balances.GetRecentLedgerAsync(hostId, 25, ct),
            giveaway
        );
    }

    public async Task<PointBalanceEntry> LookupAsync(
        int hostId,
        string login,
        CancellationToken ct
    ) => await balances.GetBalanceAsync(hostId, login, ct);

    public async Task<PointOperationOutcome> AddAsync(
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

        async Task<PointOperationOutcome> AddParsedAsync(PointAmount amount)
        {
            var target = LoginName.Parse(targetLogin).Value;
            if (!await users.ExistsAsync(target, ct))
            {
                return new PointOperationOutcome.Failed(
                    $"Twitch user @{target} was not found.",
                    CommandResponseTarget.Chat
                );
            }

            var result = await balances
                .Add(hostId, target, amount, actorLogin, "dashboard")
                .ExecuteAsync(ct);
            await changes.NotifyChangedAsync(ct);
            return result.Match<PointOperationOutcome>(
                _ => new PointOperationOutcome.Succeeded(
                    "Points added.",
                    CommandResponseTarget.Chat
                ),
                _ => new PointOperationOutcome.Failed(
                    "Could not add points.",
                    CommandResponseTarget.Chat
                )
            );
        }
    }

    public async Task<PointOperationOutcome> GiveAsync(
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

        async Task<PointOperationOutcome> GiveParsedAsync(PointAmount amount)
        {
            var target = LoginName.Parse(toLogin).Value;
            if (!await users.ExistsAsync(target, ct))
            {
                return new PointOperationOutcome.Failed(
                    $"Twitch user @{target} was not found.",
                    CommandResponseTarget.Chat
                );
            }

            var result = await balances
                .Transfer(hostId, fromLogin, target, amount)
                .ExecuteAsync(ct);
            await changes.NotifyChangedAsync(ct);
            return result.Match<PointOperationOutcome>(
                _ => new PointOperationOutcome.Succeeded(
                    "Points transferred.",
                    CommandResponseTarget.Chat
                ),
                _ => new PointOperationOutcome.Failed(
                    "Could not transfer points.",
                    CommandResponseTarget.Chat
                )
            );
        }
    }

    public async Task<PointOperationOutcome> RemoveAsync(
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

        async Task<PointOperationOutcome> RemoveParsedAsync(PointAmount amount)
        {
            var result = await balances
                .Remove(hostId, targetLogin, amount, actorLogin, "dashboard")
                .ExecuteAsync(ct);
            await changes.NotifyChangedAsync(ct);
            return result.Match<PointOperationOutcome>(
                _ => new PointOperationOutcome.Succeeded(
                    "Points removed.",
                    CommandResponseTarget.Chat
                ),
                _ => new PointOperationOutcome.Failed(
                    "Could not remove points.",
                    CommandResponseTarget.Chat
                )
            );
        }
    }

    public async Task<PointOperationOutcome> RemoveBalanceAsync(
        int hostId,
        string targetLogin,
        string actorLogin,
        CancellationToken ct
    )
    {
        var result = await balances
            .DeleteBalance(hostId, targetLogin, actorLogin, "dashboard")
            .ExecuteAsync(ct);
        return await result.Match<Task<PointOperationOutcome>>(
            async _ =>
            {
                await changes.NotifyChangedAsync(ct);
                return new PointOperationOutcome.Succeeded(
                    "Point balance removed.",
                    CommandResponseTarget.Chat
                );
            },
            _ =>
                Task.FromResult<PointOperationOutcome>(
                    new PointOperationOutcome.Failed(
                        "No point balance found.",
                        CommandResponseTarget.Chat
                    )
                )
        );
    }

    public Task<PointOperationOutcome> StartGiveawayAsync(
        int hostId,
        string hostLogin,
        CancellationToken ct
    ) => giveaways.StartAsync(hostId, hostLogin, null, ct);

    public Task<PointOperationOutcome> EndGiveawayAsync(
        int hostId,
        string hostLogin,
        CancellationToken ct
    ) => giveaways.EndAsync(hostId, hostLogin, ct);

    public Task<PointOperationOutcome> CancelGiveawayAsync(int hostId, CancellationToken ct) =>
        giveaways.CancelAsync(hostId, ct);

    private static PointOperationOutcome InvalidAmount() =>
        new PointOperationOutcome.Failed("Invalid amount.", CommandResponseTarget.Chat);
}
