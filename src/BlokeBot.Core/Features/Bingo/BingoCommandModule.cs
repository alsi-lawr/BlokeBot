using System.Security.Cryptography;
using System.Text;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Bingo;

internal sealed class BingoCommandModule(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    BingoService bingo
) : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands)
    {
        _ = commands.Map(FixedChatCommandRoutes.Bingo, ViewAsync);
        _ = commands.Map(FixedChatCommandRoutes.BingoJoin, JoinAsync);
        _ = commands.Map(FixedChatCommandRoutes.BingoLeave, LeaveAsync);
    }

    private async ValueTask ViewAsync(
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
        var view = await bingo.GetPublicAsync(host.Login, ct);
        if (view?.LiveGame is not { } game)
        {
            await context.ReplyAsync("No Bingo cards are live right now.", ct);
            return;
        }
        var viewerId = context.Message.Tags.GetValueOrDefault("user-id", string.Empty);
        var card = game.Cards.SingleOrDefault(value =>
            value.Participants.Any(participant => participant.TwitchUserId == viewerId)
        );
        var assignment = card is null ? string.Empty : $" Your card is {card.AssignmentName}.";
        await context.ReplyAsync(
            $"Bingo is live: {game.TemplateName} ({game.Dimension.Value}×{game.Dimension.Value}).{assignment} /bingo/{host.Login}",
            ct
        );
    }

    private async ValueTask JoinAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var host = await FindEnabledHostAsync(context.Message.Channel, ct);
        var viewer = Viewer(context.Message);
        var operationId = OperationId(context.Message);
        if (host is null || viewer is null || operationId is null)
        {
            return;
        }
        var current = (await bingo.GetModeratorGamesAsync(host.Id, ct))
            .Select(value => value.Game)
            .FirstOrDefault(value => value.Status == BingoGameStatus.Joining);
        if (current is null)
        {
            await context.ReplyAsync("Bingo joining is closed.", ct);
            return;
        }
        BingoTeamId? teamId = null;
        if (current.Mode == BingoGameMode.Team)
        {
            var teamName = string.Join(' ', args).Trim();
            var team = current.Teams.SingleOrDefault(value =>
                value.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase)
            );
            if (team is null)
            {
                await context.ReplyAsync(
                    $"Choose a Bingo team: {string.Join(", ", current.Teams.Select(value => value.Name))}.",
                    ct
                );
                return;
            }
            teamId = team.Id;
        }
        var result = await bingo.JoinAsync(
            host.Id,
            new(
                operationId.Value,
                current.Id,
                viewer,
                teamId,
                new(viewer.TwitchUserId, viewer.Login),
                string.Empty
            ),
            ct
        );
        await context.ReplyAsync(
            result is BingoOperationOutcome.Succeeded
                ? $"@{viewer.Login} joined Bingo{(teamId is null ? "." : $" on {current.Teams.Single(value => value.Id == teamId).Name}.")}"
                : ResultMessage(result),
            ct
        );
    }

    private async ValueTask LeaveAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var host = await FindEnabledHostAsync(context.Message.Channel, ct);
        var viewer = Viewer(context.Message);
        var operationId = OperationId(context.Message);
        if (host is null || viewer is null || operationId is null)
        {
            return;
        }
        var current = (await bingo.GetModeratorGamesAsync(host.Id, ct))
            .Select(value => value.Game)
            .FirstOrDefault(value => value.Status == BingoGameStatus.Joining);
        if (current is null)
        {
            await context.ReplyAsync("Bingo joining is closed.", ct);
            return;
        }
        var result = await bingo.RemoveAsync(
            host.Id,
            new(
                operationId.Value,
                current.Id,
                viewer,
                null,
                new(viewer.TwitchUserId, viewer.Login),
                string.Empty
            ),
            ct
        );
        await context.ReplyAsync(
            result is BingoOperationOutcome.Succeeded
                ? $"@{viewer.Login} left Bingo."
                : ResultMessage(result),
            ct
        );
    }

    private async Task<HostReference?> FindEnabledHostAsync(string channel, CancellationToken ct)
    {
        var login = CommunityInput.NormalizeLogin(channel);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(value =>
                value.Login == login
                && (value.EnabledFeatures & HostFeatureFlags.Bingo) == HostFeatureFlags.Bingo
            )
            .Select(value => new HostReference(value.Id, value.Login))
            .SingleOrDefaultAsync(ct);
    }

    private static BingoViewer? Viewer(ChatMessage message) =>
        message.Tags.TryGetValue("user-id", out var userId) && !string.IsNullOrWhiteSpace(userId)
            ? new(
                userId,
                message.Login,
                message.Tags.GetValueOrDefault("display-name", message.Login)
            )
            : null;

    private static Guid? OperationId(ChatMessage message) =>
        message.Tags.TryGetValue("id", out var messageId) && !string.IsNullOrWhiteSpace(messageId)
            ? StableOperationId(messageId)
            : null;

    private static Guid StableOperationId(string value) =>
        Guid.TryParse(value, out var parsed)
            ? parsed
            : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));

    private static string ResultMessage(BingoOperationOutcome result) =>
        result switch
        {
            BingoOperationOutcome.FeatureDisabled => "Bingo is turned off.",
            BingoOperationOutcome.Frozen => "Bingo joining is closed.",
            BingoOperationOutcome.Invalid invalid => invalid.Message,
            BingoOperationOutcome.Conflict conflict => conflict.Message,
            _ => "Bingo is unavailable.",
        };

    private sealed record HostReference(int Id, string Login);
}
