using BlokeBot.Eventing;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.CustomCommands;

public sealed class CustomCommandConfigurationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    CustomCommandAliasRegistry aliasRegistry,
    HostCustomCommandSettingsService hostSettings,
    EventBus<AppEventKind> events,
    TimeProvider clock
)
{
    private const int AliasMaxLength = 64;

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
            .Include(x => x.Action)
            .Include(x => x.Aliases)
            .Where(x => x.HostId == hostId)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var announcements = await db
            .CustomAnnouncements.AsNoTracking()
            .Include(x => x.Schedule)
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

        return new CustomCommandConfiguration
        {
            TimeZoneId = await hostSettings.GetTimeZoneIdAsync(hostId, ct),
            MessageEntries = messageEntries
                .Select(CustomCommandConfigurationMapper.ToEditor)
                .ToList(),
            Counters = counters.Select(CustomCommandConfigurationMapper.ToEditor).ToList(),
            Commands = commands.Select(CustomCommandConfigurationMapper.ToEditor).ToList(),
            Announcements = announcements
                .Select(CustomCommandConfigurationMapper.ToEditor)
                .ToList(),
            AlertSummary = new CustomCommandAlertSummary
            {
                ActiveCount = await alertQuery.CountAsync(ct),
                ActiveAlerts = activeAlerts,
            },
        };
    }

    public async Task SaveConfigurationAsync(
        int hostId,
        CustomCommandConfiguration config,
        CancellationToken ct
    )
    {
        var normalizedTimeZone = HostCustomCommandSettingsService.NormalizeTimeZoneId(
            config.TimeZoneId
        );
        CustomCommandConfigurationValidator.Validate(config);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var managedCommandIds = (
            await db
                .CustomCommands.AsNoTracking()
                .Where(x => x.HostId == hostId)
                .Select(x => x.Id)
                .ToArrayAsync(ct)
        ).ToHashSet();
        var normalizedAliases = await NormalizeAliasesAsync(
            db,
            hostId,
            managedCommandIds,
            config.Commands,
            ct
        );

        var graphWriter = new CustomCommandConfigurationGraphWriter(dbFactory, clock);
        await graphWriter.WriteAsync(hostId, config, normalizedAliases, ct);
        await hostSettings.SetTimeZoneIdAsync(hostId, normalizedTimeZone, ct);
        await events.PublishAsync(AppEventKind.CustomCommandsChanged);
    }

    private async Task<Dictionary<CustomCommandEditor, string[]>> NormalizeAliasesAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlySet<int> managedCommandIds,
        IEnumerable<CustomCommandEditor> commands,
        CancellationToken ct
    )
    {
        var normalized = new Dictionary<CustomCommandEditor, string[]>();
        foreach (var command in commands)
        {
            var aliases = await aliasRegistry.ValidateExcludingCommandsAsync(
                db,
                hostId,
                managedCommandIds,
                command.Aliases,
                ct
            );
            if (aliases.Any(alias => alias.Length > AliasMaxLength))
                throw new InvalidOperationException(
                    $"Custom command aliases cannot exceed {AliasMaxLength} characters."
                );

            normalized[command] = aliases;
        }

        var duplicate = normalized
            .SelectMany(pair => pair.Value.Select(alias => new { Alias = alias, pair.Key }))
            .GroupBy(x => x.Alias, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Select(x => x.Key).Distinct().Count() > 1)
            ?.Key;
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Alias !{duplicate} is already used by another custom command."
            );

        return normalized;
    }
}
