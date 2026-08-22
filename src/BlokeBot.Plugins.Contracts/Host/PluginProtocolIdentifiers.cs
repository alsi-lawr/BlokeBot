using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

public abstract record PluginProtocolIdentifier
{
    private protected PluginProtocolIdentifier(Guid value) => Value = value;

    public Guid Value { get; }

    public override string ToString() =>
        Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
}

internal interface IPluginProtocolIdentifier<TIdentifier>
    where TIdentifier : PluginProtocolIdentifier
{
    static abstract bool TryCreate(Guid candidate, out TIdentifier identifier);
}

internal static class PluginProtocolIdentifierFactory
{
    internal static bool TryCreate<TIdentifier>(
        Guid candidate,
        Func<Guid, TIdentifier> create,
        out TIdentifier identifier
    )
        where TIdentifier : PluginProtocolIdentifier
    {
        var valid = candidate != Guid.Empty;
        identifier = valid ? create(candidate) : null!;
        return valid;
    }
}

internal sealed class PluginProtocolIdentifierJsonConverter<TIdentifier>
    : JsonConverter<TIdentifier>
    where TIdentifier : PluginProtocolIdentifier, IPluginProtocolIdentifier<TIdentifier>
{
    public override TIdentifier Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType == JsonTokenType.String
        && reader.TryGetGuid(out var candidate)
        && TIdentifier.TryCreate(candidate, out var identifier)
            ? identifier
            : throw new JsonException($"Invalid {typeof(TIdentifier).Name} value.");

    public override void Write(
        Utf8JsonWriter writer,
        TIdentifier value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value.Value);
}

[JsonConverter(typeof(PluginProtocolIdentifierJsonConverter<PluginHostCallId>))]
public sealed record PluginHostCallId
    : PluginProtocolIdentifier,
        IPluginProtocolIdentifier<PluginHostCallId>
{
    private PluginHostCallId(Guid value)
        : base(value) { }

    public static bool TryCreate(Guid candidate, out PluginHostCallId identifier) =>
        PluginProtocolIdentifierFactory.TryCreate(
            candidate,
            static value => new PluginHostCallId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginProtocolIdentifierJsonConverter<PluginCoroutineId>))]
public sealed record PluginCoroutineId
    : PluginProtocolIdentifier,
        IPluginProtocolIdentifier<PluginCoroutineId>
{
    private PluginCoroutineId(Guid value)
        : base(value) { }

    public static bool TryCreate(Guid candidate, out PluginCoroutineId identifier) =>
        PluginProtocolIdentifierFactory.TryCreate(
            candidate,
            static value => new PluginCoroutineId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginProtocolIdentifierJsonConverter<PluginAutomationInvocationId>))]
public sealed record PluginAutomationInvocationId
    : PluginProtocolIdentifier,
        IPluginProtocolIdentifier<PluginAutomationInvocationId>
{
    private PluginAutomationInvocationId(Guid value)
        : base(value) { }

    public static bool TryCreate(Guid candidate, out PluginAutomationInvocationId identifier) =>
        PluginProtocolIdentifierFactory.TryCreate(
            candidate,
            static value => new PluginAutomationInvocationId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginProtocolIdentifierJsonConverter<PluginPageSessionId>))]
public sealed record PluginPageSessionId
    : PluginProtocolIdentifier,
        IPluginProtocolIdentifier<PluginPageSessionId>
{
    private PluginPageSessionId(Guid value)
        : base(value) { }

    public static bool TryCreate(Guid candidate, out PluginPageSessionId identifier) =>
        PluginProtocolIdentifierFactory.TryCreate(
            candidate,
            static value => new PluginPageSessionId(value),
            out identifier
        );
}
