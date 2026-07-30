using BlokeBot.Commands;
using BlokeBot.Core.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Commands;

public sealed class ViewerCommandCatalogModule(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ViewerCommandCatalogService catalog
) : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands)
    {
        commands.MapDynamic(ExecuteAsync);
    }

    private async ValueTask<CommandHandlingOutcome> ExecuteAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostLogin = LoginName.Parse(context.Message.Channel).Value;
        var alias = CommandAliasNormalizer.Normalize(context.CommandName);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var configured = await db
            .CommandAliases.AsNoTracking()
            .AnyAsync(
                value =>
                    value.HostId
                        == db.Hosts.Where(host => host.Login == hostLogin)
                            .Select(host => host.Id)
                            .SingleOrDefault()
                    && value.GuessRoundProfileId == null
                    && value.Kind == AppCommandKind.Commands
                    && value.Alias == alias,
                ct
            );
        if (!configured)
        {
            return new CommandHandlingOutcome.Unhandled();
        }

        var snapshot = await catalog.LoadForChannelAsync(hostLogin, ct);
        var message =
            snapshot.Entries.Count == 0
                ? "No viewer commands are currently available."
                : $"Available viewer commands: {string.Join(", ", snapshot.Names)}.";
        await context.ReplyAsync(message, ct);
        return new CommandHandlingOutcome.Handled();
    }
}
