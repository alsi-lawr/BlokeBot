using System.Diagnostics;
using BlokeBot.Commands;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Identity;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomCommandExecutionService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IOptions<BlokeBotOptions> options,
    CustomCommandCooldownStore cooldowns,
    CustomMessageSelector messageSelector,
    CustomCommandTemplateRenderer templates,
    CustomCommandInvocationClaimStore claims,
    IHostStreamLivenessProvider streams,
    TimeProvider clock
)
{
    public async ValueTask<CustomCommandExecutionOutcome> ExecuteAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostLogin = LoginName.Parse(context.Message.Channel).Value;
        var alias = CommandAliasNormalizer.Normalize(context.CommandName);
        if (hostLogin.Length == 0 || alias.Length == 0)
        {
            return new CustomCommandExecutionOutcome.Unhandled();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == hostLogin)
            .Select(x => new { x.Id, x.EnabledFeatures })
            .SingleOrDefaultAsync(ct);
        if (host is null || !HasCustomCommands(host.EnabledFeatures))
        {
            return new CustomCommandExecutionOutcome.Unhandled();
        }

        var commandId = await db
            .CustomCommandAliases.AsNoTracking()
            .Where(x => x.HostId == host.Id && x.Alias == alias)
            .Select(x => (int?)x.CustomCommandId)
            .FirstOrDefaultAsync(ct);
        if (commandId is null)
        {
            return new CustomCommandExecutionOutcome.Unhandled();
        }

        var command = await db
            .CustomCommands.Include(x => x.Action)
                .ThenInclude(x => x.MessageLibraryEntry)
                    .ThenInclude(x => x!.Variants)
            .SingleOrDefaultAsync(x => x.HostId == host.Id && x.Id == commandId.Value, ct);
        if (command is null || !command.Enabled)
        {
            return new CustomCommandExecutionOutcome.Handled();
        }

        if (command.ModeratorOnly && !ChatModeratorPolicy.IsModerator(context.Message))
        {
            return new CustomCommandExecutionOutcome.Handled();
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
            return new CustomCommandExecutionOutcome.Cooldown();
        }

        var streamId = await StreamIdAsync(command.InvocationLimit, hostLogin, ct);
        if (streamId is StreamIdentity.Offline)
        {
            return new CustomCommandExecutionOutcome.StreamOffline();
        }

        if (streamId is StreamIdentity.Unavailable unavailable)
        {
            return new CustomCommandExecutionOutcome.StreamUnavailable(unavailable.Failure);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var claim =
            command.InvocationLimit == CustomCommandInvocationLimit.Unlimited
                ? new CustomCommandInvocationClaimOutcome.Claimed()
                : await claims.TryClaimAsync(
                    db,
                    ClaimRequest(host.Id, command, context.Message, streamId),
                    ct
                );
        if (claim is CustomCommandInvocationClaimOutcome.AlreadyUsed)
        {
            return new CustomCommandExecutionOutcome.AlreadyUsed();
        }

        long? count = null;
        if (command.Action is CounterCustomCommandAction counterAction)
        {
            await db.Entry(counterAction).Reference(x => x.Counter).LoadAsync(ct);
            count = IncrementCounter(counterAction);
            if (count is null)
            {
                return new CustomCommandExecutionOutcome.Handled();
            }
        }

        var selectedMessage = SelectMessage(command.Action.MessageLibraryEntry);
        if (selectedMessage is null)
        {
            return new CustomCommandExecutionOutcome.Handled();
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var reply = templates.Render(selectedMessage, context, args, count);
        await context.ReplyAsync(reply, ct);
        return new CustomCommandExecutionOutcome.Handled();
    }

    private async Task<StreamIdentity> StreamIdAsync(
        CustomCommandInvocationLimit limit,
        string hostLogin,
        CancellationToken ct
    )
    {
        if (
            limit
            is not (
                CustomCommandInvocationLimit.OncePerStream
                or CustomCommandInvocationLimit.OncePerStreamPerUser
            )
        )
        {
            return new StreamIdentity.NotRequired();
        }

        var result = await streams.GetStreamLiveness(hostLogin).ExecuteAsync(ct);
        var outcome = result.Match(value => value, _ => throw new UnreachableException());
        return outcome switch
        {
            HostStreamLivenessOutcome.Live live => new StreamIdentity.Available(live.StreamId),
            HostStreamLivenessOutcome.Offline => new StreamIdentity.Offline(),
            HostStreamLivenessOutcome.Unavailable unavailable => new StreamIdentity.Unavailable(
                unavailable
            ),
            _ => throw new UnreachableException("Unknown stream-liveness outcome."),
        };
    }

    private static CustomCommandInvocationClaimRequest ClaimRequest(
        int hostId,
        CustomCommand command,
        ChatMessage message,
        StreamIdentity stream
    )
    {
        CustomCommandInvocationScope scope = command.InvocationLimit switch
        {
            CustomCommandInvocationLimit.OncePerStream =>
                new CustomCommandInvocationScope.OncePerStream(
                    ((StreamIdentity.Available)stream).StreamId
                ),
            CustomCommandInvocationLimit.OncePerUser =>
                new CustomCommandInvocationScope.OncePerUser(message.Tags["user-id"]),
            CustomCommandInvocationLimit.OncePerStreamPerUser =>
                new CustomCommandInvocationScope.OncePerStreamPerUser(
                    ((StreamIdentity.Available)stream).StreamId,
                    message.Tags["user-id"]
                ),
            _ => throw new UnreachableException("Unlimited commands do not create claims."),
        };
        return new(hostId, command.Id, scope);
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

    private abstract record StreamIdentity
    {
        private StreamIdentity() { }

        public sealed record NotRequired : StreamIdentity;

        public sealed record Available(string StreamId) : StreamIdentity;

        public sealed record Offline : StreamIdentity;

        public sealed record Unavailable(HostStreamLivenessOutcome.Unavailable Failure)
            : StreamIdentity;
    }
}
