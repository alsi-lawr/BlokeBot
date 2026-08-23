using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

[JsonConverter(typeof(PluginProtocolIdentifierJsonConverter<PluginWorkerInvocationId>))]
public sealed record PluginWorkerInvocationId
    : PluginProtocolIdentifier,
        IPluginProtocolIdentifier<PluginWorkerInvocationId>
{
    private PluginWorkerInvocationId(Guid value)
        : base(value) { }

    public static bool TryCreate(Guid candidate, out PluginWorkerInvocationId identifier) =>
        PluginProtocolIdentifierFactory.TryCreate(
            candidate,
            static value => new PluginWorkerInvocationId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginProtocolIdentifierJsonConverter<PluginWorkerCancellationId>))]
public sealed record PluginWorkerCancellationId
    : PluginProtocolIdentifier,
        IPluginProtocolIdentifier<PluginWorkerCancellationId>
{
    private PluginWorkerCancellationId(Guid value)
        : base(value) { }

    public static bool TryCreate(Guid candidate, out PluginWorkerCancellationId identifier) =>
        PluginProtocolIdentifierFactory.TryCreate(
            candidate,
            static value => new PluginWorkerCancellationId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginWorkerGenerationJsonConverter))]
public sealed record PluginWorkerGeneration
{
    private PluginWorkerGeneration(ulong value) => Value = value;

    public ulong Value { get; }

    public static bool TryCreate(ulong candidate, out PluginWorkerGeneration generation)
    {
        var valid = candidate > 0;
        generation = valid ? new(candidate) : null!;
        return valid;
    }
}

[JsonConverter(typeof(PluginWorkerDeadlineJsonConverter))]
public sealed record PluginWorkerDeadline
{
    private PluginWorkerDeadline(long unixTimeMilliseconds) =>
        UnixTimeMilliseconds = unixTimeMilliseconds;

    public long UnixTimeMilliseconds { get; }

    public static bool TryCreate(long candidate, out PluginWorkerDeadline deadline)
    {
        var valid = candidate > 0 && candidate <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();
        deadline = valid ? new(candidate) : null!;
        return valid;
    }

    public static PluginWorkerDeadline From(DateTimeOffset value) =>
        TryCreate(value.ToUnixTimeMilliseconds(), out var deadline)
            ? deadline
            : throw new ArgumentOutOfRangeException(nameof(value));

    public DateTimeOffset ToDateTimeOffset() =>
        DateTimeOffset.FromUnixTimeMilliseconds(UnixTimeMilliseconds);
}

internal sealed class PluginWorkerGenerationJsonConverter : JsonConverter<PluginWorkerGeneration>
{
    public override PluginWorkerGeneration Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType == JsonTokenType.Number
        && reader.TryGetUInt64(out var candidate)
        && PluginWorkerGeneration.TryCreate(candidate, out var generation)
            ? generation
            : throw new JsonException("Invalid plugin worker generation.");

    public override void Write(
        Utf8JsonWriter writer,
        PluginWorkerGeneration value,
        JsonSerializerOptions options
    ) => writer.WriteNumberValue(value.Value);
}

internal sealed class PluginWorkerDeadlineJsonConverter : JsonConverter<PluginWorkerDeadline>
{
    public override PluginWorkerDeadline Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType == JsonTokenType.Number
        && reader.TryGetInt64(out var candidate)
        && PluginWorkerDeadline.TryCreate(candidate, out var deadline)
            ? deadline
            : throw new JsonException("Invalid plugin worker deadline.");

    public override void Write(
        Utf8JsonWriter writer,
        PluginWorkerDeadline value,
        JsonSerializerOptions options
    ) => writer.WriteNumberValue(value.UnixTimeMilliseconds);
}
