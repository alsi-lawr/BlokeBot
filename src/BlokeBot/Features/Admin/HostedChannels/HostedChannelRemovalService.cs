using BlokeBot.Eventing;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Admin.HostedChannels;

public sealed class HostedChannelRemovalService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    EventBus<AppEventKind> events
)
{
    public async Task<bool> RemoveAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var hostExists = await db.Hosts.AnyAsync(host => host.Id == hostId, ct);
        if (!hostExists)
            return false;

        var hostRounds = db.Rounds.Where(round =>
            round.GuessRoundProfile != null && round.GuessRoundProfile.HostId == hostId
        );

        await db
            .Votes.Where(vote => hostRounds.Select(round => round.Id).Contains(vote.GuessRoundId))
            .ExecuteDeleteAsync(ct);
        await hostRounds.ExecuteDeleteAsync(ct);
        await db
            .ReplySettings.Where(settings =>
                settings.GuessRoundProfile != null && settings.GuessRoundProfile.HostId == hostId
            )
            .ExecuteDeleteAsync(ct);
        await db
            .GuessOptions.Where(option =>
                option.GuessRoundProfile != null && option.GuessRoundProfile.HostId == hostId
            )
            .ExecuteDeleteAsync(ct);
        await db.CommandAliases.Where(alias => alias.HostId == hostId).ExecuteDeleteAsync(ct);
        await db.Profiles.Where(profile => profile.HostId == hostId).ExecuteDeleteAsync(ct);
        await db.Hosts.Where(host => host.Id == hostId).ExecuteDeleteAsync(ct);

        await events.PublishAsync(AppEventKind.HostedChannelsChanged);
        return true;
    }
}
