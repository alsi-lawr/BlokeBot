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
    CustomMessageSelector messageSelector,
    CustomCommandTemplateRenderer templates,
    TimeProvider clock
)
{
    public async ValueTask<CommandHandlingOutcome> ExecuteAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostLogin = LoginName.Parse(context.Message.Channel).Value;
        var alias = CommandAliasNormalizer.Normalize(context.CommandName);
        if (hostLogin.Length == 0 || alias.Length == 0)
        {
            return new CommandHandlingOutcome.Unhandled();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == hostLogin)
            .Select(x => new { x.Id, x.EnabledFeatures })
            .SingleOrDefaultAsync(ct);
        if (host is null || !HasCustomCommands(host.EnabledFeatures))
        {
            return new CommandHandlingOutcome.Unhandled();
        }

        var commandId = await db
            .CustomCommandAliases.AsNoTracking()
            .Where(x => x.HostId == host.Id && x.Alias == alias)
            .Select(x => (int?)x.CustomCommandId)
            .FirstOrDefaultAsync(ct);
        if (commandId is null)
        {
            return new CommandHandlingOutcome.Unhandled();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var command = await db
            .CustomCommands.Include(x => x.Action)
                .ThenInclude(x => x.MessageLibraryEntry)
                    .ThenInclude(x => x!.Variants)
            .SingleOrDefaultAsync(x => x.HostId == host.Id && x.Id == commandId.Value, ct);
        if (command is null || !command.Enabled)
        {
            return new CommandHandlingOutcome.Handled();
        }

        if (command.ModeratorOnly && !ChatModeratorPolicy.IsModerator(context.Message))
        {
            return new CommandHandlingOutcome.Handled();
        }

        if (
            !cooldowns.TryRecord(
                command.Id,
                command.CooldownScope,
                context.Message.Login,
                Cooldown(command)
            )
        )
        {
            return new CommandHandlingOutcome.Handled();
        }

        long? count = null;
        if (command.Action is CounterCustomCommandAction counterAction)
        {
            await db.Entry(counterAction).Reference(x => x.Counter).LoadAsync(ct);
            count = IncrementCounter(counterAction);
            if (count is null)
            {
                return new CommandHandlingOutcome.Handled();
            }
        }

        var selectedMessage = SelectMessage(command.Action.MessageLibraryEntry);
        if (selectedMessage is null)
        {
            return new CommandHandlingOutcome.Handled();
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var reply = templates.Render(selectedMessage, context, args, count);
        await context.ReplyAsync(reply, ct);
        return new CommandHandlingOutcome.Handled();
    }

    private TimeSpan Cooldown(CustomCommand command)
    {
        var seconds = Math.Max(
            Math.Max(0, command.CooldownSeconds),
            Math.Max(0, options.Value.CustomCommands.MinimumCooldownSeconds)
        );
        return TimeSpan.FromSeconds(seconds);
    }

    private string? SelectMessage(CustomMessageLibraryEntry? entry)
    {
        if (entry is null)
        {
            return null;
        }

        var snapshot = new CustomMessageSelectionSnapshot(
            entry.SelectionMode,
            entry.CurrentVariantIndex,
            entry.Variants.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => x.Text)
        );
        return messageSelector
            .Select(snapshot)
            .Match<string?>(
                selected =>
                {
                    if (entry.SelectionMode is CustomMessageSelectionMode.Sequential)
                    {
                        entry.CurrentVariantIndex = selected.NextVariantIndex;
                        entry.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
                    }

                    return selected.Text;
                },
                static () => null
            );
    }

    private long? IncrementCounter(CounterCustomCommandAction action)
    {
        if (action.Counter is null)
        {
            return null;
        }

        action.Counter.Value++;
        action.Counter.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        return action.Counter.Value;
    }

    private static bool HasCustomCommands(HostFeatureFlags features)
    {
        return (features & HostFeatureFlags.CustomCommands) == HostFeatureFlags.CustomCommands;
    }
}
