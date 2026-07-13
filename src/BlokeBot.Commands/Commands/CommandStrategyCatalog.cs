namespace BlokeBot.Commands;

/// <summary>
/// Builds a typed command strategy lookup for one feature command-kind type.
/// </summary>
public sealed class CommandStrategyCatalog<TKind, TState>
    where TKind : struct, Enum
{
    private readonly IReadOnlyDictionary<TKind, ICommandStrategy<TKind, TState>> _strategies;

    public CommandStrategyCatalog(IEnumerable<ICommandStrategy<TKind, TState>> strategies)
    {
        var strategyArray = strategies.OrderBy(x => x.Kind).ToArray();
        var duplicate = strategyArray.GroupBy(x => x.Kind).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Command kind {duplicate.Key} is registered more than once."
            );
        }

        var missing = Enum.GetValues<TKind>().Except(strategyArray.Select(x => x.Kind)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Missing command strategies for: {string.Join(", ", missing)}."
            );
        }

        Descriptors = strategyArray
            .Select(strategy => new CommandStrategyDescriptor<TKind>(
                strategy.Kind,
                CommandAliasNormalizer.NormalizeMany(strategy.DefaultAliases),
                strategy.RequiresModerator
            ))
            .ToArray();
        _strategies = strategyArray.ToDictionary(x => x.Kind);
    }

    public IReadOnlyList<CommandStrategyDescriptor<TKind>> Descriptors { get; }

    public ICommandStrategy<TKind, TState>? Find(TKind kind)
    {
        return _strategies.TryGetValue(kind, out var strategy) ? strategy : null;
    }
}

public sealed record CommandStrategyDescriptor<TKind>(
    TKind Kind,
    IReadOnlyList<string> DefaultAliases,
    bool RequiresModerator
)
    where TKind : notnull;
