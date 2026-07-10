using System.Security.Cryptography;
using BlokeBot.Commands;
using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlokeBot.Features.CustomCommands;

public sealed class CustomCommandExecutionService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IOptions<BlokeBotOptions> options,
    CustomCommandCooldownStore cooldowns,
    CustomCommandTemplateRenderer templates,
    TimeProvider clock
)
{
    public async ValueTask<bool> TryExecuteAsync(
        TwitchCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostLogin = LoginName.Parse(context.Message.Channel).Value;
        var alias = CommandAliasNormalizer.Normalize(context.CommandName);
        if (hostLogin.Length == 0 || alias.Length == 0)
            return false;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == hostLogin)
            .Select(x => new { x.Id, x.EnabledFeatures })
            .SingleOrDefaultAsync(ct);
        if (host is null || !HasCustomCommands(host.EnabledFeatures))
            return false;

        var commandId = await db
            .CustomCommandAliases.AsNoTracking()
            .Where(x => x.HostId == host.Id && x.Alias == alias)
            .Select(x => (int?)x.CustomCommandId)
            .FirstOrDefaultAsync(ct);
        if (commandId is null)
            return false;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var command = await db
            .CustomCommands.Include(x => x.MessageLibraryEntry)
            .ThenInclude(x => x!.Variants)
            .Include(x => x.Counter)
            .SingleOrDefaultAsync(x => x.HostId == host.Id && x.Id == commandId.Value, ct);
        if (command is null || !command.Enabled)
            return true;

        if (command.ModeratorOnly && !TwitchModeratorPolicy.IsModerator(context.Message))
            return true;

        if (!cooldowns.TryRecord(command.Id, command.CooldownScope, context.Message.Login, Cooldown(command)))
            return true;

        var count = command.ActionType == CustomCommandActionType.Counter
            ? IncrementCounter(command)
            : null;
        if (command.ActionType == CustomCommandActionType.Counter && count is null)
            return true;

        var selectedMessage = SelectMessage(command.MessageLibraryEntry);
        if (selectedMessage is null)
            return true;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var reply = templates.Render(selectedMessage, context, args, count);
        await context.ReplyAsync(reply, ct);
        return true;
    }

    private TimeSpan Cooldown(CustomCommand command)
    {
        var seconds = Math.Max(
            Math.Max(0, command.CooldownSeconds),
            Math.Max(0, options.Value.CustomCommands.MinimumCooldownSeconds)
        );
        return TimeSpan.FromSeconds(seconds);
    }

    private long? IncrementCounter(CustomCommand command)
    {
        if (command.Counter is null)
            return null;

        command.Counter.Value++;
        command.Counter.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        return command.Counter.Value;
    }

    private string? SelectMessage(CustomMessageLibraryEntry? entry)
    {
        if (entry is null)
            return null;

        var variants = entry
            .Variants.OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToArray();
        if (variants.Length == 0)
            return null;

        return entry.SelectionMode switch
        {
            CustomMessageSelectionMode.First => variants[0].Text,
            CustomMessageSelectionMode.Random => variants[
                RandomNumberGenerator.GetInt32(variants.Length)
            ].Text,
            CustomMessageSelectionMode.Sequential => SelectSequentialMessage(entry, variants),
            _ => variants[0].Text,
        };
    }

    private string SelectSequentialMessage(
        CustomMessageLibraryEntry entry,
        CustomMessageVariant[] variants
    )
    {
        var index = entry.CurrentVariantIndex < 0 ? 0 : entry.CurrentVariantIndex;
        var selectedIndex = index % variants.Length;
        entry.CurrentVariantIndex = (selectedIndex + 1) % variants.Length;
        entry.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        return variants[selectedIndex].Text;
    }

    private static bool HasCustomCommands(HostFeatureFlags features) =>
        (features & HostFeatureFlags.CustomCommands) == HostFeatureFlags.CustomCommands;
}
