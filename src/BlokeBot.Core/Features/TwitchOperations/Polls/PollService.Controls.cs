using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations.Polls;

public sealed partial class PollService
{
    public async Task<PollControlState> ReadControlAsync(
        int hostId,
        int templateId,
        CancellationToken cancellationToken
    )
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, HostFeatureFlags.Polls, cancellationToken))
        {
            return new(new PollAuthorizationReadiness.Disabled(), null, null);
        }
        var readiness = await ReadinessAsync(hostId, cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var template = await db
            .TwitchPollTemplates.AsNoTracking()
            .Include(x => x.Choices)
            .SingleOrDefaultAsync(x => x.HostId == hostId && x.Id == templateId, cancellationToken);
        var active = await db
            .TwitchPolls.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.HostId == hostId && x.Status == TwitchPollStatus.Active,
                cancellationToken
            );
        return new(
            readiness,
            template is null ? null : View(template),
            active is null ? null : View(active)
        );
    }
}
