using BlokeBot.Core.Features.ConfigurationTransfer;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed partial class CustomCommandConfigurationTransferAdapter
{
    private static void RemapCommandReferences(
        IEnumerable<CustomCommandEditor> commands,
        IReadOnlyDictionary<CustomMessageLibraryEntryEditor, int> originalReplyIds,
        IReadOnlyDictionary<CustomCounterEditor, int> originalCounterIds
    )
    {
        var replyIds = originalReplyIds.ToDictionary(x => x.Value, x => x.Key.Id);
        var counterIds = originalCounterIds.ToDictionary(x => x.Value, x => x.Key.Id);
        foreach (var command in commands)
        {
            var routes = command.Action.ReplyRoutes;
            routes.ZeroArgumentMessageLibraryEntryId = Remap(
                routes.ZeroArgumentMessageLibraryEntryId,
                replyIds
            );
            routes.OneArgumentMessageLibraryEntryId = Remap(
                routes.OneArgumentMessageLibraryEntryId,
                replyIds
            );
            routes.TwoArgumentMessageLibraryEntryId = Remap(
                routes.TwoArgumentMessageLibraryEntryId,
                replyIds
            );
            if (command.Action is CounterCustomCommandActionEditor counter)
            {
                counter.CounterId = counterIds.GetValueOrDefault(
                    counter.CounterId,
                    counter.CounterId
                );
            }
        }
    }

    private static int? Remap(int? id, IReadOnlyDictionary<int, int> ids) =>
        id is { } value ? ids.GetValueOrDefault(value, value) : null;

    private static List<T> Merge<T>(
        List<T> current,
        List<T> imported,
        ImportConflictStrategy strategy,
        Func<T, string> name,
        Func<T, int> id,
        Action<T, int> preserveId
    )
        where T : class
    {
        var existing = current.ToDictionary(name, StringComparer.OrdinalIgnoreCase);
        var selected = strategy == ImportConflictStrategy.ReplaceSection ? [] : current.ToList();
        foreach (var item in imported)
        {
            if (existing.TryGetValue(name(item), out var match))
            {
                if (strategy == ImportConflictStrategy.AddMissing)
                {
                    continue;
                }

                preserveId(item, id(match));
                _ = selected.Remove(match);
            }
            selected.Add(item);
        }
        return selected;
    }

    private static int? Resolve(string? id, IReadOnlyDictionary<string, int> ids) =>
        id is null ? null : ids[id];
}
