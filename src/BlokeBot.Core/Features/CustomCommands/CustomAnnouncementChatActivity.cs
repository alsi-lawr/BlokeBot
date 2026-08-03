using BlokeBot.Core.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomAnnouncementChatActivity(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider clock
) : IChatMessageObserver
{
    public async ValueTask MessageReceivedAsync(
        ChatMessage message,
        CancellationToken cancellationToken
    )
    {
        var hostLogin = LoginName.Parse(message.Channel).Value;
        if (hostLogin.Length == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == hostLogin)
            .Select(x => new { x.Id, x.EnabledFeatures })
            .SingleOrDefaultAsync(cancellationToken);
        if (host is null || !HasCustomCommands(host.EnabledFeatures))
        {
            return;
        }

        var intervalAfterChatIds = db
            .CustomAnnouncementSchedules.OfType<IntervalAfterChatCustomAnnouncementSchedule>()
            .Where(x => x.HostId == host.Id)
            .Select(x => x.CustomAnnouncementId);
        var announcements = await db
            .CustomAnnouncements.Where(x =>
                x.HostId == host.Id && x.Enabled && intervalAfterChatIds.Contains(x.Id)
            )
            .ToListAsync(cancellationToken);
        if (announcements.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var announcement in announcements)
        {
            announcement.ChatMessagesSinceLastSent++;
            announcement.UpdatedAtUtc = now;
        }

        _ = await db.SaveChangesAsync(cancellationToken);
    }

    private static bool HasCustomCommands(HostFeatureFlags features) =>
        (features & HostFeatureFlags.CustomCommands) == HostFeatureFlags.CustomCommands;
}
