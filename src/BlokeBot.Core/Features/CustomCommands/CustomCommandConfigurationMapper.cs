using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

internal static partial class CustomCommandConfigurationMapper
{
    public static CustomMessageLibraryEntryEditor ToEditor(CustomMessageLibraryEntry entry) =>
        new()
        {
            Id = entry.Id,
            Name = entry.Name,
            SelectionMode = entry.SelectionMode,
            CurrentVariantIndex = entry.CurrentVariantIndex,
            Variants = entry
                .Variants.OrderBy(static x => x.SortOrder)
                .ThenBy(static x => x.Id)
                .Select(static x => new CustomMessageVariantEditor { Id = x.Id, Text = x.Text })
                .ToList(),
        };

    public static CustomCounterEditor ToEditor(CustomCounter counter) =>
        new()
        {
            Id = counter.Id,
            Name = counter.Name,
            Value = counter.Value,
        };
}
