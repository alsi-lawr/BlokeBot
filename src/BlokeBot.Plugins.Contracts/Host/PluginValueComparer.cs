namespace BlokeBot.Plugins.Contracts;

public static class PluginValueComparer
{
    public static bool SemanticallyEquals(PluginValue left, PluginValue right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return (left, right) switch
        {
            (PluginValue.Nil, PluginValue.Nil) => true,
            (PluginValue.Boolean first, PluginValue.Boolean second) => first.Value == second.Value,
            (PluginValue.Number first, PluginValue.Number second) => first.Value.Equals(
                second.Value
            ),
            (PluginValue.String first, PluginValue.String second) => first.Value.Equals(
                second.Value,
                StringComparison.Ordinal
            ),
            (PluginValue.Array first, PluginValue.Array second) => SequencesEqual(
                first.Items,
                second.Items
            ),
            (PluginValue.Map first, PluginValue.Map second) => MapsEqual(
                first.Properties,
                second.Properties
            ),
            _ => false,
        };
    }

    private static bool SequencesEqual(
        IReadOnlyList<PluginValue> left,
        IReadOnlyList<PluginValue> right
    ) =>
        left.Count == right.Count
        && left.Select((value, index) => SemanticallyEquals(value, right[index]))
            .All(value => value);

    private static bool MapsEqual(
        IReadOnlyList<PluginValueProperty> left,
        IReadOnlyList<PluginValueProperty> right
    )
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var orderedLeft = left.OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
        var orderedRight = right
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();
        return orderedLeft
            .Select(
                (property, index) =>
                    property.Name.Equals(orderedRight[index].Name, StringComparison.Ordinal)
                    && SemanticallyEquals(property.Value, orderedRight[index].Value)
            )
            .All(value => value);
    }
}
