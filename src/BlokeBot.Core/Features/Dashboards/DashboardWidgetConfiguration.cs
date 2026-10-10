using System.Diagnostics;
using System.Text.Json.Serialization;

namespace BlokeBot.Core.Features.Dashboards;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DashboardWidgetConfiguration.Automation), "automation")]
[JsonDerivedType(typeof(DashboardWidgetConfiguration.Value), "value")]
[JsonDerivedType(typeof(DashboardWidgetConfiguration.Countdown), "countdown")]
[JsonDerivedType(typeof(DashboardWidgetConfiguration.Poll), "poll")]
[JsonDerivedType(typeof(DashboardWidgetConfiguration.Prediction), "prediction")]
[JsonDerivedType(typeof(DashboardWidgetConfiguration.Giveaway), "giveaway")]
[JsonDerivedType(typeof(DashboardWidgetConfiguration.Guessing), "guessing")]
internal abstract record DashboardWidgetConfiguration
{
    private DashboardWidgetConfiguration(DashboardBinding binding) => Binding = binding;

    public DashboardBinding Binding { get; init; }

    internal TResult Match<TResult>(
        Func<Automation, TResult> automation,
        Func<Value, TResult> value,
        Func<Countdown, TResult> countdown,
        Func<Poll, TResult> poll,
        Func<Prediction, TResult> prediction,
        Func<Giveaway, TResult> giveaway,
        Func<Guessing, TResult> guessing
    ) =>
        this switch
        {
            Automation configuration => automation(configuration),
            Value configuration => value(configuration),
            Countdown configuration => countdown(configuration),
            Poll configuration => poll(configuration),
            Prediction configuration => prediction(configuration),
            Giveaway configuration => giveaway(configuration),
            Guessing configuration => guessing(configuration),
            _ => throw new UnreachableException(),
        };

    internal sealed record Automation(DashboardBinding Binding, DashboardTextOverride Data)
        : DashboardWidgetConfiguration(Binding);

    internal sealed record Value(DashboardBinding Binding, DashboardValueSelection Selection)
        : DashboardWidgetConfiguration(Binding);

    internal sealed record Countdown(DashboardBinding Binding, TimeSpan DefaultDuration)
        : DashboardWidgetConfiguration(Binding);

    internal sealed record Poll(DashboardBinding Binding) : DashboardWidgetConfiguration(Binding);

    internal sealed record Prediction(DashboardBinding Binding)
        : DashboardWidgetConfiguration(Binding);

    internal sealed record Giveaway(DashboardBinding Binding)
        : DashboardWidgetConfiguration(Binding);

    internal sealed record Guessing(DashboardBinding Binding)
        : DashboardWidgetConfiguration(Binding);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(DashboardTextOverride.Inherit), "inherit")]
[JsonDerivedType(typeof(DashboardTextOverride.Replace), "replace")]
internal abstract record DashboardTextOverride
{
    private DashboardTextOverride() { }

    internal TResult Match<TResult>(
        Func<Inherit, TResult> inherit,
        Func<Replace, TResult> replace
    ) =>
        this switch
        {
            Inherit value => inherit(value),
            Replace value => replace(value),
            _ => throw new UnreachableException(),
        };

    internal sealed record Inherit : DashboardTextOverride;

    internal sealed record Replace(string Text) : DashboardTextOverride;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(DashboardValueSelection.Scalar), "scalar")]
[JsonDerivedType(typeof(DashboardValueSelection.DictionaryEntry), "dictionary-entry")]
internal abstract record DashboardValueSelection
{
    private DashboardValueSelection() { }

    internal TResult Match<TResult>(
        Func<Scalar, TResult> scalar,
        Func<DictionaryEntry, TResult> dictionaryEntry
    ) =>
        this switch
        {
            Scalar value => scalar(value),
            DictionaryEntry value => dictionaryEntry(value),
            _ => throw new UnreachableException(),
        };

    internal sealed record Scalar : DashboardValueSelection;

    internal sealed record DictionaryEntry(string Key) : DashboardValueSelection;
}
