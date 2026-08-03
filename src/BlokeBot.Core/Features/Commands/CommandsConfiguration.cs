using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Commands;

public sealed record CommandsConfiguration(string Aliases, string? ConflictAlias);

public sealed record CommandsConfigurationSaveCommand(string Aliases);

public abstract record CommandsConfigurationSaveOutcome
{
    private CommandsConfigurationSaveOutcome() { }

    public sealed record Saved(CommandsConfiguration Configuration)
        : CommandsConfigurationSaveOutcome;

    public sealed record Unauthorized : CommandsConfigurationSaveOutcome;

    public sealed record HostNotFound : CommandsConfigurationSaveOutcome;

    public sealed record AliasTooLong(int MaximumLength) : CommandsConfigurationSaveOutcome;

    public sealed record AliasConflict(string Alias) : CommandsConfigurationSaveOutcome;
}

public sealed class CommandsConfigurationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    EventBus<AppEventKind> events
)
{
    private const int _maximumAliasLength = 64;

    public async Task<CommandsConfiguration> LoadAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
        {
            return new(string.Empty, null);
        }

        var aliases = await db
            .CommandAliases.AsNoTracking()
            .Where(x =>
                x.HostId == hostId
                && x.GuessRoundProfileId == null
                && x.Kind == AppCommandKind.Commands
            )
            .OrderBy(x => x.Alias)
            .Select(x => x.Alias)
            .ToArrayAsync(ct);
        return new(string.Join(", ", aliases), host.CommandsDefaultConflictAlias);
    }

    public async Task<CommandsConfigurationSaveOutcome> SaveAsync(
        AuthenticatedSession session,
        int hostId,
        CommandsConfigurationSaveCommand command,
        CancellationToken ct
    )
    {
        if (!CanConfigure(session, hostId))
        {
            return new CommandsConfigurationSaveOutcome.Unauthorized();
        }

        var aliases = CommandAliasNormalizer.Split(command.Aliases).ToArray();
        if (aliases.Any(alias => alias.Length > _maximumAliasLength))
        {
            return new CommandsConfigurationSaveOutcome.AliasTooLong(_maximumAliasLength);
        }

        var fixedCollision = FixedChatCommandRoutes.FindCollision(aliases);
        if (fixedCollision is not null)
        {
            return new CommandsConfigurationSaveOutcome.AliasConflict(fixedCollision);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
        {
            return new CommandsConfigurationSaveOutcome.HostNotFound();
        }

        var builtInCollision = await db
            .CommandAliases.AsNoTracking()
            .Where(x =>
                x.HostId == hostId && x.Kind != AppCommandKind.Commands && aliases.Contains(x.Alias)
            )
            .Select(x => x.Alias)
            .FirstOrDefaultAsync(ct);
        if (builtInCollision is not null)
        {
            return new CommandsConfigurationSaveOutcome.AliasConflict(builtInCollision);
        }

        var customCollision = await db
            .CustomCommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId && aliases.Contains(x.Alias))
            .Select(x => x.Alias)
            .FirstOrDefaultAsync(ct);
        if (customCollision is not null)
        {
            return new CommandsConfigurationSaveOutcome.AliasConflict(customCollision);
        }

        db.CommandAliases.RemoveRange(
            db.CommandAliases.Where(x =>
                x.HostId == hostId
                && x.GuessRoundProfileId == null
                && x.Kind == AppCommandKind.Commands
            )
        );
        db.CommandAliases.AddRange(
            aliases.Select(alias => new CommandAlias
            {
                HostId = hostId,
                Kind = AppCommandKind.Commands,
                Alias = alias,
            })
        );
        host.CommandsAliasesConfigured = true;
        host.CommandsDefaultConflictAlias = null;
        _ = await db.SaveChangesAsync(ct);
        _ = await events.PublishAsync(AppEventKind.CommandsChanged, ct);
        return new CommandsConfigurationSaveOutcome.Saved(
            new CommandsConfiguration(string.Join(", ", aliases), null)
        );
    }

    private static bool CanConfigure(AuthenticatedSession session, int hostId)
    {
        var selectedHost = session.State.Match<BotHostChoice?>(
            static _ => null,
            static selected => selected.Selection.Current,
            static _ => null
        );
        return selectedHost?.Id == hostId && session.CanManageSelectedHostConfig;
    }
}
