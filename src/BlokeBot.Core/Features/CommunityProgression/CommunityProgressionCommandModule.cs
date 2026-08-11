using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CommunityProgression;

internal sealed class CommunityProgressionCommandModule(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    CommunityProgressionService progression
) : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands)
    {
        _ = commands.Map(FixedChatCommandRoutes.Progress, ProgressAsync);
        _ = commands.Map(
            FixedChatCommandRoutes.EquipTitle,
            (context, args, ct) => EquipAsync(context, args, CommunityRewardKind.Title, "title", ct)
        );
        _ = commands.Map(
            FixedChatCommandRoutes.EquipBadge,
            (context, args, ct) => EquipAsync(context, args, CommunityRewardKind.Badge, "badge", ct)
        );
        _ = commands.Map(
            FixedChatCommandRoutes.EquipAccent,
            (context, args, ct) =>
                EquipAsync(context, args, CommunityRewardKind.CosmeticAccent, "accent", ct)
        );
    }

    private async ValueTask ProgressAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var host = await FindEnabledHostAsync(context.Message.Channel, ct);
        if (host is null)
        {
            return;
        }
        var viewerId = ViewerId(context.Message);
        if (viewerId is null)
        {
            return;
        }
        var board = await progression.GetPublicAsync(host.Login, ct);
        if (board is null)
        {
            await context.ReplyAsync("Community progression is hidden for this channel.", ct);
            return;
        }
        var current = board.Seasons.FirstOrDefault(value =>
            value.Status == CommunitySeasonStatus.Open
        );
        var progress = current
            ?.Progress.Where(value => value.TwitchUserId == viewerId)
            .Take(4)
            .ToArray();
        await context.ReplyAsync(
            current is null ? "No public community season is open."
                : progress is null or { Length: 0 }
                    ? $"No progress yet in {current.Name}. /community/{host.Login}"
                : $"{current.Name}: {string.Join("; ", progress.Select(value => $"{value.DefinitionName} {value.Amount}/{value.Target}"))}. /community/{host.Login}",
            ct
        );
    }

    private async ValueTask EquipAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CommunityRewardKind kind,
        string kindName,
        CancellationToken ct
    )
    {
        var host = await FindEnabledHostAsync(context.Message.Channel, ct);
        var viewerId = ViewerId(context.Message);
        if (host is null || viewerId is null)
        {
            return;
        }
        if (args.Count != 1)
        {
            await context.ReplyAsync($"Usage: !equip{kindName} <reward-key>", ct);
            return;
        }
        var result = await progression.EquipAsync(
            new(
                MessageOperationId(context.Message),
                host.Id,
                new(viewerId, context.Message.Login, context.Message.Login),
                kind,
                args[0]
            ),
            ct
        );
        await context.ReplyAsync(
            result switch
            {
                CommunityOperationOutcome.Succeeded { WasIdempotent: true } =>
                    $"That {kindName} is already equipped.",
                CommunityOperationOutcome.Succeeded => $"Equipped {kindName} {args[0]}.",
                CommunityOperationOutcome.NotFound => $"{kindName} reward not found.",
                CommunityOperationOutcome.Conflict conflict => conflict.Message,
                CommunityOperationOutcome.Invalid invalid => invalid.Message,
                _ => "Community progression is unavailable.",
            },
            ct
        );
    }

    private async Task<HostIdentity?> FindEnabledHostAsync(string channel, CancellationToken ct)
    {
        var login = CommunityInput.NormalizeLogin(channel);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(value =>
                value.Login == login
                && (value.EnabledFeatures & HostFeatureFlags.CommunityProgression)
                    == HostFeatureFlags.CommunityProgression
            )
            .Select(value => new HostIdentity(value.Id, value.Login))
            .SingleOrDefaultAsync(ct);
    }

    private static string? ViewerId(ChatMessage message) =>
        message.Tags.TryGetValue("user-id", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static Guid MessageOperationId(ChatMessage message) =>
        message.Tags.TryGetValue("id", out var value) && Guid.TryParse(value, out var parsed)
            ? parsed
            : Guid.NewGuid();

    private sealed record HostIdentity(int Id, string Login);
}
