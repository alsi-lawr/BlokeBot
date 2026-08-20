using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed partial class CustomCommandConfigurationTransferAdapter
{
    private async Task<CustomCommandConfiguration> LoadDraftAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    )
    {
        var replies = await db
            .CustomMessageLibraryEntries.Include(x => x.Variants)
            .Where(x => x.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        var counters = await db
            .CustomCounters.Where(x => x.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        var commands = await db
            .CustomCommands.AsSplitQuery()
            .Include(x => x.Action)
            .Include(x => x.Aliases)
            .Include(x => x.AllowedUsers)
            .Where(x => x.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        var announcements = await db
            .CustomAnnouncements.Include(x => x.Schedule)
            .Include(x => x.DeliveryPolicy)
            .Where(x => x.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        var timeZone = await db
            .Hosts.Where(x => x.Id == hostId)
            .Select(x => x.TimeZoneId)
            .SingleAsync(cancellationToken);
        var projectionReference = timeProvider.GetUtcNow();
        var projectionTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        return new()
        {
            TimeZoneId = timeZone,
            ProjectionReferenceUtc = projectionReference,
            MessageEntries = replies.Select(CustomCommandConfigurationMapper.ToEditor).ToList(),
            Counters = counters.Select(CustomCommandConfigurationMapper.ToEditor).ToList(),
            Commands = commands.Select(CustomCommandConfigurationMapper.ToEditor).ToList(),
            Announcements = announcements
                .Select(x =>
                    CustomCommandConfigurationMapper.ToEditor(
                        x,
                        projectionTimeZone,
                        projectionReference
                    )
                )
                .ToList(),
        };
    }

    private static AnnouncementsSectionV1 SelectMissingAnnouncements(
        CustomCommandConfiguration draft,
        AnnouncementsSectionV1 imported
    )
    {
        var existingNames = draft
            .Announcements.Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = imported.Items.Where(x => !existingNames.Contains(x.Name)).ToArray();
        var replyIds = items.Select(x => x.MessageReplyId).ToHashSet(StringComparer.Ordinal);
        return new(imported.Replies.Where(x => replyIds.Contains(x.Id)).ToArray(), items);
    }

    private static IEnumerable<CustomCommandEditor> LoadRetainedCommandsByName(
        IEnumerable<CustomCommandEditor> commands,
        IReadOnlySet<string> names
    ) => commands.Where(command => names.Contains(command.Name));
}
