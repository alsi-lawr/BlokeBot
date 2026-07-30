using System.Diagnostics;
using BlokeBot.Commands;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Persistence;
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
        commands.Map("moment", CaptureAsync);
        commands.Map("clip", CaptureAsync);
    }

    private async ValueTask CaptureAsync(
        ChatCommandContext context,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        var hostLogin = MomentInput.NormalizeLogin(context.Message.Channel);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Login == hostLogin)
            .Select(value => (int?)value.Id)
            .SingleOrDefaultAsync(ct);
        if (hostId is null)
        {
            return;
        }
        var streamResult = await streams.GetStreamLiveness(hostLogin).ExecuteAsync(ct);
        var stream = streamResult.Match(value => value, _ => throw new UnreachableException());
        if (stream is HostStreamLivenessOutcome.Offline)
        {
            await context.ReplyAsync("Moments can only be captured while the channel is live.", ct);
            return;
        }
        if (stream is not HostStreamLivenessOutcome.Live live)
        {
            await context.ReplyAsync("Twitch stream identity is temporarily unavailable.", ct);
            return;
        }

        var text = string.Join(' ', args);
        var sections = text.Split('|', 2, StringSplitOptions.TrimEntries);
        var category =
            sections.Length == 2
            && sections[1].StartsWith("category=", StringComparison.OrdinalIgnoreCase)
                ? sections[1]["category=".Length..].Trim()
                : string.Empty;
        context.Message.Tags.TryGetValue("user-id", out var userId);
        context.Message.Tags.TryGetValue("display-name", out var displayName);
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
                        ? $"Added your capture to moment {succeeded.Value.PublicId:N}."
                        : $"Captured moment {succeeded.Value.PublicId:N} for moderator review.",
                rejected => rejected.Reason.Message
            ),
            ct
        );
    }
}
