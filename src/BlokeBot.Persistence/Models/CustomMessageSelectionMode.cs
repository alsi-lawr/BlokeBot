namespace BlokeBot.Persistence.Models;

public enum CustomMessageSelectionMode
{
    First,
    Sequential,
    Random,
}

public static class CustomMessageSelectionModeStore
{
    public static IReadOnlyList<string> Values { get; } =
    [
        Format(CustomMessageSelectionMode.First),
        Format(CustomMessageSelectionMode.Random),
        Format(CustomMessageSelectionMode.Sequential),
    ];

    public static string Format(CustomMessageSelectionMode mode) => mode.ToString();

    public static CustomMessageSelectionMode Parse(string value) =>
        Enum.TryParse<CustomMessageSelectionMode>(value, ignoreCase: true, out var mode)
            ? mode
            : throw new FormatException($"Unknown custom message selection mode '{value}'.");
}
