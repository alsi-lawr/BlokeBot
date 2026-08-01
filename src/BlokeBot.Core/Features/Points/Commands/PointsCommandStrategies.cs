using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.Points.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Core.Identity;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.Points.Commands;

public abstract class PointsCommandStrategy(PointsCommandService commands)
    : ICommandStrategy<PointsCommandKind, AppCommandRouteState>
{
    protected PointsCommandService Commands { get; } = commands;

    public abstract PointsCommandKind Kind { get; }

    public abstract IReadOnlyList<string> DefaultAliases { get; }

    public abstract CommandStrategyAccess<PointsCommandKind, AppCommandRouteState> Access { get; }

    public async ValueTask<CommandResponse> ModeratorOnlyResponseAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await Commands.CreateResolutionAsync(
            HostId(context.State),
            Kind,
            cancellationToken
        );
        var message = Format(resolution.Settings.ModeratorOnlyReply, resolution.Settings);
        return Response(message, resolution.ReplyDelivery.TargetFor(PointsReplyKeys.ModeratorOnly));
    }

    public abstract ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    );

    protected async Task<PointsCommandResolution> LoadResolutionAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    ) => await Commands.CreateResolutionAsync(HostId(context.State), Kind, cancellationToken);

    private static int HostId(AppCommandRouteState state) =>
        state.Match(static host => host.HostId, static guessingProfile => guessingProfile.HostId);

    protected static async ValueTask ReplyAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        PointOperationOutcome outcome,
        CancellationToken cancellationToken
    )
    {
        var response = outcome.Match(
            succeeded => new CommandResponse(succeeded.Target, succeeded.Message),
            failed => new CommandResponse(failed.Target, failed.Message)
        );
        if (!string.IsNullOrWhiteSpace(response.Message))
        {
            await context.Command.RespondAsync(response, cancellationToken);
        }
    }

    protected static CommandResponse Response(string message, CommandResponseTarget target) =>
        new(target, message);

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

    protected static PointOperationOutcome Insufficient(
        PointsSettings settings,
        ReplyDeliveryMap delivery
    ) =>
        new PointOperationOutcome.Failed(
            Format(settings.InsufficientBalanceReply, settings),
            delivery.TargetFor(PointsReplyKeys.InsufficientBalance)
        );

    protected static PointOperationOutcome Invalid(
        PointsSettings settings,
        ReplyDeliveryMap delivery
    ) =>
        new PointOperationOutcome.Failed(
            Format(settings.InvalidAmountReply, settings),
            delivery.TargetFor(PointsReplyKeys.InvalidAmount)
        );

    protected static PointOperationOutcome UnknownUser(string login) =>
        new PointOperationOutcome.Failed(
            $"Twitch user @{login} was not found.",
            CommandResponseTarget.Chat
        );
}

