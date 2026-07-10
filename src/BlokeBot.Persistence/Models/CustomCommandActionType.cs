namespace BlokeBot.Persistence.Models;

public enum CustomCommandActionType
{
    Message,
    Counter,
}

public static class CustomCommandActionTypeStore
{
    public static IReadOnlyList<string> Values { get; } =
    [
        Format(CustomCommandActionType.Counter),
        Format(CustomCommandActionType.Message),
    ];

    public static string Format(CustomCommandActionType type) => type.ToString();

    public static CustomCommandActionType Parse(string value) =>
        Enum.TryParse<CustomCommandActionType>(value, ignoreCase: true, out var type)
            ? type
            : throw new FormatException($"Unknown custom command action type '{value}'.");
}
