using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Guessing.Guesses;

internal sealed class GuessAnswerNames
{
    private readonly ImmutableArray<GuessName> _values;

    private GuessAnswerNames(ImmutableArray<GuessName> values, string canonicalDisplayName)
    {
        _values = values;
        CanonicalDisplayName = canonicalDisplayName;
    }

    public GuessName Canonical => _values.Length == 0 ? GuessName.Parse(null) : _values[0];

    public string CanonicalDisplayName { get; }

    public IReadOnlyList<GuessName> Values => _values;

    public bool IsEmpty => _values.Length == 0;

    public string Value => string.Join(", ", _values.Select(name => name.Value));

    public bool Contains(GuessName name) => _values.Contains(name);

    public static GuessAnswerNames Parse(string? value)
    {
        var entries = (value ?? string.Empty).Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        var values = entries
            .Select(GuessName.Parse)
            .Where(name => !name.IsEmpty)
            .ToImmutableArray();
        return new GuessAnswerNames(values, entries.FirstOrDefault() ?? string.Empty);
    }
}
