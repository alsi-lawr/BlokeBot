using System.Reflection;

namespace BlokeBot.Persistence.Models;

[AttributeUsage(AttributeTargets.Field)]
public sealed class PersistedTokenAttribute(string token) : Attribute
{
    public string Token { get; } =
        !string.IsNullOrWhiteSpace(token)
            ? token
            : throw new ArgumentException("A persisted token cannot be blank.", nameof(token));
}

public static class PersistedEnumTokens<TEnum>
    where TEnum : struct, Enum
{
    private static readonly IReadOnlyDictionary<TEnum, string> TokensByValue = BuildTokensByValue();
    private static readonly IReadOnlyDictionary<string, TEnum> ValuesByToken = BuildValuesByToken();

    public static IReadOnlyList<string> Values { get; } =
        Enum.GetValues<TEnum>()
            .Select(Format)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static string Format(TEnum value) =>
        TokensByValue.TryGetValue(value, out var token)
            ? token
            : throw new ArgumentOutOfRangeException(nameof(value), value, null);

    public static TEnum Parse(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ValuesByToken.TryGetValue(token.Trim(), out var value)
            ? value
            : throw new FormatException(
                $"Unknown persisted {typeof(TEnum).Name} token '{token}'."
            );
    }

    private static IReadOnlyDictionary<TEnum, string> BuildTokensByValue() =>
        Enum.GetValues<TEnum>()
            .ToDictionary(
                value => value,
                value =>
                    typeof(TEnum)
                        .GetField(value.ToString())
                        ?.GetCustomAttribute<PersistedTokenAttribute>()
                        ?.Token
                    ?? throw new InvalidOperationException(
                        $"{typeof(TEnum).Name}.{value} must declare {nameof(PersistedTokenAttribute)}."
                    )
            );

    private static IReadOnlyDictionary<string, TEnum> BuildValuesByToken()
    {
        var values = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in TokensByValue)
        {
            if (!values.TryAdd(pair.Value, pair.Key))
                throw new InvalidOperationException(
                    $"Persisted token '{pair.Value}' is duplicated on {typeof(TEnum).Name}."
                );
        }

        return values;
    }
}
