using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.HostedChannels.Whispers;

public sealed class HostWhisperResponseTargetResolver(
    IDbContextFactory<BlokeBotDbContext> dbFactory
) : ITwitchCommandResponseTargetResolver
{
    public async ValueTask<TwitchCommandResponseTarget> ResolveAsync(
        TwitchCommandContext context,
        CancellationToken cancellationToken
    )
    {
        var channel = TwitchLogin.Normalize(context.Message.Channel);
        if (string.IsNullOrWhiteSpace(channel))
            return TwitchCommandResponseTarget.Chat;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var hostId = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == channel)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (hostId is not { } resolvedHostId)
            return TwitchCommandResponseTarget.Chat;

        var enabled = await db
            .HostBotAccountSettings.AsNoTracking()
            .Where(x => x.HostId == resolvedHostId)
            .Select(x => x.OverrideEnabled && x.WhisperResponsesEnabled)
            .SingleOrDefaultAsync(cancellationToken);

        return enabled ? TwitchCommandResponseTarget.Whisper : TwitchCommandResponseTarget.Chat;
    }
}
