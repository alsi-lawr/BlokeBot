using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public abstract record PluginSettingValue
{
    private PluginSettingValue() { }

    public abstract TResult Match<TResult>(
        Func<Boolean, TResult> boolean,
        Func<Text, TResult> text,
        Func<Integer, TResult> integer,
        Func<Number, TResult> number,
        Func<Duration, TResult> duration,
        Func<Choice, TResult> choice
    );

    public sealed record Boolean(bool Value) : PluginSettingValue
    {
        public override TResult Match<TResult>(
            Func<Boolean, TResult> boolean,
            Func<Text, TResult> text,
            Func<Integer, TResult> integer,
            Func<Number, TResult> number,
            Func<Duration, TResult> duration,
            Func<Choice, TResult> choice
        ) => boolean(this);
    }

    public sealed record Text(string Value) : PluginSettingValue
    {
        public override TResult Match<TResult>(
            Func<Boolean, TResult> boolean,
            Func<Text, TResult> text,
            Func<Integer, TResult> integer,
            Func<Number, TResult> number,
            Func<Duration, TResult> duration,
            Func<Choice, TResult> choice
        ) => text(this);
    }

    public sealed record Integer(long Value) : PluginSettingValue
    {
        public override TResult Match<TResult>(
            Func<Boolean, TResult> boolean,
            Func<Text, TResult> text,
            Func<Integer, TResult> integer,
            Func<Number, TResult> number,
            Func<Duration, TResult> duration,
            Func<Choice, TResult> choice
        ) => integer(this);
    }

    public sealed record Number(decimal Value) : PluginSettingValue
    {
        public override TResult Match<TResult>(
            Func<Boolean, TResult> boolean,
            Func<Text, TResult> text,
            Func<Integer, TResult> integer,
            Func<Number, TResult> number,
            Func<Duration, TResult> duration,
            Func<Choice, TResult> choice
        ) => number(this);
    }

    public sealed record Duration(long Seconds) : PluginSettingValue
    {
        public override TResult Match<TResult>(
            Func<Boolean, TResult> boolean,
            Func<Text, TResult> text,
            Func<Integer, TResult> integer,
            Func<Number, TResult> number,
            Func<Duration, TResult> duration,
            Func<Choice, TResult> choice
        ) => duration(this);
    }

    public sealed record Choice(PluginSettingChoiceId Value) : PluginSettingValue
    {
        public override TResult Match<TResult>(
            Func<Boolean, TResult> boolean,
            Func<Text, TResult> text,
            Func<Integer, TResult> integer,
            Func<Number, TResult> number,
            Func<Duration, TResult> duration,
            Func<Choice, TResult> choice
        ) => choice(this);
    }
}

public sealed record PluginSettingValueEntry(PluginSettingId SettingId, PluginSettingValue Value);

public sealed class PluginSettingValues : IEquatable<PluginSettingValues>
{
    private PluginSettingValues(ImmutableArray<PluginSettingValueEntry> entries) =>
        Entries = entries;

    public IReadOnlyList<PluginSettingValueEntry> Entries { get; }

    public static PluginSettingValues Empty { get; } = new([]);

    public static PluginSettingValuesOutcome Create(IEnumerable<PluginSettingValueEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var materialized = entries
            .OrderBy(static entry => entry.SettingId.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        return
            materialized.Select(static entry => entry.SettingId).Distinct().Count()
            == materialized.Length
            ? new PluginSettingValuesOutcome.Created(new(materialized))
            : new PluginSettingValuesOutcome.DuplicateSetting();
    }

    public bool Equals(PluginSettingValues? other) =>
        ReferenceEquals(this, other) || (other is not null && Entries.SequenceEqual(other.Entries));

    public override bool Equals(object? obj) => Equals(obj as PluginSettingValues);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var entry in Entries)
        {
            hash.Add(entry);
        }
        return hash.ToHashCode();
    }
}

public abstract record PluginSettingValuesOutcome
{
    private PluginSettingValuesOutcome() { }

    public sealed record Created(PluginSettingValues Values) : PluginSettingValuesOutcome;

    public sealed record DuplicateSetting : PluginSettingValuesOutcome;
}
