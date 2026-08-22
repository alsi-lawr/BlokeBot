using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

public enum PluginValueKind
{
    Nil,
    Boolean,
    Number,
    String,
    Array,
    Map,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PluginValue.Nil), "nil")]
[JsonDerivedType(typeof(PluginValue.Boolean), "boolean")]
[JsonDerivedType(typeof(PluginValue.Number), "number")]
[JsonDerivedType(typeof(PluginValue.String), "string")]
[JsonDerivedType(typeof(PluginValue.Array), "array")]
[JsonDerivedType(typeof(PluginValue.Map), "map")]
public abstract record PluginValue
{
    private PluginValue() { }

    [JsonIgnore]
    public abstract PluginValueKind Kind { get; }

    public sealed record Nil : PluginValue
    {
        [JsonIgnore]
        public override PluginValueKind Kind => PluginValueKind.Nil;
    }

    public sealed record Boolean(bool Value) : PluginValue
    {
        [JsonIgnore]
        public override PluginValueKind Kind => PluginValueKind.Boolean;
    }

    public sealed record Number(double Value) : PluginValue
    {
        [JsonIgnore]
        public override PluginValueKind Kind => PluginValueKind.Number;
    }

    public sealed record String(string Value) : PluginValue
    {
        [JsonIgnore]
        public override PluginValueKind Kind => PluginValueKind.String;
    }

    public sealed record Array(ImmutableArray<PluginValue> Items) : PluginValue
    {
        [JsonIgnore]
        public override PluginValueKind Kind => PluginValueKind.Array;
    }

    public sealed record Map(ImmutableArray<PluginValueProperty> Properties) : PluginValue
    {
        [JsonIgnore]
        public override PluginValueKind Kind => PluginValueKind.Map;
    }
}

public sealed record PluginValueProperty(string Name, PluginValue Value);

public enum PluginValueErrorCode
{
    DepthExceeded,
    NodeCountExceeded,
    StringTooLarge,
    PayloadTooLarge,
    NonFiniteNumber,
    DuplicateMapKey,
    InvalidMapKey,
}

public sealed record PluginValueError(PluginValueErrorCode Code, string Location);

public abstract record PluginValueValidationOutcome
{
    private PluginValueValidationOutcome() { }

    public sealed record Valid(long PayloadBytes) : PluginValueValidationOutcome;

    public sealed record Invalid(IReadOnlyList<PluginValueError> Errors)
        : PluginValueValidationOutcome;
}