public sealed class PointsBalanceCommandStrategy(
    PointsCommandService commands,
    PointBalanceService balances
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.Points;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["points"];

    public override CommandStrategyAccess<PointsCommandKind, AppCommandRouteState> Access =>
        new CommandStrategyAccess<PointsCommandKind, AppCommandRouteState>.Everyone();

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        PointOperationOutcome result;
        if (context.Args.Count > 1)
        {
            result = Invalid(resolution.Settings, resolution.ReplyDelivery);
        }
        else if (
            context.Args.Count == 1
            && !ChatModeratorPolicy.IsModerator(context.Command.Message)
        )
        {
            result = new PointOperationOutcome.Failed(
                Format(resolution.Settings.ModeratorOnlyReply, resolution.Settings),
                resolution.ReplyDelivery.TargetFor(PointsReplyKeys.ModeratorOnly)
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
            var replyKey =
                context.Args.Count == 0 ? PointsReplyKeys.Balance : PointsReplyKeys.OtherBalance;
            result = new PointOperationOutcome.Succeeded(
                Format(
                    template,
                    resolution.Settings,
                    user: balance.Login,
                    balance: balance.Balance.ToDisplayString()
                ),
                resolution.ReplyDelivery.TargetFor(replyKey)
            );
        }

        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class GivePointsCommandStrategy(
    PointsCommandService commands,
    PointBalanceService balances,
    IPointTargetUserLookup users
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.GivePoints;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["givepoints"];

    public override CommandStrategyAccess<PointsCommandKind, AppCommandRouteState> Access =>
        new CommandStrategyAccess<PointsCommandKind, AppCommandRouteState>.Everyone();

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        PointOperationOutcome result;
        if (context.Args.Count != 2)
        {
            result = Invalid(resolution.Settings, resolution.ReplyDelivery);
        }
        else
        {
            var source = await balances.GetBalanceAsync(
                resolution.HostId,
                context.Command.Message.Login,
                cancellationToken
            );
            result = await PointAmountArgumentParser
                .ParseSpend(context.Args[1], source.Balance)
                .Match(
                    TransferAsync,
                    _ => Task.FromResult(Invalid(resolution.Settings, resolution.ReplyDelivery))
                );

            async Task<PointOperationOutcome> TransferAsync(PointAmount amount)
            {
                var target = LoginName.Parse(context.Args[0]).Value;
                if (!await users.ExistsAsync(target, cancellationToken))
                {
                    return UnknownUser(target);
                }

                var transfer = await balances
                    .Transfer(resolution.HostId, context.Command.Message.Login, target, amount)
                    .ExecuteAsync(cancellationToken);
                return transfer.Match<PointOperationOutcome>(
                    success => new PointOperationOutcome.Succeeded(
                        Format(
                            resolution.Settings.TransferReply,
                            resolution.Settings,
                            from: context.Command.Message.Login,
                            to: target,
                            amount: amount.ToDisplayString(),
                            balance: success.Balance.ToDisplayString()
                        ),
                        resolution.ReplyDelivery.TargetFor(PointsReplyKeys.Transfer)
                    ),
                    failure =>
                        failure.Match<PointOperationOutcome>(
                            _ => Invalid(resolution.Settings, resolution.ReplyDelivery),
                            _ => UnknownUser(target),
                            _ => Insufficient(resolution.Settings, resolution.ReplyDelivery),
                            _ => Invalid(resolution.Settings, resolution.ReplyDelivery)
                        )
                );
            }
        }

        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class AddPointsCommandStrategy(
    PointsCommandService commands,
    PointBalanceService balances,
    IPointTargetUserLookup users
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.AddPoints;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["addpoints"];

    public override CommandStrategyAccess<PointsCommandKind, AppCommandRouteState> Access =>
        new CommandStrategyAccess<PointsCommandKind, AppCommandRouteState>.ModeratorOnly(
            ModeratorOnlyResponseAsync
        );

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        PointOperationOutcome result;
        if (context.Args.Count != 2)
        {
            result = Invalid(resolution.Settings, resolution.ReplyDelivery);
        }
        else
        {
            result = await PointAmountArgumentParser
                .ParseAbsolute(context.Args[1])
                .Match(
                    AddAsync,
                    _ => Task.FromResult(Invalid(resolution.Settings, resolution.ReplyDelivery))
                );

            async Task<PointOperationOutcome> AddAsync(PointAmount amount)
            {
                var target = LoginName.Parse(context.Args[0]).Value;
                if (!await users.ExistsAsync(target, cancellationToken))
                {
                    return UnknownUser(target);
                }

                var addition = await balances
                    .Add(
                        resolution.HostId,
                        target,
                        amount,
                        context.Command.Message.Login,
                        "chat command"
                    )
                    .ExecuteAsync(cancellationToken);
                return addition.Match<PointOperationOutcome>(
                    success => new PointOperationOutcome.Succeeded(
                        Format(
                            resolution.Settings.AddReply,
                            resolution.Settings,
                            user: target,
                            amount: amount.ToDisplayString(),
                            balance: success.Balance.ToDisplayString()
                        ),
                        resolution.ReplyDelivery.TargetFor(PointsReplyKeys.Add)
                    ),
                    failure =>
                        failure.Match<PointOperationOutcome>(
                            _ => Invalid(resolution.Settings, resolution.ReplyDelivery),
                            _ => UnknownUser(target),
                            _ => Insufficient(resolution.Settings, resolution.ReplyDelivery),
                            _ => Invalid(resolution.Settings, resolution.ReplyDelivery)
                        )
                );
            }
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

    public override CommandStrategyAccess<PointsCommandKind, AppCommandRouteState> Access =>
        new CommandStrategyAccess<PointsCommandKind, AppCommandRouteState>.ModeratorOnly(
            ModeratorOnlyResponseAsync
        );

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        PointOperationOutcome result;
        if (context.Args.Count != 2)
        {
            result = Invalid(resolution.Settings, resolution.ReplyDelivery);
        }
        else
        {
            var target = LoginName.Parse(context.Args[0]).Value;
            var source = await balances.GetBalanceAsync(
                resolution.HostId,
                target,
                cancellationToken
            );
            result = await PointAmountArgumentParser
                .ParseSpend(context.Args[1], source.Balance)
                .Match(
                    RemoveAsync,
                    _ => Task.FromResult(Invalid(resolution.Settings, resolution.ReplyDelivery))
                );

            async Task<PointOperationOutcome> RemoveAsync(PointAmount amount)
            {
                var removal = await balances
                    .Remove(
                        resolution.HostId,
                        target,
                        amount,
                        context.Command.Message.Login,
                        "chat command"
                    )
                    .ExecuteAsync(cancellationToken);
                return removal.Match<PointOperationOutcome>(
                    success => new PointOperationOutcome.Succeeded(
                        Format(
                            resolution.Settings.RemoveReply,
                            resolution.Settings,
                            user: target,
                            amount: amount.ToDisplayString(),
                            balance: success.Balance.ToDisplayString()
                        ),
                        resolution.ReplyDelivery.TargetFor(PointsReplyKeys.Remove)
                    ),
                    failure =>
                        failure.Match<PointOperationOutcome>(
                            _ => Invalid(resolution.Settings, resolution.ReplyDelivery),
                            _ => UnknownUser(target),
                            _ => Insufficient(resolution.Settings, resolution.ReplyDelivery),
                            _ => Invalid(resolution.Settings, resolution.ReplyDelivery)
                        )
                );
            }
        }

        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class GambleCommandStrategy(
    PointsCommandService commands,
    PointBalanceService balances,
    IPointsRandom random,
    PointsGamblingCooldownStore cooldowns,
    IOptions<BlokeBotOptions> options
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.Gamble;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["gamble"];

    public override CommandStrategyAccess<PointsCommandKind, AppCommandRouteState> Access =>
        new CommandStrategyAccess<PointsCommandKind, AppCommandRouteState>.Everyone();

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        if (context.Args.Count != 1)
        {
            await ReplyAsync(
                context,
                Invalid(resolution.Settings, resolution.ReplyDelivery),
                cancellationToken
            );
            return;
        }

        if (resolution.Settings.GamblingCooldownSeconds < 0)
        {
            var failure = new PointsConfigurationValidationError.NegativeGamblingCooldown();
            await ReplyAsync(
                context,
                new PointOperationOutcome.Failed(
                    $"Gambling is unavailable. {failure.Message}",
                    CommandResponseTarget.Chat
                ),
                cancellationToken
            );
            return;
        }

        var source = await balances.GetBalanceAsync(
            resolution.HostId,
            context.Command.Message.Login,
            cancellationToken
        );
        await PointAmountArgumentParser
            .ParseSpend(context.Args[0], source.Balance)
            .Match(
                GambleAsync,
                _ =>
                    ReplyAsync(
                        context,
                        Invalid(resolution.Settings, resolution.ReplyDelivery),
                        cancellationToken
                    )
            );

        async ValueTask GambleAsync(PointAmount stake)
        {
            PointOperationOutcome result;
            if (source.Balance.Value < stake.Value)
            {
                result = Insufficient(resolution.Settings, resolution.ReplyDelivery);
            }
            else
            {
                if (
                    !cooldowns.TryRecord(
                        resolution.HostId,
                        context.Command.Message.Login,
                        Cooldown(resolution.Settings)
                    )
                )
                {
                    return;
                }

                PointGambleOutcome gamble =
                    random.NextDouble() * 100 < resolution.Settings.GamblingWinRatePercent
                        ? new PointGambleOutcome.Won()
                        : new PointGambleOutcome.Lost();
                var mutation = await balances
                    .ApplyGamble(resolution.HostId, context.Command.Message.Login, stake, gamble)
                    .ExecuteAsync(cancellationToken);
                result = mutation.Match<PointOperationOutcome>(
                    success =>
                        gamble.Match<PointOperationOutcome>(
                            _ => new PointOperationOutcome.Succeeded(
                                Format(
                                    resolution.Settings.GamblingWinReply,
                                    resolution.Settings,
                                    user: context.Command.Message.Login,
                                    amount: stake.ToDisplayString(),
                                    balance: success.Balance.ToDisplayString()
                                ),
                                CommandResponseTarget.Chat
                            ),
                            _ => new PointOperationOutcome.Succeeded(
                                Format(
                                    resolution.Settings.GamblingLoseReply,
                                    resolution.Settings,
                                    user: context.Command.Message.Login,
                                    amount: stake.ToDisplayString(),
                                    balance: success.Balance.ToDisplayString()
                                ),
                                CommandResponseTarget.Chat
                            )
                        ),
                    failure =>
                        failure.Match<PointOperationOutcome>(
                            _ => Invalid(resolution.Settings, resolution.ReplyDelivery),
                            _ => UnknownUser(context.Command.Message.Login),
                            _ => Insufficient(resolution.Settings, resolution.ReplyDelivery),
                            _ => Invalid(resolution.Settings, resolution.ReplyDelivery)
                        )
                );
            }

            await ReplyAsync(context, result, cancellationToken);
        }
    }

    private TimeSpan Cooldown(PointsSettings settings)
    {
        var configuredSeconds = settings.GamblingCooldownSeconds;
        var minimumSeconds = options.Value.Points.MinimumGamblingCooldownSeconds;
        var seconds = configuredSeconds < minimumSeconds ? minimumSeconds : configuredSeconds;
        return TimeSpan.FromSeconds(seconds);
    }
}

public sealed class StartGiveawayCommandStrategy(
    PointsCommandService commands,
    PointsGiveawayService giveaways
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.Giveaway;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["giveaway"];

    public override CommandStrategyAccess<PointsCommandKind, AppCommandRouteState> Access =>
        new CommandStrategyAccess<PointsCommandKind, AppCommandRouteState>.ModeratorOnly(
            ModeratorOnlyResponseAsync
        );

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
                    null,
                    cancellationToken
                )
                : Invalid(resolution.Settings, resolution.ReplyDelivery);
        await ReplyAsync(context, result, cancellationToken);
    }
}

public sealed class JoinGiveawayCommandStrategy(
    PointsCommandService commands,
    PointsGiveawayService giveaways
) : PointsCommandStrategy(commands)
{
    public override PointsCommandKind Kind => PointsCommandKind.Join;

    public override IReadOnlyList<string> DefaultAliases { get; } = ["enter"];

    public override CommandStrategyAccess<PointsCommandKind, AppCommandRouteState> Access =>
        new CommandStrategyAccess<PointsCommandKind, AppCommandRouteState>.Everyone();

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
                : Invalid(resolution.Settings, resolution.ReplyDelivery);
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

    public override CommandStrategyAccess<PointsCommandKind, AppCommandRouteState> Access =>
        new CommandStrategyAccess<PointsCommandKind, AppCommandRouteState>.ModeratorOnly(
            ModeratorOnlyResponseAsync
        );

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
                : Invalid(resolution.Settings, resolution.ReplyDelivery);
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

    public override CommandStrategyAccess<PointsCommandKind, AppCommandRouteState> Access =>
        new CommandStrategyAccess<PointsCommandKind, AppCommandRouteState>.ModeratorOnly(
            ModeratorOnlyResponseAsync
        );

    public override async ValueTask ExecuteAsync(
        CommandStrategyContext<PointsCommandKind, AppCommandRouteState> context,
        CancellationToken cancellationToken
    )
    {
        var resolution = await LoadResolutionAsync(context, cancellationToken);
        var result =
            context.Args.Count == 0
                ? await giveaways.CancelAsync(resolution.HostId, cancellationToken)
                : Invalid(resolution.Settings, resolution.ReplyDelivery);
        await ReplyAsync(context, result, cancellationToken);
    }
}
