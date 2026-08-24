using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Moments;

public sealed class MomentCommandModule(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IHostStreamLivenessProvider streams,
    MomentHubService moments
) : IChatCommandModule
{
    public void AddCommands(IChatCommandBuilder commands)
    {
        _ = commands.MapContextual(FixedChatCommandRoutes.Moment, CaptureAsync);
        _ = commands.MapContextual(FixedChatCommandRoutes.Clip, CaptureAsync);
    }

    /// <summary>
    /// What chat sees for a first capture. Shared with the settings preview so the two cannot drift.
    /// </summary>
    internal static string CapturedReply(Guid publicId) =>
        $"Captured moment {publicId:N} for moderator review.";

    /// <summary>
    /// What chat sees when a capture lands inside the merge window of an existing moment.
    /// </summary>
    internal static string JoinedReply(Guid publicId) =>
        $"Added your capture to moment {publicId:N}.";

    private async ValueTask<CommandHandlingOutcome> CaptureAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostLogin = MomentInput.NormalizeLogin(context.Message.Channel);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await db
            .Hosts.AsNoTracking()
            .Where(value =>
                value.Login == hostLogin
                && (value.EnabledFeatures & HostFeatureFlags.Moments) == HostFeatureFlags.Moments
            )
            .Select(value => (int?)value.Id)
            .SingleOrDefaultAsync(ct);
        if (hostId is null)
        {
            return new CommandHandlingOutcome.Unhandled();
        }
        var streamResult = await streams.GetStreamLiveness(hostLogin).ExecuteAsync(ct);
        var stream = streamResult.Match(value => value, _ => throw new UnreachableException());
        if (stream is HostStreamLivenessOutcome.Offline)
        {
            await context.ReplyAsync("Moments can only be captured while the channel is live.", ct);
            return new CommandHandlingOutcome.Handled();
        }
        if (stream is not HostStreamLivenessOutcome.Live live)
        {
            await context.ReplyAsync("Twitch stream identity is temporarily unavailable.", ct);
            return new CommandHandlingOutcome.Handled();
        }

        var text = string.Join(' ', args);
        var sections = text.Split('|', 2, StringSplitOptions.TrimEntries);
        var category =
            sections.Length == 2
            && sections[1].StartsWith("category=", StringComparison.OrdinalIgnoreCase)
                ? sections[1]["category=".Length..].Trim()
                : string.Empty;
        _ = context.Message.Tags.TryGetValue("user-id", out var userId);
        _ = context.Message.Tags.TryGetValue("display-name", out var displayName);
        var result = await moments.CaptureAsync(
            hostId.Value,
            new CaptureMomentCommand(
                live.StreamId,
                new MomentViewerIdentity(context.Message.Login, userId, displayName),
                sections[0],
                category
            ),
            ct
        );
        await context.ReplyAsync(
            result.Match(
                succeeded =>
                    succeeded.WasIdempotent
                        ? JoinedReply(succeeded.Value.PublicId)
                        : CapturedReply(succeeded.Value.PublicId),
                rejected => rejected.Reason.Message
            ),
            ct
        );
        return new CommandHandlingOutcome.Handled();
    }
}
