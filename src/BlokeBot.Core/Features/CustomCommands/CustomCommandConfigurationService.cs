using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomCommandConfigurationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    CustomCommandAliasRegistry aliasRegistry,
    CustomCommandConfigurationGraphWriter graphWriter,
    HostCustomCommandSettingsService hostSettings,
    ITwitchAnnouncementReadinessProvider twitchAnnouncementAccess,
    EventBus<AppEventKind> events,
    TimeProvider timeProvider
)
{
    public async Task<CustomCommandConfiguration> LoadConfigurationAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var messageEntries = await db
            .CustomMessageLibraryEntries.AsNoTracking()
            .Include(x => x.Variants)
            .Where(x => x.HostId == hostId)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var counters = await db
            .CustomCounters.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var commands = await db
            .CustomCommands.AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Action)
            .Include(x => x.Aliases)
            .Include(x => x.AllowedUsers)
            .Where(x => x.HostId == hostId)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var announcements = await db
            .CustomAnnouncements.AsNoTracking()
            .Include(x => x.Schedule)
            .Include(x => x.DeliveryPolicy)
            .Where(x => x.HostId == hostId)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var alertQuery = db
            .DurableAlerts.AsNoTracking()
            .Where(x => x.HostId == hostId && x.AcknowledgedAtUtc == null);
        var activeAlerts = await alertQuery
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(5)
            .Select(x => new CustomCommandAlertEditor
            {
                Severity = x.Severity,
                Title = x.Title,
                Message = x.Message,
                LinkPath = x.LinkPath,
                CreatedAtUtc = x.CreatedAtUtc,
            })
            .ToListAsync(ct);
        var channelLogin = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Id == hostId)
            .Select(x => x.Login)
            .SingleOrDefaultAsync(ct);
        var twitchAnnouncementReadiness = string.IsNullOrWhiteSpace(channelLogin)
            ? new TwitchAnnouncementReadiness(
                TwitchAnnouncementAvailability.Unavailable,
                string.Empty
            )
            : await twitchAnnouncementAccess.GetReadinessAsync(channelLogin, ct);

        var timeZoneId = await hostSettings.GetTimeZoneIdAsync(hostId, ct);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var projectionReference = timeProvider.GetUtcNow();
        var builtInAliases = await aliasRegistry.ListBuiltInAliasesAsync(db, hostId, ct);
        return new CustomCommandConfiguration
        {
            TimeZoneId = timeZoneId,
            ProjectionReferenceUtc = projectionReference,
            MessageEntries = messageEntries
                .Select(CustomCommandConfigurationMapper.ToEditor)
                .ToList(),
            Counters = counters.Select(CustomCommandConfigurationMapper.ToEditor).ToList(),
            Commands = commands.Select(CustomCommandConfigurationMapper.ToEditor).ToList(),
            Announcements = announcements
                .Select(x =>
                    CustomCommandConfigurationMapper.ToEditor(x, timeZone, projectionReference)
                )
                .ToList(),
            BuiltInAliases = builtInAliases,
            TwitchAnnouncementReadiness = twitchAnnouncementReadiness,
            AlertSummary = new CustomCommandAlertSummary
            {
                ActiveCount = await alertQuery.CountAsync(ct),
                ActiveAlerts = activeAlerts,
            },
        };
    }

    public IO<
        CustomCommandConfigurationSaved,
        CustomCommandConfigurationSaveFailure
    > SaveConfiguration(int hostId, CustomCommandConfigurationSaveCommand command) =>
        IO<CustomCommandConfigurationSaved, CustomCommandConfigurationSaveFailure>.Create(ct =>
            ExecuteSaveAsync(hostId, command, ct)
        );

    private async ValueTask<
        Result<CustomCommandConfigurationSaved, CustomCommandConfigurationSaveFailure>
    > ExecuteSaveAsync(
        int hostId,
        CustomCommandConfigurationSaveCommand command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var managedCommandIds = (
            await db
                .CustomCommands.AsNoTracking()
                .Where(stored => stored.HostId == hostId)
                .Select(stored => stored.Id)
                .ToArrayAsync(ct)
        ).ToHashSet();
        foreach (var configured in command.Commands)
        {
            var conflict = await aliasRegistry.FindCustomSaveConflictAsync(
                db,
                hostId,
                managedCommandIds,
                configured.Aliases,
                ct
            );
            if (conflict is not null)
            {
                return Result<
                    CustomCommandConfigurationSaved,
                    CustomCommandConfigurationSaveFailure
                >.Error(AliasFailure(conflict));
            }
        }

        var commandForSave = await DisableUnavailableNativeAnnouncementsAsync(
            db,
            hostId,
            command,
            ct
        );
        var graphFailure = await graphWriter.WriteAsync(hostId, commandForSave, ct);
        if (graphFailure is not null)
        {
            return Result<
                CustomCommandConfigurationSaved,
                CustomCommandConfigurationSaveFailure
            >.Error(graphFailure);
        }

        await hostSettings.SetTimeZoneAsync(hostId, command.TimeZone, ct);
        _ = await events.PublishAsync(AppEventKind.CustomCommandsChanged, ct);
        return Result<
            CustomCommandConfigurationSaved,
            CustomCommandConfigurationSaveFailure
        >.Success(new());
    }

    private static CustomCommandConfigurationSaveFailure AliasFailure(
        CustomCommandAliasConflict conflict
    ) =>
        conflict.Match<CustomCommandConfigurationSaveFailure>(
            static builtIn => new CustomCommandConfigurationSaveFailure.BuiltInAliasCollision(
                builtIn.Alias
            ),
            static custom => new CustomCommandConfigurationSaveFailure.CustomAliasCollision(
                custom.Alias
            )
        );

    private async Task<CustomCommandConfigurationSaveCommand> DisableUnavailableNativeAnnouncementsAsync(
        BlokeBotDbContext db,
        int hostId,
        CustomCommandConfigurationSaveCommand command,
        CancellationToken ct
    )
    {
        if (
            !command.Announcements.Any(x =>
                x.Enabled && x.DeliveryType == CustomAnnouncementDeliveryType.TwitchAnnouncement
            )
        )
        {
            return command;
        }

        var channelLogin = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Id == hostId)
            .Select(x => x.Login)
            .SingleOrDefaultAsync(ct);
        var readiness = string.IsNullOrWhiteSpace(channelLogin)
            ? new TwitchAnnouncementReadiness(
                TwitchAnnouncementAvailability.Unavailable,
                string.Empty
            )
            : await twitchAnnouncementAccess.GetReadinessAsync(channelLogin, ct);
        return readiness.Availability == TwitchAnnouncementAvailability.Available
            ? command
            : new(
                command.TimeZone,
                command.MessageEntries,
                command.Commands,
                command.Counters,
                command.Announcements.Select(announcement =>
                    announcement.DeliveryType == CustomAnnouncementDeliveryType.TwitchAnnouncement
                        ? announcement with
                        {
                            Enabled = false,
                        }
                        : announcement
                )
            );
    }
}
