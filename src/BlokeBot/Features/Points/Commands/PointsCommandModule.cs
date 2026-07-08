using BlokeBot.Features.Commands;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Gambling;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Identity;
using BlokeBot.Persistence.Models;
using BlokeBot.Text;

namespace BlokeBot.Features.Points.Commands;

public sealed class PointsCommandModule(
    PointBalanceService balances,
    PointsGiveawayService giveaways,
    IPointsRandom random
)
{
    public async Task HandleAsync(
        TwitchCommandContext context,
        IReadOnlyList<string> args,
        PointsCommandResolution resolution,
        CancellationToken ct
    )
    {
        if (
            AppCommandCatalog.RequiresModerator(AppCommandCatalog.FromPointsKind(resolution.Kind))
            && !TwitchModeratorPolicy.IsModerator(context.Message)
        )
        {
            await context.ReplyAsync(
                Format(resolution.Settings.ModeratorOnlyReply, resolution.Settings),
                ct
            );
            return;
        }

        var result = resolution.Kind switch
        {
            PointsCommandKind.Points => await HandlePointsAsync(resolution, context, args, ct),
            PointsCommandKind.GivePoints => await HandleGivePointsAsync(
                resolution,
                context,
                args,
                ct
            ),
            PointsCommandKind.AddPoints => await HandleAddPointsAsync(
                resolution,
                context,
                args,
                ct
            ),
            PointsCommandKind.RemovePoints => await HandleRemovePointsAsync(
                resolution,
                context,
                args,
                ct
            ),
            PointsCommandKind.Gamble => await HandleGambleAsync(resolution, context, args, ct),
            PointsCommandKind.Giveaway => args.Count == 0
                ? await giveaways.StartAsync(
                    resolution.HostId,
                    context.Message.Channel,
                    context.ReplyAsync,
                    ct
                )
                : Invalid(resolution.Settings),
            PointsCommandKind.Join => args.Count == 0
                ? await giveaways.JoinAsync(
                    resolution.HostId,
                    context.Message.Channel,
                    context.Message.Login,
                    context.Message.Tags,
                    ct
                )
                : Invalid(resolution.Settings),
            PointsCommandKind.EndGiveaway => args.Count == 0
                ? await giveaways.EndAsync(resolution.HostId, context.Message.Channel, ct)
                : Invalid(resolution.Settings),
            PointsCommandKind.CancelGiveaway => args.Count == 0
                ? await giveaways.CancelAsync(resolution.HostId, ct)
                : Invalid(resolution.Settings),
            _ => null,
        };

        if (result is not null && !string.IsNullOrWhiteSpace(result.Message))
            await context.ReplyAsync(result.Message, ct);
    }

    private async Task<PointOperationResult> HandleAddPointsAsync(
        PointsCommandResolution resolution,
        TwitchCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (
            args.Count != 2
            || !PointAmount.TryParseAbsolute(args[1], out var amount)
            || amount.IsZero
        )
            return Invalid(resolution.Settings);

        var target = LoginName.Parse(args[0]).Value;
        var result = await balances.AddAsync(
            resolution.HostId,
            target,
            amount,
            context.Message.Login,
            "chat command",
            ct
        );
        return result.Success
            ? result with
            {
                Message = Format(
                    resolution.Settings.AddReply,
                    resolution.Settings,
                    user: target,
                    amount: amount.ToDisplayString(),
                    balance: result.Balance?.ToDisplayString()
                ),
            }
            : Invalid(resolution.Settings);
    }

    private async Task<PointOperationResult> HandleGambleAsync(
        PointsCommandResolution resolution,
        TwitchCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (args.Count != 1)
            return Invalid(resolution.Settings);

        var source = await balances.GetBalanceAsync(resolution.HostId, context.Message.Login, ct);
        if (!TryParseSpend(args[0], source.Balance, out var stake))
            return Invalid(resolution.Settings);

        var won = random.NextDouble() * 100 < resolution.Settings.GamblingWinRatePercent;
        var result = await balances.ApplyGambleAsync(
            resolution.HostId,
            context.Message.Login,
            stake,
            won,
            ct
        );
        if (!result.Success)
            return result.FailureReason == PointOperationFailureReason.InsufficientBalance
                ? Insufficient(resolution.Settings)
                : Invalid(resolution.Settings);

        return result with
        {
            Message = Format(
                won ? resolution.Settings.GamblingWinReply : resolution.Settings.GamblingLoseReply,
                resolution.Settings,
                user: context.Message.Login,
                amount: stake.ToDisplayString(),
                balance: result.Balance?.ToDisplayString()
            ),
        };
    }

    private async Task<PointOperationResult> HandleGivePointsAsync(
        PointsCommandResolution resolution,
        TwitchCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (args.Count != 2)
            return Invalid(resolution.Settings);

        var source = await balances.GetBalanceAsync(resolution.HostId, context.Message.Login, ct);
        if (!TryParseSpend(args[1], source.Balance, out var amount))
            return Invalid(resolution.Settings);

        var target = LoginName.Parse(args[0]).Value;
        var result = await balances.TransferAsync(
            resolution.HostId,
            context.Message.Login,
            target,
            amount,
            ct
        );
        if (!result.Success)
            return result.FailureReason == PointOperationFailureReason.InsufficientBalance
                ? Insufficient(resolution.Settings)
                : Invalid(resolution.Settings);

        return result with
        {
            Message = Format(
                resolution.Settings.TransferReply,
                resolution.Settings,
                from: context.Message.Login,
                to: target,
                amount: amount.ToDisplayString(),
                balance: result.Balance?.ToDisplayString()
            ),
        };
    }

    private async Task<PointOperationResult> HandlePointsAsync(
        PointsCommandResolution resolution,
        TwitchCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (args.Count > 1)
            return Invalid(resolution.Settings);

        if (args.Count == 1 && !TwitchModeratorPolicy.IsModerator(context.Message))
            return new PointOperationResult(
                false,
                Format(resolution.Settings.ModeratorOnlyReply, resolution.Settings)
            );

        var login = args.Count == 0 ? context.Message.Login : args[0];
        var balance = await balances.GetBalanceAsync(resolution.HostId, login, ct);
        var template =
            args.Count == 0
                ? resolution.Settings.BalanceReply
                : resolution.Settings.OtherBalanceReply;
        return new PointOperationResult(
            true,
            Format(
                template,
                resolution.Settings,
                user: balance.Login,
                balance: balance.Balance.ToDisplayString()
            ),
            balance.Balance
        );
    }

    private async Task<PointOperationResult> HandleRemovePointsAsync(
        PointsCommandResolution resolution,
        TwitchCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (args.Count != 2)
            return Invalid(resolution.Settings);

        var target = LoginName.Parse(args[0]).Value;
        var source = await balances.GetBalanceAsync(resolution.HostId, target, ct);
        if (!TryParseSpend(args[1], source.Balance, out var amount))
            return Invalid(resolution.Settings);

        var result = await balances.RemoveAsync(
            resolution.HostId,
            target,
            amount,
            context.Message.Login,
            "chat command",
            ct
        );
        if (!result.Success)
            return result.FailureReason == PointOperationFailureReason.InsufficientBalance
                ? Insufficient(resolution.Settings)
                : Invalid(resolution.Settings);

        return result with
        {
            Message = Format(
                resolution.Settings.RemoveReply,
                resolution.Settings,
                user: target,
                amount: amount.ToDisplayString(),
                balance: result.Balance?.ToDisplayString()
            ),
        };
    }

    private static string Format(
        string template,
        PointsSettings settings,
        string? user = null,
        string? from = null,
        string? to = null,
        string? amount = null,
        string? balance = null
    ) =>
        TemplateFormatter.Format(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["label"] = settings.PointLabel,
                ["user"] = user ?? string.Empty,
                ["from"] = from ?? string.Empty,
                ["to"] = to ?? string.Empty,
                ["amount"] = amount ?? string.Empty,
                ["balance"] = balance ?? string.Empty,
            }
        );

    private static PointOperationResult Insufficient(PointsSettings settings) =>
        PointOperationResult.Failure(
            PointOperationFailureReason.InsufficientBalance,
            Format(settings.InsufficientBalanceReply, settings)
        );

    private static PointOperationResult Invalid(PointsSettings settings) =>
        PointOperationResult.Failure(
            PointOperationFailureReason.InvalidAmount,
            Format(settings.InvalidAmountReply, settings)
        );

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
