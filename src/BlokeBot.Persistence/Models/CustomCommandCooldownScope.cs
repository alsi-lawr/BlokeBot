namespace BlokeBot.Persistence.Models;

public enum CustomCommandCooldownScope
{
    Global,
    User,
}

public static class CustomCommandCooldownScopeStore
{
    public static IReadOnlyList<string> Values { get; } =
    [
        Format(CustomCommandCooldownScope.Global),
        Format(CustomCommandCooldownScope.User),
    ];

    public static string Format(CustomCommandCooldownScope scope) => scope.ToString();

    public static CustomCommandCooldownScope Parse(string value) =>
        Enum.TryParse<CustomCommandCooldownScope>(value, ignoreCase: true, out var scope)
            ? scope
            : throw new FormatException($"Unknown custom command cooldown scope '{value}'.");
}
