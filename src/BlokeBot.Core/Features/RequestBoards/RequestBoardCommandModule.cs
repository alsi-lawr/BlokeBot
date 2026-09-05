using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.RequestBoards;

public sealed class RequestBoardCommandModule(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    RequestBoardService boards
) : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands)
    {
        _ = commands.MapContextual(FixedChatCommandRoutes.Request, SubmitAsync);
        _ = commands.MapContextual(FixedChatCommandRoutes.Requests, ListAsync);
        _ = commands.MapContextual(FixedChatCommandRoutes.RequestVote, VoteAsync);
        _ = commands.MapContextual(
            FixedChatCommandRoutes.RequestApprove,
            (context, args, ct) =>
                ModerateAsync(context, args, RequestSubmissionStatus.Approved, ct)
        );
        _ = commands.MapContextual(
            FixedChatCommandRoutes.RequestReject,
            (context, args, ct) =>
                ModerateAsync(context, args, RequestSubmissionStatus.Rejected, ct)
        );
        _ = commands.MapContextual(
            FixedChatCommandRoutes.RequestQueue,
            (context, args, ct) => ModerateAsync(context, args, RequestSubmissionStatus.Queued, ct)
        );
        _ = commands.MapContextual(
            FixedChatCommandRoutes.RequestAccept,
            (context, args, ct) =>
                ModerateAsync(context, args, RequestSubmissionStatus.Accepted, ct)
        );
        _ = commands.MapContextual(
            FixedChatCommandRoutes.RequestComplete,
            (context, args, ct) =>
                ModerateAsync(context, args, RequestSubmissionStatus.Completed, ct)
        );
        _ = commands.MapContextual(FixedChatCommandRoutes.RequestMerge, MergeAsync);
    }

    private async ValueTask<CommandHandlingOutcome> SubmitAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostId = await FindHostIdAsync(context.Message.Channel, ct);
        if (hostId is null)
        {
            return new CommandHandlingOutcome.Unhandled();
        }

        if (args.Count < 2)
        {
            await context.ReplyAsync(
                "Usage: !request <board> <title> | field=value | category=value | tags=a,b",
                ct
            );
            return new CommandHandlingOutcome.Handled();
        }

        var actor = RequestActor.FromChatMessage(context.Message);
        if (actor is null)
        {
            await context.ReplyAsync("A verified Twitch identity is required.", ct);
            return new CommandHandlingOutcome.Handled();
        }

        var sections = string.Join(' ', args.Skip(1)).Split('|', StringSplitOptions.TrimEntries);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var category = string.Empty;
        IReadOnlyList<string> tags = [];
        foreach (var section in sections.Skip(1))
        {
            var separator = section.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = CommunityInput.NormalizeSlug(section[..separator]);
            var value = section[(separator + 1)..].Trim();
            if (key == "category")
            {
                category = value;
            }
            else if (key == "tags")
            {
                tags = RequestBoardInput.ParseTags(value);
            }
            else
            {
                values[key] = value;
            }
        }

        var outcome = await boards.SubmitAsync(
            hostId.Value,
            args[0],
            new SubmitRequestCommand(
                MessageOperationId(context.Message),
                actor,
                sections[0],
                category,
                tags,
                values
            ),
            ct
        );
        await context.ReplyAsync(
            outcome.Match(
                succeeded =>
                    succeeded.WasIdempotent
                        ? $"Request #{succeeded.Value.Id} was already received."
                        : $"Request #{succeeded.Value.Id} submitted for moderator review.",
                rejected => rejected.Reason.Message
            ),
            ct
        );
        return new CommandHandlingOutcome.Handled();
    }

    private async ValueTask<CommandHandlingOutcome> ListAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostId = await FindHostIdAsync(context.Message.Channel, ct);
        if (hostId is null)
        {
            return new CommandHandlingOutcome.Unhandled();
        }

        var boardList = await boards.GetBoardsForHostAsync(hostId.Value, ct);
        if (args.Count == 0)
        {
            var names =
                boardList.Count == 0
                    ? "No request boards are configured."
                    : $"Request boards: {string.Join(", ", boardList.Select(board => board.Slug))}.";
            await context.ReplyAsync(names, ct);
            return new CommandHandlingOutcome.Handled();
        }

        var board = boardList.SingleOrDefault(value =>
            string.Equals(
                value.Slug,
                CommunityInput.NormalizeSlug(args[0]),
                StringComparison.Ordinal
            )
        );
        await context.ReplyAsync(
            board is null
                ? "Request board not found."
                : $"{board.Title} is {(board.IsOpen ? "open" : "closed")}. Voting: {(board.VotingEnabled ? $"up to {board.VoteLimitPerUser} per viewer" : "off")}. Order: {board.OrderingDescription} /requests/{board.HostLogin}/{board.Slug}",
            ct
        );
        return new CommandHandlingOutcome.Handled();
    }

    private async ValueTask<CommandHandlingOutcome> VoteAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostId = await FindHostIdAsync(context.Message.Channel, ct);
        if (hostId is null)
        {
            return new CommandHandlingOutcome.Unhandled();
        }
        if (
            args.Count != 1
            || !long.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
        )
        {
            await context.ReplyAsync("Usage: !requestvote <request-id>", ct);
            return new CommandHandlingOutcome.Handled();
        }

        var actor = RequestActor.FromChatMessage(context.Message);
        if (actor is null)
        {
            await context.ReplyAsync("A verified Twitch identity is required.", ct);
            return new CommandHandlingOutcome.Handled();
        }

        var outcome = await boards.VoteAsync(hostId.Value, id, actor, ct);
        await context.ReplyAsync(
            outcome.Match(
                succeeded =>
                    succeeded.WasIdempotent
                        ? $"You already voted for request #{id}."
                        : $"Vote recorded for request #{id}.",
                rejected => rejected.Reason.Message
            ),
            ct
        );
        return new CommandHandlingOutcome.Handled();
    }

    private async ValueTask<CommandHandlingOutcome> ModerateAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        RequestSubmissionStatus target,
        CancellationToken ct
    )
    {
        var hostId = await FindHostIdAsync(context.Message.Channel, ct);
        if (hostId is null)
        {
            return new CommandHandlingOutcome.Unhandled();
        }

        if (!ChatModeratorPolicy.IsModerator(context.Message))
        {
            await context.ReplyAsync("That request-board command is moderator-only.", ct);
            return new CommandHandlingOutcome.Handled();
        }

        if (
            args.Count != 1
            || !long.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
        )
        {
            await context.ReplyAsync(
                $"Usage: !request{target.ToString().ToLowerInvariant()} <request-id>",
                ct
            );
            return new CommandHandlingOutcome.Handled();
        }

        var current = await boards.GetModeratorSubmissionAsync(hostId.Value, id, ct);
        if (current is null)
        {
            await context.ReplyAsync("Request not found.", ct);
            return new CommandHandlingOutcome.Handled();
        }

        var outcome = await boards.ModerateAsync(
            hostId.Value,
            new ModerateRequestCommand(
                id,
                target,
                current.Public.PublicNote,
                current.PrivateModeratorNote,
                current.PrivateRejectionReason,
                current.Public.Priority,
                current.Public.Category,
                current.Public.Tags
            ),
            ct
        );
        await context.ReplyAsync(
            outcome.Match(
                _ => $"Request #{id} is now {target.ToString().ToLowerInvariant()}.",
                rejected => rejected.Reason.Message
            ),
            ct
        );
        return new CommandHandlingOutcome.Handled();
    }

    private async ValueTask<CommandHandlingOutcome> MergeAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostId = await FindHostIdAsync(context.Message.Channel, ct);
        if (hostId is null)
        {
            return new CommandHandlingOutcome.Unhandled();
        }

        if (!ChatModeratorPolicy.IsModerator(context.Message))
        {
            await context.ReplyAsync("That request-board command is moderator-only.", ct);
            return new CommandHandlingOutcome.Handled();
        }

        if (
            args.Count != 2
            || !long.TryParse(
                args[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var source
            )
            || !long.TryParse(
                args[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var target
            )
        )
        {
            await context.ReplyAsync("Usage: !requestmerge <source-id> <target-id>", ct);
            return new CommandHandlingOutcome.Handled();
        }

        var outcome = await boards.MergeAsync(
            hostId.Value,
            source,
            target,
            $"Merged into request #{target}.",
            $"Merged by @{context.Message.Login} through chat.",
            ct
        );
        await context.ReplyAsync(
            outcome.Match(
                _ => $"Request #{source} was merged into #{target}.",
                rejected => rejected.Reason.Message
            ),
            ct
        );
        return new CommandHandlingOutcome.Handled();
    }

    private async Task<int?> FindHostIdAsync(string channel, CancellationToken ct)
    {
        var login = CommunityInput.NormalizeLogin(channel);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(value =>
                value.Login == login
                && (value.EnabledFeatures & HostFeatureFlags.RequestBoards)
                    == HostFeatureFlags.RequestBoards
            )
            .Select(value => (int?)value.Id)
            .SingleOrDefaultAsync(ct);
    }

    private static Guid MessageOperationId(ChatMessage message)
    {
        if (
            message.Tags.TryGetValue("id", out var messageId)
            && Guid.TryParse(messageId, out var parsed)
        )
        {
            return parsed;
        }

        if (!string.IsNullOrWhiteSpace(messageId))
        {
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(messageId));
            return new Guid(digest.AsSpan(0, 16));
        }

        return Guid.NewGuid();
    }
}
