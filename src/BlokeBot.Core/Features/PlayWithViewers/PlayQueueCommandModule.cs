using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PlayWithViewers;

public sealed class PlayQueueCommandModule(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PlayQueueService queues
) : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands)
    {
        _ = commands.MapContextual(FixedChatCommandRoutes.Queue, StatusAsync);
        _ = commands.MapContextual(FixedChatCommandRoutes.Join, JoinAsync);
        _ = commands.MapContextual(FixedChatCommandRoutes.Leave, LeaveAsync);
        _ = commands.MapContextual(FixedChatCommandRoutes.Position, PositionAsync);
        _ = commands.MapContextual(FixedChatCommandRoutes.Ready, ReadyAsync);
        _ = commands.MapContextual(
            FixedChatCommandRoutes.QueueOpen,
            (context, args, ct) => SetOpenAsync(context, args, true, ct)
        );
        _ = commands.MapContextual(
            FixedChatCommandRoutes.QueueClose,
            (context, args, ct) => SetOpenAsync(context, args, false, ct)
        );
    }

    private async ValueTask<CommandHandlingOutcome> StatusAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var resolution = await ResolveAsync(context, args, ct);
        if (ResolutionOutcome(resolution) is { } outcome)
        {
            return outcome;
        }
        var resolved = ((QueueRouteResolution.Available)resolution).Queue;

        var page = await queues.GetPublicPageAsync(resolved.HostLogin, resolved.Queue.Slug, ct);
        if (page is null)
        {
            return new CommandHandlingOutcome.Handled();
        }

        await context.ReplyAsync(
            $"{page.Queue.Name} is {(page.Queue.IsOpen ? "open" : "closed")}: {page.Waiting.Count} waiting, {page.CurrentParty.Count}/{page.Queue.Capacity} in the party. {page.Queue.PriorityDescription} /queues/{page.Queue.HostLogin}/{page.Queue.Slug}",
            ct
        );
        return new CommandHandlingOutcome.Handled();
    }

    private async ValueTask<CommandHandlingOutcome> JoinAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var resolution = await ResolveAsync(context, args, ct);
        if (ResolutionOutcome(resolution) is { } outcome)
        {
            return outcome;
        }
        var resolved = ((QueueRouteResolution.Available)resolution).Queue;

        var values = resolved
            .RemainingArguments.Select(argument =>
                argument.Split('=', 2, StringSplitOptions.TrimEntries)
            )
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
        var result = await queues.JoinAsync(
            resolved.Queue.HostId,
            resolved.Queue.Slug,
            new JoinPlayQueueCommand(Identity(context.Message), 0, values),
            ct
        );
        await context.ReplyAsync(
            result.Match(
                succeeded =>
                    succeeded.WasIdempotent
                        ? $"You are already in {resolved.Queue.Name} at position {succeeded.Value.Position}."
                        : $"You joined {resolved.Queue.Name} at position {succeeded.Value.Position}.",
                rejected => rejected.Reason.Message
            ),
            ct
        );
        return new CommandHandlingOutcome.Handled();
    }

    private ValueTask<CommandHandlingOutcome> LeaveAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    ) => RespondViewerMutationAsync(context, args, "You left the queue.", queues.LeaveAsync, ct);

    private ValueTask<CommandHandlingOutcome> ReadyAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    ) => RespondViewerMutationAsync(context, args, "You are marked ready.", queues.ReadyAsync, ct);

    private async ValueTask<CommandHandlingOutcome> PositionAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var resolution = await ResolveAsync(context, args, ct);
        if (ResolutionOutcome(resolution) is { } outcome)
        {
            return outcome;
        }
        var resolved = ((QueueRouteResolution.Available)resolution).Queue;

        var result = await queues.GetPositionAsync(
            resolved.Queue.HostId,
            resolved.Queue.Slug,
            Identity(context.Message),
            ct
        );
        await context.ReplyAsync(
            result.Match(
                succeeded =>
                    succeeded.Value.Status
                    == BlokeBot.Persistence.Models.PlayQueueEntryStatus.Selected
                        ? "You are in the current party."
                        : $"You are position {succeeded.Value.Position} ({succeeded.Value.Status}).",
                rejected => rejected.Reason.Message
            ),
            ct
        );
        return new CommandHandlingOutcome.Handled();
    }

    private async ValueTask<CommandHandlingOutcome> SetOpenAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        bool open,
        CancellationToken ct
    )
    {
        var resolution = await ResolveAsync(context, args, ct);
        if (ResolutionOutcome(resolution) is { } outcome)
        {
            return outcome;
        }
        var resolved = ((QueueRouteResolution.Available)resolution).Queue;
        if (!ChatModeratorPolicy.IsModerator(context.Message))
        {
            await context.ReplyAsync("That queue command is moderator-only.", ct);
            return new CommandHandlingOutcome.Handled();
        }

        var result = await queues.SetOpenAsync(
            resolved.Queue.HostId,
            resolved.Queue.Slug,
            open,
            ct
        );
        await context.ReplyAsync(
            result.Match(
                _ => $"{resolved.Queue.Name} is now {(open ? "open" : "closed")}.",
                rejected => rejected.Reason.Message
            ),
            ct
        );
        return new CommandHandlingOutcome.Handled();
    }

    private async ValueTask<CommandHandlingOutcome> RespondViewerMutationAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        string successMessage,
        Func<
            int,
            string,
            PlayQueueViewerIdentity,
            CancellationToken,
            Task<PlayQueueResult<PublicPlayQueueEntryView>>
        > mutate,
        CancellationToken ct
    )
    {
        var resolution = await ResolveAsync(context, args, ct);
        if (ResolutionOutcome(resolution) is { } outcome)
        {
            return outcome;
        }
        var resolved = ((QueueRouteResolution.Available)resolution).Queue;

        var result = await mutate(
            resolved.Queue.HostId,
            resolved.Queue.Slug,
            Identity(context.Message),
            ct
        );
        await context.ReplyAsync(
            result.Match(_ => successMessage, rejected => rejected.Reason.Message),
            ct
        );
        return new CommandHandlingOutcome.Handled();
    }

    private async Task<QueueRouteResolution> ResolveAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostLogin = CommunityInput.NormalizeLogin(context.Message.Channel);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(value =>
                value.Login == hostLogin
                && (value.EnabledFeatures & HostFeatureFlags.PlayWithViewers)
                    == HostFeatureFlags.PlayWithViewers
            )
            .Select(value => new { value.Id, value.Login })
            .SingleOrDefaultAsync(ct);
        if (host is null)
        {
            return new QueueRouteResolution.Unavailable();
        }

        var available = await queues.GetQueuesForHostAsync(host.Id, ct);
        if (available.Count == 0)
        {
            await context.ReplyAsync("No play-with-viewers queue is configured.", ct);
            return new QueueRouteResolution.Handled();
        }

        PlayQueueSummary queue;
        var consumed = 0;
        if (available.Count == 1)
        {
            queue = available[0];
            if (args.Count > 0 && args[0].Equals(queue.Slug, StringComparison.OrdinalIgnoreCase))
            {
                consumed = 1;
            }
        }
        else
        {
            if (args.Count == 0)
            {
                await context.ReplyAsync(
                    $"Choose a queue: {string.Join(", ", available.Select(value => value.Slug))}.",
                    ct
                );
                return new QueueRouteResolution.Handled();
            }

            queue = available.FirstOrDefault(value =>
                value.Slug.Equals(args[0], StringComparison.OrdinalIgnoreCase)
            )!;
            if (queue is null)
            {
                await context.ReplyAsync("Queue not found.", ct);
                return new QueueRouteResolution.Handled();
            }

            consumed = 1;
        }

        return new QueueRouteResolution.Available(
            new ResolvedQueue(host.Login, queue, args.Skip(consumed).ToArray())
        );
    }

    private static CommandHandlingOutcome? ResolutionOutcome(QueueRouteResolution resolution) =>
        resolution switch
        {
            QueueRouteResolution.Unavailable => new CommandHandlingOutcome.Unhandled(),
            QueueRouteResolution.Handled => new CommandHandlingOutcome.Handled(),
            QueueRouteResolution.Available => null,
            _ => throw new ArgumentOutOfRangeException(nameof(resolution)),
        };

    private static PlayQueueViewerIdentity Identity(ChatMessage message)
    {
        _ = message.Tags.TryGetValue("user-id", out var userId);
        _ = message.Tags.TryGetValue("display-name", out var displayName);
        return new PlayQueueViewerIdentity(message.Login, userId, displayName);
    }

    private sealed record ResolvedQueue(
        string HostLogin,
        PlayQueueSummary Queue,
        IReadOnlyList<string> RemainingArguments
    );

    private abstract record QueueRouteResolution
    {
        private QueueRouteResolution() { }

        public sealed record Unavailable : QueueRouteResolution;

        public sealed record Handled : QueueRouteResolution;

        public sealed record Available(ResolvedQueue Queue) : QueueRouteResolution;
    }
}
