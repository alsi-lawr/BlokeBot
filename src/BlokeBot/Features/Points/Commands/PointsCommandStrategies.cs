using BlokeBot.Features.Commands;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Gambling;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Identity;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Points.Commands;

public abstract class PointsCommandStrategy(PointsCommandService commands)
    : ICommandStrategy<PointsCommandKind, AppCommandRouteState>
{
    protected PointsCommandService Commands { get; } = commands;

    public abstract PointsCommandKind Kind { get; }

    public abstract IReadOnlyList<string> DefaultAliases { get; }

    public abstract bool RequiresModerator { get; }

    public async ValueTask<string> ModeratorOnlyReplyAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await Commands.CreateResolutionAsync(
            context.State.HostId,
            Kind,
            cancellationToken
        );
        return Format(resolution.Settings.ModeratorOnlyReply, resolution.Settings);
    }

    public abstract ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    );

    protected async Task<PointsCommandResolution> LoadResolutionAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    ) => await Commands.CreateResolutionAsync(context.State.HostId, Kind, cancellationToken);

    protected static async ValueTask ReplyAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        PointOperationResult result,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
            await context.Command.ReplyAsync(result.Message, cancellationToken);
    }

    protected static string Format(
        string template,
        PointsSettings settings,
        string? user = null,
        string? from = null,
        string? to = null,
        string? amount = null,
        string? balance = null
    ) =>
        MessageTemplateFormatter.Format(
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

    protected static PointOperationResult Insufficient(PointsSettings settings) =>
        PointOperationResult.Failure(
            PointOperationFailureReason.InsufficientBalance,
            Format(settings.InsufficientBalanceReply, settings)
        );

    protected static PointOperationResult Invalid(PointsSettings settings) =>
        PointOperationResult.Failure(
            PointOperationFailureReason.InvalidAmount,
            Format(settings.InvalidAmountReply, settings)
        );

    protected static bool TryParseSpend(
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

public sealed class PointsBalanceCommandStrategy(
    PointsCommandService commands,
    PointBalanceService balances
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.Points;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["points"];

    public override bool RequiresModerator => false;

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        PointOperationResult result;
        if (context.Args.Count > 1)
        {
            result = Invalid(resolution.Settings);
        }
        else if (
            context.Args.Count == 1
            && !TwitchModeratorPolicy.IsModerator(context.Command.Message)
        )
        {
            result = new PointOperationResult(
                false,
                Format(resolution.Settings.ModeratorOnlyReply, resolution.Settings)
            );
        }
        else
        {
            var login = context.Args.Count == 0 ? context.Command.Message.Login : context.Args[0];
            var balance = await balances.GetBalanceAsync(
                resolution.HostId,
                login,
                cancellationToken
            );
            var template =
                context.Args.Count == 0
                    ? resolution.Settings.BalanceReply
                    : resolution.Settings.OtherBalanceReply;
            result = new PointOperationResult(
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

        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class GivePointsCommandStrategy(
    PointsCommandService commands,
    PointBalanceService balances
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.GivePoints;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["givepoints"];

    public override bool RequiresModerator => false;

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        PointOperationResult result;
        if (context.Args.Count != 2)
        {
            result = Invalid(resolution.Settings);
        }
        else
        {
            var source = await balances.GetBalanceAsync(
                resolution.HostId,
                context.Command.Message.Login,
                cancellationToken
            );
            if (!TryParseSpend(context.Args[1], source.Balance, out var amount))
            {
                result = Invalid(resolution.Settings);
            }
            else
            {
                var target = LoginName.Parse(context.Args[0]).Value;
                result = await balances.TransferAsync(
                    resolution.HostId,
                    context.Command.Message.Login,
                    target,
                    amount,
                    cancellationToken
                );
                result =
                    result.Success
                        ? result with
                        {
                            Message = Format(
                                resolution.Settings.TransferReply,
                                resolution.Settings,
                                from: context.Command.Message.Login,
                                to: target,
                                amount: amount.ToDisplayString(),
                                balance: result.Balance?.ToDisplayString()
                            ),
                        }
                    : result.FailureReason == PointOperationFailureReason.InsufficientBalance
                        ? Insufficient(resolution.Settings)
                    : Invalid(resolution.Settings);
            }
        }

        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class AddPointsCommandStrategy(
    PointsCommandService commands,
    PointBalanceService balances
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.AddPoints;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["addpoints"];

    public override bool RequiresModerator => true;

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        PointOperationResult result;
        if (
            context.Args.Count != 2
            || !PointAmount.TryParseAbsolute(context.Args[1], out var amount)
            || amount.IsZero
        )
        {
            result = Invalid(resolution.Settings);
        }
        else
        {
            var target = LoginName.Parse(context.Args[0]).Value;
            result = await balances.AddAsync(
                resolution.HostId,
                target,
                amount,
                context.Command.Message.Login,
                "chat command",
                cancellationToken
            );
            result = result.Success
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

        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class RemovePointsCommandStrategy(
    PointsCommandService commands,
    PointBalanceService balances
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.RemovePoints;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["removepoints"];

    public override bool RequiresModerator => true;

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        PointOperationResult result;
        if (context.Args.Count != 2)
        {
            result = Invalid(resolution.Settings);
        }
        else
        {
            var target = LoginName.Parse(context.Args[0]).Value;
            var source = await balances.GetBalanceAsync(
                resolution.HostId,
                target,
                cancellationToken
            );
            if (!TryParseSpend(context.Args[1], source.Balance, out var amount))
            {
                result = Invalid(resolution.Settings);
            }
            else
            {
                result = await balances.RemoveAsync(
                    resolution.HostId,
                    target,
                    amount,
                    context.Command.Message.Login,
                    "chat command",
                    cancellationToken
                );
                result =
                    result.Success
                        ? result with
                        {
                            Message = Format(
                                resolution.Settings.RemoveReply,
                                resolution.Settings,
                                user: target,
                                amount: amount.ToDisplayString(),
                                balance: result.Balance?.ToDisplayString()
                            ),
                        }
                    : result.FailureReason == PointOperationFailureReason.InsufficientBalance
                        ? Insufficient(resolution.Settings)
                    : Invalid(resolution.Settings);
            }
        }

        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class GambleCommandStrategy(
    PointsCommandService commands,
    PointBalanceService balances,
    IPointsRandom random
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.Gamble;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["gamble"];

    public override bool RequiresModerator => false;

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        PointOperationResult result;
        if (context.Args.Count != 1)
        {
            result = Invalid(resolution.Settings);
        }
        else
        {
            var source = await balances.GetBalanceAsync(
                resolution.HostId,
                context.Command.Message.Login,
                cancellationToken
            );
            if (!TryParseSpend(context.Args[0], source.Balance, out var stake))
            {
                result = Invalid(resolution.Settings);
            }
            else
            {
                var won = random.NextDouble() * 100 < resolution.Settings.GamblingWinRatePercent;
                result = await balances.ApplyGambleAsync(
                    resolution.HostId,
                    context.Command.Message.Login,
                    stake,
                    won,
                    cancellationToken
                );
                result =
                    result.Success
                        ? result with
                        {
                            Message = Format(
                                won
                                    ? resolution.Settings.GamblingWinReply
                                    : resolution.Settings.GamblingLoseReply,
                                resolution.Settings,
                                user: context.Command.Message.Login,
                                amount: stake.ToDisplayString(),
                                balance: result.Balance?.ToDisplayString()
                            ),
                        }
                    : result.FailureReason == PointOperationFailureReason.InsufficientBalance
                        ? Insufficient(resolution.Settings)
                    : Invalid(resolution.Settings);
            }
        }

        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class StartGiveawayCommandStrategy(
    PointsCommandService commands,
    PointsGiveawayService giveaways
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.Giveaway;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["giveaway"];

    public override bool RequiresModerator => true;

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        var result =
            context.Args.Count == 0
                ? await giveaways.StartAsync(
                    resolution.HostId,
                    context.Command.Message.Channel,
                    context.Command.ReplyAsync,
                    cancellationToken
                )
                : Invalid(resolution.Settings);
        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class JoinGiveawayCommandStrategy(
    PointsCommandService commands,
    PointsGiveawayService giveaways
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.Join;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["join"];

    public override bool RequiresModerator => false;

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        var result =
            context.Args.Count == 0
                ? await giveaways.JoinAsync(
                    resolution.HostId,
                    context.Command.Message.Channel,
                    context.Command.Message.Login,
                    context.Command.Message.Tags,
                    cancellationToken
                )
                : Invalid(resolution.Settings);
        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class EndGiveawayCommandStrategy(
    PointsCommandService commands,
    PointsGiveawayService giveaways
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.EndGiveaway;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["endgiveaway"];

    public override bool RequiresModerator => true;

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        var result =
            context.Args.Count == 0
                ? await giveaways.EndAsync(
                    resolution.HostId,
                    context.Command.Message.Channel,
                    cancellationToken
                )
                : Invalid(resolution.Settings);
        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class CancelGiveawayCommandStrategy(
    PointsCommandService commands,
    PointsGiveawayService giveaways
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.CancelGiveaway;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["cancelgiveaway"];

    public override bool RequiresModerator => true;

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        var result =
            context.Args.Count == 0
                ? await giveaways.CancelAsync(resolution.HostId, cancellationToken)
                : Invalid(resolution.Settings);
        await ReplyAsync(context, result, cancellationToken);
    }
}
