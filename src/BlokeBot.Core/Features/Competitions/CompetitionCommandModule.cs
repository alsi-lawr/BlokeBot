using System.Security.Cryptography;
using System.Text;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Competitions;

internal sealed class CompetitionCommandModule(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    CompetitionService competitions
) : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands)
    {
        _ = commands.Map(FixedChatCommandRoutes.Competitions, ViewAsync);
        _ = commands.Map(FixedChatCommandRoutes.CompetitionJoin, JoinAsync);
    }

    private async ValueTask ViewAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var host = await EnabledHostAsync(context.Message.Channel, ct);
        if (host is null)
        {
            return;
        }
        var board = await competitions.GetPublicAsync(host.Login, ct);
        var current = board?.Active.FirstOrDefault();
        await context.ReplyAsync(
            current is null
                ? "No viewer competition is open right now."
                : $"{current.Name}: {current.Status.Label()} · {current.Format.Label()}. /competitions/{host.Login}",
            ct
        );
    }

    private async ValueTask JoinAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var host = await EnabledHostAsync(context.Message.Channel, ct);
        if (
            host is null
            || !context.Message.Tags.TryGetValue("user-id", out var userId)
            || string.IsNullOrWhiteSpace(userId)
        )
        {
            return;
        }
        var board = await competitions.GetPublicAsync(host.Login, ct);
        var competition = board?.Active.FirstOrDefault(x =>
            x.Status == CompetitionStatus.Registration
            && x.EntryKind == CompetitionEntryKind.Individual
        );
        if (competition is null)
        {
            await context.ReplyAsync("Individual competition registration is closed.", ct);
            return;
        }
        var login = CommunityInput.NormalizeLogin(context.Message.Login);
        var displayName = context.Message.Tags.GetValueOrDefault("display-name", login);
        var messageId = context.Message.Tags.GetValueOrDefault(
            "id",
            $"{host.Id}:{competition.Id.Value}:{userId}"
        );
        var outcome = await competitions.RegisterAsync(
            host.Id,
            new(
                StableOperationId(messageId),
                competition.Id,
                displayName,
                null,
                [new(userId, login, displayName, string.Empty)],
                new(userId, login),
                "Viewer chat registration"
            ),
            ct
        );
        await context.ReplyAsync(
            outcome is CompetitionOutcome.Succeeded
                ? $"@{login} joined {competition.Name}."
                : outcome switch
                {
                    CompetitionOutcome.Conflict conflict => conflict.Message,
                    CompetitionOutcome.Invalid invalid => invalid.Message,
                    _ => "Competition registration is unavailable.",
                },
            ct
        );
    }

    private async Task<HostReference?> EnabledHostAsync(string channel, CancellationToken ct)
    {
        var login = CommunityInput.NormalizeLogin(channel);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(x =>
                x.Login == login
                && (x.EnabledFeatures & HostFeatureFlags.Competitions)
                    == HostFeatureFlags.Competitions
            )
            .Select(x => new HostReference(x.Id, x.Login))
            .SingleOrDefaultAsync(ct);
    }

    private static Guid StableOperationId(string value) =>
        Guid.TryParse(value, out var parsed)
            ? parsed
            : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));

    private sealed record HostReference(int Id, string Login);
}
