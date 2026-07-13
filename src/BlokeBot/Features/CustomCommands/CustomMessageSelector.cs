using System.Security.Cryptography;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.CustomCommands;

public sealed class CustomMessageSelector(TimeProvider clock)
{
    public string? SelectMessage(CustomMessageLibraryEntry? entry)
    {
        if (entry is null)
        {
            return null;
        }

        var variants = entry.Variants.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToArray();
        if (variants.Length == 0)
        {
            return null;
        }

        return entry.SelectionMode switch
        {
            CustomMessageSelectionMode.First => variants[0].Text,
            CustomMessageSelectionMode.Random => variants[
                RandomNumberGenerator.GetInt32(variants.Length)
            ].Text,
            CustomMessageSelectionMode.Sequential => SelectSequentialMessage(entry, variants),
            _ => variants[0].Text,
        };
    }

    private string SelectSequentialMessage(
        CustomMessageLibraryEntry entry,
        CustomMessageVariant[] variants
    )
    {
        var index = entry.CurrentVariantIndex < 0 ? 0 : entry.CurrentVariantIndex;
        var selectedIndex = index % variants.Length;
        entry.CurrentVariantIndex = (selectedIndex + 1) % variants.Length;
        entry.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        return variants[selectedIndex].Text;
    }
}
