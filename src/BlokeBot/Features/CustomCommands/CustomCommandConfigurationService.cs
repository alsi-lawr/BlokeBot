using System.Globalization;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
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
    private const int MessageVariantMaxLength = 500;
    private const int NameMaxLength = 128;

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
            .Include(x => x.Aliases)
            .Where(x => x.HostId == hostId)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var announcements = await db
            .CustomAnnouncements.AsNoTracking()
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
            MessageEntries = messageEntries.Select(ToEditor).ToList(),
            Counters = counters.Select(ToEditor).ToList(),
            Commands = commands.Select(ToEditor).ToList(),
            Announcements = announcements.Select(ToEditor).ToList(),
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
        ValidateEditorShape(config);

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

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var messageEntries = await db
            .CustomMessageLibraryEntries.Include(x => x.Variants)
            .Where(x => x.HostId == hostId)
            .ToListAsync(ct);
        var counters = await db.CustomCounters.Where(x => x.HostId == hostId).ToListAsync(ct);
        var commands = await db
            .CustomCommands.Include(x => x.Aliases)
            .Where(x => x.HostId == hostId)
            .ToListAsync(ct);
        var announcements = await db
            .CustomAnnouncements.Where(x => x.HostId == hostId)
            .ToListAsync(ct);

        ValidateExistingIds(config, messageEntries, counters, commands, announcements);

        var messageEntityByEditorId = await StageMessageEntriesAsync(
            db,
            hostId,
            config.MessageEntries,
            messageEntries,
            now,
            ct
        );
        var counterEntityByEditorId = await StageCountersAsync(
            db,
            hostId,
            config.Counters,
            counters,
            now,
            ct
        );
        var commandEntityByEditor = await StageCommandsAsync(
            db,
            hostId,
            config.Commands,
            commands,
            messageEntityByEditorId,
            counterEntityByEditorId,
            now,
            ct
        );
        var announcementEntityByEditor = await StageAnnouncementsAsync(
            db,
            hostId,
            config.Announcements,
            announcements,
            messageEntityByEditorId,
            now,
            ct
        );

        await DeleteRemovedDependentsAsync(db, config, commands, announcements, ct);
        await DeleteRemovedPrincipalsAsync(db, config, messageEntries, counters, ct);
        await ReplaceVariantsAsync(db, config.MessageEntries, messageEntityByEditorId, ct);
        await ReplaceAliasesAsync(db, hostId, commandEntityByEditor, normalizedAliases, ct);
        ApplyFinalFields(
            config,
            messageEntityByEditorId,
            counterEntityByEditorId,
            commandEntityByEditor,
            announcementEntityByEditor,
            now
        );
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

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

    private static async Task<Dictionary<int, CustomMessageLibraryEntry>> StageMessageEntriesAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomMessageLibraryEntryEditor> editors,
        IReadOnlyList<CustomMessageLibraryEntry> existing,
        DateTime now,
        CancellationToken ct
    )
    {
        var existingById = existing.ToDictionary(x => x.Id);
        var result = new Dictionary<int, CustomMessageLibraryEntry>();
        foreach (var editor in editors)
        {
            var entry =
                editor.Id > 0
                    ? existingById[editor.Id]
                    : new CustomMessageLibraryEntry
                    {
                        HostId = hostId,
                        CreatedAtUtc = now,
                    };
            if (editor.Id <= 0)
                db.CustomMessageLibraryEntries.Add(entry);

            entry.Name = TemporaryName("message", editor.Id);
            entry.SelectionMode = editor.SelectionMode;
            entry.CurrentVariantIndex = 0;
            entry.UpdatedAtUtc = now;
            result[editor.Id] = entry;
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    private static async Task<Dictionary<int, CustomCounter>> StageCountersAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomCounterEditor> editors,
        IReadOnlyList<CustomCounter> existing,
        DateTime now,
        CancellationToken ct
    )
    {
        var existingById = existing.ToDictionary(x => x.Id);
        var result = new Dictionary<int, CustomCounter>();
        foreach (var editor in editors)
        {
            var counter =
                editor.Id > 0
                    ? existingById[editor.Id]
                    : new CustomCounter { HostId = hostId, CreatedAtUtc = now };
            if (editor.Id <= 0)
                db.CustomCounters.Add(counter);

            counter.Name = TemporaryName("counter", editor.Id);
            counter.Value = editor.Value;
            counter.UpdatedAtUtc = now;
            result[editor.Id] = counter;
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    private static async Task<Dictionary<CustomCommandEditor, CustomCommand>> StageCommandsAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomCommandEditor> editors,
        IReadOnlyList<CustomCommand> existing,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters,
        DateTime now,
        CancellationToken ct
    )
    {
        var existingById = existing.ToDictionary(x => x.Id);
        var result = new Dictionary<CustomCommandEditor, CustomCommand>();
        foreach (var editor in editors)
        {
            var command =
                editor.Id > 0
                    ? existingById[editor.Id]
                    : new CustomCommand { HostId = hostId, CreatedAtUtc = now };
            if (editor.Id <= 0)
                db.CustomCommands.Add(command);

            command.Name = TemporaryName("command", editor.Id);
            command.Enabled = editor.Enabled;
            command.ModeratorOnly = editor.ModeratorOnly;
            command.CooldownSeconds = Math.Max(0, editor.CooldownSeconds);
            command.CooldownScope = editor.CooldownScope;
            command.ActionType = editor.ActionType;
            command.MessageLibraryEntryId = messageEntries[editor.MessageLibraryEntryId].Id;
            command.CounterId =
                editor.ActionType == CustomCommandActionType.Counter && editor.CounterId is { } id
                    ? counters[id].Id
                    : null;
            command.UpdatedAtUtc = now;
            result[editor] = command;
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    private static async Task<
        Dictionary<CustomAnnouncementEditor, CustomAnnouncement>
    > StageAnnouncementsAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<CustomAnnouncementEditor> editors,
        IReadOnlyList<CustomAnnouncement> existing,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        DateTime now,
        CancellationToken ct
    )
    {
        var existingById = existing.ToDictionary(x => x.Id);
        var result = new Dictionary<CustomAnnouncementEditor, CustomAnnouncement>();
        foreach (var editor in editors)
        {
            var announcement =
                editor.Id > 0
                    ? existingById[editor.Id]
                    : new CustomAnnouncement { HostId = hostId, CreatedAtUtc = now };
            if (editor.Id <= 0)
                db.CustomAnnouncements.Add(announcement);

            announcement.Name = TemporaryName("announcement", editor.Id);
            announcement.Enabled = editor.Enabled;
            announcement.MessageLibraryEntryId = messageEntries[editor.MessageLibraryEntryId].Id;
            announcement.ScheduleType = editor.ScheduleType;
            announcement.IntervalMinutes = Math.Max(1, editor.IntervalMinutes);
            announcement.RequiredChatMessages = Math.Max(0, editor.RequiredChatMessages);
            announcement.WeeklyDay =
                editor.ScheduleType == CustomAnnouncementScheduleType.Weekly
                    ? editor.WeeklyDay
                    : null;
            announcement.WeeklyTime =
                editor.ScheduleType == CustomAnnouncementScheduleType.Weekly
                    ? ParseWeeklyTime(editor.WeeklyTime)
                    : null;
            announcement.UpdatedAtUtc = now;
            result[editor] = announcement;
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    private static async Task DeleteRemovedDependentsAsync(
        BlokeBotDbContext db,
        CustomCommandConfiguration config,
        IReadOnlyList<CustomCommand> existingCommands,
        IReadOnlyList<CustomAnnouncement> existingAnnouncements,
        CancellationToken ct
    )
    {
        var retainedCommandIds = config.Commands.Where(x => x.Id > 0).Select(x => x.Id).ToHashSet();
        var retainedAnnouncementIds = config
            .Announcements.Where(x => x.Id > 0)
            .Select(x => x.Id)
            .ToHashSet();

        db.CustomCommands.RemoveRange(existingCommands.Where(x => !retainedCommandIds.Contains(x.Id)));
        db.CustomAnnouncements.RemoveRange(
            existingAnnouncements.Where(x => !retainedAnnouncementIds.Contains(x.Id))
        );
        await db.SaveChangesAsync(ct);
    }

    private static async Task DeleteRemovedPrincipalsAsync(
        BlokeBotDbContext db,
        CustomCommandConfiguration config,
        IReadOnlyList<CustomMessageLibraryEntry> existingMessageEntries,
        IReadOnlyList<CustomCounter> existingCounters,
        CancellationToken ct
    )
    {
        var retainedMessageEntryIds = config
            .MessageEntries.Where(x => x.Id > 0)
            .Select(x => x.Id)
            .ToHashSet();
        var retainedCounterIds = config.Counters.Where(x => x.Id > 0).Select(x => x.Id).ToHashSet();

        db.CustomCounters.RemoveRange(existingCounters.Where(x => !retainedCounterIds.Contains(x.Id)));
        db.CustomMessageLibraryEntries.RemoveRange(
            existingMessageEntries.Where(x => !retainedMessageEntryIds.Contains(x.Id))
        );
        await db.SaveChangesAsync(ct);
    }

    private static async Task ReplaceVariantsAsync(
        BlokeBotDbContext db,
        IReadOnlyList<CustomMessageLibraryEntryEditor> editors,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> entries,
        CancellationToken ct
    )
    {
        var entryIds = entries.Values.Select(x => x.Id).ToArray();
        var existingVariants = await db
            .CustomMessageVariants.Where(x => entryIds.Contains(x.CustomMessageLibraryEntryId))
            .ToListAsync(ct);
        db.CustomMessageVariants.RemoveRange(existingVariants);
        await db.SaveChangesAsync(ct);

        foreach (var editor in editors)
        {
            var entry = entries[editor.Id];
            for (var i = 0; i < editor.Variants.Count; i++)
            {
                db.CustomMessageVariants.Add(
                    new CustomMessageVariant
                    {
                        CustomMessageLibraryEntryId = entry.Id,
                        SortOrder = i,
                        Text = editor.Variants[i].Text.Trim(),
                    }
                );
            }
        }
    }

    private static async Task ReplaceAliasesAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyDictionary<CustomCommandEditor, CustomCommand> commands,
        IReadOnlyDictionary<CustomCommandEditor, string[]> normalizedAliases,
        CancellationToken ct
    )
    {
        if (commands.Count == 0)
        {
            var hostAliases = await db
                .CustomCommandAliases.Where(x => x.HostId == hostId)
                .ToListAsync(ct);
            db.CustomCommandAliases.RemoveRange(hostAliases);
            await db.SaveChangesAsync(ct);
            return;
        }

        var existingAliases = await db
            .CustomCommandAliases.Where(x => x.HostId == hostId)
            .ToListAsync(ct);
        db.CustomCommandAliases.RemoveRange(existingAliases);
        await db.SaveChangesAsync(ct);

        foreach (var pair in commands)
        {
            foreach (var alias in normalizedAliases[pair.Key])
            {
                db.CustomCommandAliases.Add(
                    new CustomCommandAlias
                    {
                        HostId = hostId,
                        CustomCommandId = pair.Value.Id,
                        Alias = alias,
                    }
                );
            }
        }
    }

    private static void ApplyFinalFields(
        CustomCommandConfiguration config,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters,
        IReadOnlyDictionary<CustomCommandEditor, CustomCommand> commands,
        IReadOnlyDictionary<CustomAnnouncementEditor, CustomAnnouncement> announcements,
        DateTime now
    )
    {
        foreach (var editor in config.MessageEntries)
        {
            var entry = messageEntries[editor.Id];
            entry.Name = RequiredName(editor.Name, "Message library entry");
            entry.SelectionMode = editor.SelectionMode;
            entry.CurrentVariantIndex = ClampVariantIndex(editor.CurrentVariantIndex, editor.Variants.Count);
            entry.UpdatedAtUtc = now;
        }

        foreach (var editor in config.Counters)
        {
            var counter = counters[editor.Id];
            counter.Name = RequiredName(editor.Name, "Counter");
            counter.Value = editor.Value;
            counter.UpdatedAtUtc = now;
        }

        foreach (var pair in commands)
        {
            pair.Value.Name = RequiredName(pair.Key.Name, "Custom command");
            pair.Value.UpdatedAtUtc = now;
        }

        foreach (var pair in announcements)
        {
            pair.Value.Name = RequiredName(pair.Key.Name, "Announcement");
            pair.Value.UpdatedAtUtc = now;
        }
    }

    private static void ValidateEditorShape(CustomCommandConfiguration config)
    {
        EnsureUniqueEditorIds(config.MessageEntries.Select(x => x.Id), "message library entries");
        EnsureUniqueEditorIds(config.Counters.Select(x => x.Id), "counters");
        EnsureUniqueEditorIds(config.Commands.Select(x => x.Id), "custom commands");
        EnsureUniqueEditorIds(config.Announcements.Select(x => x.Id), "announcements");
        EnsureUniqueNames(config.MessageEntries, x => x.Name, "message library entry");
        EnsureUniqueNames(config.Counters, x => x.Name, "counter");
        EnsureUniqueNames(config.Commands, x => x.Name, "custom command");
        EnsureUniqueNames(config.Announcements, x => x.Name, "announcement");

        var messageEntryIds = config.MessageEntries.Select(x => x.Id).ToHashSet();
        var counterIds = config.Counters.Select(x => x.Id).ToHashSet();
        foreach (var entry in config.MessageEntries)
        {
            RequiredName(entry.Name, "Message library entry");
            if (entry.Variants.Count == 0)
                throw new InvalidOperationException(
                    $"Message library entry '{entry.Name.Trim()}' needs at least one variant."
                );

            foreach (var variant in entry.Variants)
            {
                var text = variant.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException(
                        $"Message library entry '{entry.Name.Trim()}' has an empty variant."
                    );

                if (text.Length > MessageVariantMaxLength)
                    throw new InvalidOperationException(
                        $"Message variants cannot exceed {MessageVariantMaxLength} characters."
                    );
            }
        }

        foreach (var counter in config.Counters)
            RequiredName(counter.Name, "Counter");

        foreach (var command in config.Commands)
        {
            RequiredName(command.Name, "Custom command");
            if (!messageEntryIds.Contains(command.MessageLibraryEntryId))
                throw new InvalidOperationException(
                    $"Custom command '{command.Name.Trim()}' needs a message library entry."
                );

            if (command.CooldownSeconds < 0)
                throw new InvalidOperationException("Command cooldown seconds cannot be negative.");

            if (command.ActionType == CustomCommandActionType.Counter && command.CounterId is null)
                throw new InvalidOperationException(
                    $"Custom command '{command.Name.Trim()}' needs a counter."
                );

            if (command.CounterId is { } counterId && !counterIds.Contains(counterId))
                throw new InvalidOperationException(
                    $"Custom command '{command.Name.Trim()}' references a missing counter."
                );
        }

        foreach (var announcement in config.Announcements)
        {
            RequiredName(announcement.Name, "Announcement");
            if (!messageEntryIds.Contains(announcement.MessageLibraryEntryId))
                throw new InvalidOperationException(
                    $"Announcement '{announcement.Name.Trim()}' needs a message library entry."
                );

            if (announcement.IntervalMinutes < 1)
                throw new InvalidOperationException(
                    "Announcement interval minutes must be at least 1."
                );

            if (announcement.RequiredChatMessages < 0)
                throw new InvalidOperationException(
                    "Announcement required chat messages cannot be negative."
                );

            if (
                announcement.ScheduleType == CustomAnnouncementScheduleType.IntervalAfterChat
                && announcement.RequiredChatMessages == 0
            )
            {
                throw new InvalidOperationException(
                    "Interval-after-chat announcements need at least one required chat message."
                );
            }

            if (announcement.ScheduleType == CustomAnnouncementScheduleType.Weekly)
            {
                if (announcement.WeeklyDay is null)
                    throw new InvalidOperationException(
                        $"Weekly announcement '{announcement.Name.Trim()}' needs a day."
                    );

                _ = ParseWeeklyTime(announcement.WeeklyTime);
            }
        }
    }

    private static void ValidateExistingIds(
        CustomCommandConfiguration config,
        IReadOnlyList<CustomMessageLibraryEntry> messageEntries,
        IReadOnlyList<CustomCounter> counters,
        IReadOnlyList<CustomCommand> commands,
        IReadOnlyList<CustomAnnouncement> announcements
    )
    {
        EnsurePositiveIdsExist(
            config.MessageEntries.Select(x => x.Id),
            messageEntries.Select(x => x.Id).ToHashSet(),
            "Message library entry"
        );
        EnsurePositiveIdsExist(
            config.Counters.Select(x => x.Id),
            counters.Select(x => x.Id).ToHashSet(),
            "Counter"
        );
        EnsurePositiveIdsExist(
            config.Commands.Select(x => x.Id),
            commands.Select(x => x.Id).ToHashSet(),
            "Custom command"
        );
        EnsurePositiveIdsExist(
            config.Announcements.Select(x => x.Id),
            announcements.Select(x => x.Id).ToHashSet(),
            "Announcement"
        );
    }

    private static CustomMessageLibraryEntryEditor ToEditor(CustomMessageLibraryEntry entry) =>
        new()
        {
            Id = entry.Id,
            Name = entry.Name,
            SelectionMode = entry.SelectionMode,
            CurrentVariantIndex = entry.CurrentVariantIndex,
            Variants = entry
                .Variants.OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new CustomMessageVariantEditor { Id = x.Id, Text = x.Text })
                .ToList(),
        };

    private static CustomCounterEditor ToEditor(CustomCounter counter) =>
        new()
        {
            Id = counter.Id,
            Name = counter.Name,
            Value = counter.Value,
        };

    private static CustomCommandEditor ToEditor(CustomCommand command) =>
        new()
        {
            Id = command.Id,
            Name = command.Name,
            Aliases = string.Join(", ", command.Aliases.Select(x => x.Alias).Order()),
            Enabled = command.Enabled,
            ModeratorOnly = command.ModeratorOnly,
            CooldownSeconds = command.CooldownSeconds,
            CooldownScope = command.CooldownScope,
            ActionType = command.ActionType,
            MessageLibraryEntryId = command.MessageLibraryEntryId,
            CounterId = command.CounterId,
        };

    private static CustomAnnouncementEditor ToEditor(CustomAnnouncement announcement) =>
        new()
        {
            Id = announcement.Id,
            Name = announcement.Name,
            Enabled = announcement.Enabled,
            MessageLibraryEntryId = announcement.MessageLibraryEntryId,
            ScheduleType = announcement.ScheduleType,
            IntervalMinutes = announcement.IntervalMinutes,
            RequiredChatMessages = announcement.RequiredChatMessages,
            WeeklyDay = announcement.WeeklyDay,
            WeeklyTime = announcement.WeeklyTime?.ToString("HH:mm", CultureInfo.InvariantCulture)
                ?? string.Empty,
            LastSentAtUtc = announcement.LastSentAtUtc,
            ChatMessagesSinceLastSent = announcement.ChatMessagesSinceLastSent,
        };

    private static void EnsureUniqueEditorIds(IEnumerable<int> ids, string entityName)
    {
        var duplicate = ids.GroupBy(x => x).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Editor IDs for {entityName} must be unique."
            );
    }

    private static void EnsureUniqueNames<T>(
        IEnumerable<T> editors,
        Func<T, string> name,
        string entityName
    )
    {
        var duplicate = editors
            .Select(x => RequiredName(name(x), entityName))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1)
            ?.Key;
        if (!string.IsNullOrWhiteSpace(duplicate))
            throw new InvalidOperationException(
                $"A {entityName} named '{duplicate}' already exists."
            );
    }

    private static void EnsurePositiveIdsExist(
        IEnumerable<int> editorIds,
        IReadOnlySet<int> existingIds,
        string entityName
    )
    {
        var missingId = editorIds.Where(x => x > 0).FirstOrDefault(x => !existingIds.Contains(x));
        if (missingId != 0)
            throw new InvalidOperationException($"{entityName} {missingId} was not found.");
    }

    private static string RequiredName(string value, string entityName)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException($"{entityName} name is required.");

        if (trimmed.Length > NameMaxLength)
            throw new InvalidOperationException(
                $"{entityName} names cannot exceed {NameMaxLength} characters."
            );

        return trimmed;
    }

    private static TimeOnly? ParseWeeklyTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Weekly announcements need a time.");

        if (
            TimeOnly.TryParseExact(
                value.Trim(),
                ["HH:mm", "H:mm"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time
            )
        )
        {
            return time;
        }

        throw new InvalidOperationException("Weekly announcement time is invalid.");
    }

    private static int ClampVariantIndex(int index, int variantCount)
    {
        if (variantCount <= 0)
            return 0;

        return Math.Clamp(index, 0, variantCount - 1);
    }

    private static string TemporaryName(string entityName, int editorId) =>
        $"__editing_{entityName}_{editorId}_{Guid.NewGuid():N}";
}
