using System.Collections.Immutable;
using System.Security.Cryptography;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.CustomCommands;

public sealed record CustomMessageSelectionSnapshot
{
    internal CustomMessageSelectionSnapshot(
        CustomMessageSelectionMode selectionMode,
        int currentVariantIndex,
        IEnumerable<string> variants
    )
    {
        SelectionMode = selectionMode;
        CurrentVariantIndex = currentVariantIndex;
        Variants = variants.ToImmutableArray();
    }

    public CustomMessageSelectionMode SelectionMode { get; }

    public int CurrentVariantIndex { get; }

    public ImmutableArray<string> Variants { get; }
}

public sealed record CustomMessageSelectionResult(string Text, int NextVariantIndex);

public sealed class CustomMessageSelector
{
    public Option<CustomMessageSelectionResult> Select(CustomMessageSelectionSnapshot snapshot)
    {
        if (snapshot.Variants.IsEmpty)
        {
            return Option<CustomMessageSelectionResult>.None;
        }

        var currentIndex = Math.Max(0, snapshot.CurrentVariantIndex) % snapshot.Variants.Length;
        var selectedIndex = snapshot.SelectionMode switch
        {
            CustomMessageSelectionMode.First => 0,
            CustomMessageSelectionMode.Random => RandomNumberGenerator.GetInt32(
                snapshot.Variants.Length
            ),
            CustomMessageSelectionMode.Sequential => currentIndex,
            _ => throw new PersistenceDataIntegrityException(typeof(CustomMessageSelectionMode)),
        };
        var nextIndex =
            snapshot.SelectionMode is CustomMessageSelectionMode.Sequential
                ? (selectedIndex + 1) % snapshot.Variants.Length
                : currentIndex;
        return Option<CustomMessageSelectionResult>.Some(
            new CustomMessageSelectionResult(snapshot.Variants[selectedIndex], nextIndex)
        );
    }
}
