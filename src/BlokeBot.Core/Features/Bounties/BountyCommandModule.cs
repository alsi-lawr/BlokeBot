using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Bounties;

internal sealed class BountyCommandModule(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    BountyService bounties,
    TimeProvider timeProvider
) : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands)
    {
        _ = commands.Map(FixedChatCommandRoutes.Bounties, ListAsync);
        _ = commands.Map(FixedChatCommandRoutes.Bounty, ViewAsync);
        _ = commands.Map(FixedChatCommandRoutes.BountyPledge, PledgeAsync);
        _ = commands.Map(FixedChatCommandRoutes.BountyCreate, CreateAsync);
        _ = commands.Map(
            FixedChatCommandRoutes.BountyOpen,
            (context, args, ct) =>
                TransitionAsync(context, args, BountyTransitionAction.OpenFunding, ct)
        );
        _ = commands.Map(
            FixedChatCommandRoutes.BountyAccept,
            (context, args, ct) => TransitionAsync(context, args, BountyTransitionAction.Accept, ct)
        );
        _ = commands.Map(
            FixedChatCommandRoutes.BountyComplete,
            (context, args, ct) =>
                TransitionAsync(context, args, BountyTransitionAction.Complete, ct)
        );
        _ = commands.Map(
            FixedChatCommandRoutes.BountyFail,
            (context, args, ct) => TransitionAsync(context, args, BountyTransitionAction.Fail, ct)
        );
        _ = commands.Map(
            FixedChatCommandRoutes.BountyCancel,
            (context, args, ct) => TransitionAsync(context, args, BountyTransitionAction.Cancel, ct)
        );
        _ = commands.Map(
            FixedChatCommandRoutes.BountyReject,
            (context, args, ct) => TransitionAsync(context, args, BountyTransitionAction.Reject, ct)
        );
        _ = commands.Map(FixedChatCommandRoutes.BountyExtend, ExtendAsync);
    }

    private async ValueTask ListAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (await FindEnabledHostIdAsync(context.Message.Channel, ct) is null)
        {
            return;
        }
        var board = await bounties.GetPublicBoardAsync(context.Message.Channel, ct);
        var active = board.Where(value => !IsTerminal(value.Status)).Take(5).ToArray();
        await context.ReplyAsync(
            active.Length == 0
                ? "No public bounties are open."
                : $"Bounties: {string.Join("; ", active.Select(Summary))}. /bounties/{CommunityInput.NormalizeLogin(context.Message.Channel)}",
            ct
        );
    }

    private async ValueTask ViewAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (await FindEnabledHostIdAsync(context.Message.Channel, ct) is null)
        {
            return;
        }
        if (args.Count != 1)
        {
            await context.ReplyAsync("Usage: !bounty <bounty-id>", ct);
            return;
        }

        var board = await bounties.GetPublicBoardAsync(context.Message.Channel, ct);
        var bounty = Resolve(board, args[0]);
        await context.ReplyAsync(
            bounty is null
                ? "Public bounty not found."
                : $"{Summary(bounty)}. Contributors: {ContributorSummary(bounty)}. /bounties/{bounty.HostLogin}",
            ct
        );
    }

    private async ValueTask PledgeAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostId = await FindEnabledHostIdAsync(context.Message.Channel, ct);
        if (hostId is null)
        {
            return;
        }
        if (args.Count != 2 || ParseAmount(args[1]) is not { IsZero: false } amount)
        {
            await context.ReplyAsync("Usage: !bountypledge <bounty-id> <points>", ct);
            return;
        }

        var board = await bounties.GetPublicBoardAsync(context.Message.Channel, ct);
        var bounty = Resolve(board, args[0]);
        if (bounty is null)
        {
            await context.ReplyAsync("Public bounty not found.", ct);
            return;
        }

        var result = await bounties.PledgeAsync(
            hostId.Value,
            new PledgeBountyCommand(
                MessageOperationId(context.Message),
                bounty.PublicId,
                Actor(context.Message),
                amount
            ),
            ct
        );
        await context.ReplyAsync(
            result.Match(
                succeeded =>
                    succeeded.WasIdempotent
                        ? "That pledge was already recorded."
                        : $"Pledged {succeeded.Value.ReservedAmount.ToDisplayString()} points to {bounty.Title}.",
                rejected => rejected.Reason.Message
            ),
            ct
        );
    }

    private async ValueTask CreateAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostId = await FindEnabledHostIdAsync(context.Message.Channel, ct);
        if (hostId is null)
        {
            return;
        }
        if (!ChatModeratorPolicy.IsModerator(context.Message))
        {
            await context.ReplyAsync("That bounty command is moderator-only.", ct);
            return;
        }
        if (
            args.Count < 7
            || ParseAmount(args[0]) is not { IsZero: false } target
            || !int.TryParse(
                args[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var hours
            )
            || hours < 1
            || ParseVisibility(args[2]) is not { } visibility
            || ParseFailurePolicy(args[3]) is not { } failurePolicy
            || ParseDistribution(args[4]) is not { } distribution
            || ParseAmount(args[5]) is not { } reward
        )
        {
            await context.ReplyAsync(
                "Usage: !bountycreate <target> <hours> <public|private> <refund|spend> <equal|proportional> <bonus> <title> | description | private reason",
                ct
            );
            return;
        }

        var sections = string.Join(' ', args.Skip(6)).Split('|', StringSplitOptions.TrimEntries);
        var result = await bounties.CreateAsync(
            hostId.Value,
            new CreateBountyCommand(
                MessageOperationId(context.Message),
                sections[0],
                sections.ElementAtOrDefault(1) ?? string.Empty,
                target,
                timeProvider.GetUtcNow().AddHours(hours).UtcDateTime,
                reward,
                visibility,
                failurePolicy,
                distribution,
                Actor(context.Message),
                sections.ElementAtOrDefault(2) ?? string.Empty
            ),
            ct
        );
        await context.ReplyAsync(
            result.Match(
                succeeded =>
                    $"Created proposed bounty {Reference(succeeded.Value)}: {succeeded.Value.Title}.",
                rejected => rejected.Reason.Message
            ),
            ct
        );
    }

    private async ValueTask TransitionAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        BountyTransitionAction action,
        CancellationToken ct
    )
    {
        var hostId = await FindEnabledHostIdAsync(context.Message.Channel, ct);
        if (hostId is null)
        {
            return;
        }
        if (!ChatModeratorPolicy.IsModerator(context.Message))
        {
            await context.ReplyAsync("That bounty command is moderator-only.", ct);
            return;
        }
        if (args.Count < 1)
        {
            await context.ReplyAsync(
                $"Usage: !bounty{ActionWord(action)} <bounty-id> | private reason",
                ct
            );
            return;
        }

        var page = await bounties.GetModeratorBoardAsync(hostId.Value, ct);
        var bounty = Resolve(page.Select(value => value.Bounty), args[0]);
        if (bounty is null)
        {
            await context.ReplyAsync("Bounty not found.", ct);
            return;
        }
        var reason = string.Join(' ', args.Skip(1)).TrimStart('|').Trim();
        var result = await bounties.TransitionAsync(
            hostId.Value,
            new TransitionBountyCommand(
                MessageOperationId(context.Message),
                bounty.PublicId,
                bounty.Revision,
                action,
                Actor(context.Message),
                reason
            ),
            ct
        );
        await context.ReplyAsync(
            result.Match(
                succeeded => $"{Reference(succeeded.Value)} is now {succeeded.Value.Status}.",
                rejected => rejected.Reason.Message
            ),
            ct
        );
    }

    private async ValueTask ExtendAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostId = await FindEnabledHostIdAsync(context.Message.Channel, ct);
        if (hostId is null)
        {
            return;
        }
        if (!ChatModeratorPolicy.IsModerator(context.Message))
        {
            await context.ReplyAsync("That bounty command is moderator-only.", ct);
            return;
        }
        if (
            args.Count < 2
            || !int.TryParse(
                args[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var hours
            )
            || hours < 1
        )
        {
            await context.ReplyAsync(
                "Usage: !bountyextend <bounty-id> <hours> | private reason",
                ct
            );
            return;
        }

        var page = await bounties.GetModeratorBoardAsync(hostId.Value, ct);
        var bounty = Resolve(page.Select(value => value.Bounty), args[0]);
        if (bounty is null)
        {
            await context.ReplyAsync("Bounty not found.", ct);
            return;
        }
        var reason = string.Join(' ', args.Skip(2)).TrimStart('|').Trim();
        var result = await bounties.ExtendAsync(
            hostId.Value,
            new ExtendBountyCommand(
                MessageOperationId(context.Message),
                bounty.PublicId,
                bounty.Revision,
                bounty.ExpiresAtUtc.AddHours(hours),
                Actor(context.Message),
                reason
            ),
            ct
        );
        await context.ReplyAsync(
            result.Match(
                succeeded =>
                    $"{Reference(succeeded.Value)} now expires {succeeded.Value.ExpiresAtUtc:u}.",
                rejected => rejected.Reason.Message
            ),
            ct
        );
    }

    private async Task<int?> FindEnabledHostIdAsync(string channel, CancellationToken ct)
    {
        var login = CommunityInput.NormalizeLogin(channel);
        var required = HostFeatureFlags.Bounties | HostFeatureFlags.Points;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(value => value.Login == login && (value.EnabledFeatures & required) == required)
            .Select(value => (int?)value.Id)
            .SingleOrDefaultAsync(ct);
    }

    private static BountyView? Resolve(IEnumerable<BountyView> values, string reference)
    {
        var normalized = reference.Trim().Replace("-", string.Empty, StringComparison.Ordinal);
        if (normalized.Length is < 8 or > 32 || !normalized.All(Uri.IsHexDigit))
        {
            return null;
        }

        var matches = values
            .Where(value =>
                value
                    .PublicId.ToString("N")
                    .StartsWith(normalized, StringComparison.OrdinalIgnoreCase)
            )
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static PointAmount? ParseAmount(string value) =>
        PointAmount
            .ParseNonNegativeAbsolute(value)
            .Match<PointAmount?>(static amount => amount, static _ => null);

    private static BountyVisibility? ParseVisibility(string value) =>
        value.ToLowerInvariant() switch
        {
            "public" => BountyVisibility.Public,
            "private" => BountyVisibility.Private,
            _ => null,
        };

    private static BountyFailurePledgePolicy? ParseFailurePolicy(string value) =>
        value.ToLowerInvariant() switch
        {
            "refund" => BountyFailurePledgePolicy.Refund,
            "spend" => BountyFailurePledgePolicy.Spend,
            _ => null,
        };

    private static BountyRewardDistribution? ParseDistribution(string value) =>
        value.ToLowerInvariant() switch
        {
            "equal" => BountyRewardDistribution.Equal,
            "proportional" => BountyRewardDistribution.Proportional,
            _ => null,
        };

    private static BountyActor Actor(ChatMessage message) =>
        new(message.Tags.GetValueOrDefault("user-id", message.Login), message.Login);

    private static string Summary(BountyView bounty) =>
        $"{Reference(bounty)} {bounty.Title} [{bounty.Status}] {bounty.PledgedAmount.ToDisplayString()}/{bounty.FundingTarget.ToDisplayString()}";

    private static string ContributorSummary(BountyView bounty) =>
        bounty.Contributors.Count == 0
            ? "none yet"
            : string.Join(
                ", ",
                bounty.Contributors.Select(value =>
                    $"@{value.Login} {value.PledgedAmount.ToDisplayString()}"
                )
            );

    private static string Reference(BountyView bounty) => bounty.PublicId.ToString("N")[..8];

    private static string ActionWord(BountyTransitionAction action) =>
        action switch
        {
            BountyTransitionAction.OpenFunding => "open",
            BountyTransitionAction.Accept => "accept",
            BountyTransitionAction.Complete => "complete",
            BountyTransitionAction.Fail => "fail",
            BountyTransitionAction.Cancel => "cancel",
            BountyTransitionAction.Reject => "reject",
            BountyTransitionAction.Expire => "expire",
            _ => action.ToString().ToLowerInvariant(),
        };

    private static bool IsTerminal(BountyStatus status) =>
        status
            is BountyStatus.Completed
                or BountyStatus.Failed
                or BountyStatus.Expired
                or BountyStatus.Cancelled;

    private static Guid MessageOperationId(ChatMessage message) =>
        message.Tags.TryGetValue("id", out var messageId)
        && Guid.TryParse(messageId, out var parsed)
            ? parsed
        : string.IsNullOrWhiteSpace(messageId) ? Guid.NewGuid()
        : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(messageId)).AsSpan(0, 16));
}
