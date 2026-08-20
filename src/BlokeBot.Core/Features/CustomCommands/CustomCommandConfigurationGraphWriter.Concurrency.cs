using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed partial class CustomCommandConfigurationGraphWriter
{
    private static void ApplyFinalFields(
        CustomCommandConfigurationSaveCommand command,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters,
        IReadOnlyDictionary<int, CustomCommand> commands,
        IReadOnlyDictionary<int, CustomAnnouncement> announcements,
        DateTime now
    )
    {
        foreach (var configured in command.MessageEntries)
        {
            var entry = messageEntries[configured.Id];
            entry.Name = configured.Name;
            entry.SelectionMode = configured.SelectionMode;
            entry.CurrentVariantIndex = configured.CurrentVariantIndex;
            entry.UpdatedAtUtc = now;
        }

        foreach (var configured in command.Counters)
        {
            var counter = counters[configured.Id];
            counter.Name = configured.Name;
            counter.Value = configured.Value;
            counter.UpdatedAtUtc = now;
        }

        foreach (var configured in command.Commands)
        {
            var stored = commands[configured.Id];
            stored.Name = configured.Name;
            stored.UpdatedAtUtc = now;
        }

        foreach (var configured in command.Announcements)
        {
            var stored = announcements[configured.Id];
            stored.Name = configured.Name;
            stored.UpdatedAtUtc = now;
        }
    }

    private static CustomCommandConfigurationSaveFailure? FindStaleEntity(
        CustomCommandConfigurationSaveCommand command,
        IReadOnlyList<CustomMessageLibraryEntry> messageEntries,
        IReadOnlyList<CustomCounter> counters,
        IReadOnlyList<CustomCommand> commands,
        IReadOnlyList<CustomAnnouncement> announcements
    )
    {
        if (
            HasMissingPositiveId(
                command.MessageEntries,
                messageEntries,
                static configured => configured.Id,
                static stored => stored.Id
            )
        )
        {
            return new CustomCommandConfigurationSaveFailure.StaleEntity("saved reply");
        }

        if (
            HasMissingPositiveId(
                command.Counters,
                counters,
                static configured => configured.Id,
                static stored => stored.Id
            )
        )
        {
            return new CustomCommandConfigurationSaveFailure.StaleEntity("counter");
        }

        var commandMissing = HasMissingPositiveId(
            command.Commands,
            commands,
            static configured => configured.Id,
            static stored => stored.Id
        );
        var announcementMissing = HasMissingPositiveId(
            command.Announcements,
            announcements,
            static configured => configured.Id,
            static stored => stored.Id
        );
        return commandMissing switch
        {
            true => new CustomCommandConfigurationSaveFailure.StaleEntity("command"),
            false when announcementMissing => new CustomCommandConfigurationSaveFailure.StaleEntity(
                "announcement"
            ),
            false => null,
        };
    }

    private static bool HasMissingPositiveId<TConfigured, TStored>(
        IEnumerable<TConfigured> configured,
        IEnumerable<TStored> stored,
        Func<TConfigured, int> configuredId,
        Func<TStored, int> storedId
    )
    {
        var storedIds = stored.Select(storedId).ToHashSet();
        return configured.Select(configuredId).Any(id => id > 0 && !storedIds.Contains(id));
    }

    private static string TemporaryName(string entityName, int editorId) =>
        $"__editing_{entityName}_{editorId}_{Guid.NewGuid():N}";
}
