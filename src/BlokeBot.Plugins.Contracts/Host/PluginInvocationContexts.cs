using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

[JsonConverter(typeof(PluginHostIdJsonConverter))]
public sealed record PluginHostId
{
    private PluginHostId(int value) => Value = value;

    public int Value { get; }

    public static bool TryCreate(int candidate, out PluginHostId hostId)
    {
        var valid = candidate > 0;
        hostId = valid ? new(candidate) : null!;
        return valid;
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PluginInvocationContext.Installation), "installation")]
[JsonDerivedType(typeof(PluginInvocationContext.Channel), "channel")]
[JsonDerivedType(typeof(PluginInvocationContext.Automation), "automation")]
[JsonDerivedType(typeof(PluginInvocationContext.Migration), "migration")]
[JsonDerivedType(typeof(PluginInvocationContext.Page), "page")]
public abstract record PluginInvocationContext
{
    private PluginInvocationContext() { }

    [JsonIgnore]
    public abstract PluginInvocationContextKind Kind { get; }

    public sealed record Installation(PluginInstallationIdentity Plugin) : PluginInvocationContext
    {
        [JsonIgnore]
        public override PluginInvocationContextKind Kind =>
            PluginInvocationContextKind.Installation;
    }

    public sealed record Channel(
        PluginInstallationIdentity Plugin,
        PluginHostId Host,
        PluginActorContext? Actor = null,
        PluginStreamContext? Stream = null,
        PluginCommandInvocation? Command = null,
        PluginEventInvocation? Event = null,
        PluginScheduleInvocation? Schedule = null
    ) : PluginInvocationContext
    {
        [JsonIgnore]
        public override PluginInvocationContextKind Kind => PluginInvocationContextKind.Channel;
    }

    public sealed record Automation(
        PluginInstallationIdentity Plugin,
        PluginHostId Host,
        PluginFeatureId Feature,
        PluginAutomationDefinitionId Definition,
        PluginAutomationInvocationId InvocationId
    ) : PluginInvocationContext
    {
        [JsonIgnore]
        public override PluginInvocationContextKind Kind => PluginInvocationContextKind.Automation;
    }

    public sealed record Migration(
        PluginInstallationIdentity Plugin,
        PluginMigrationId MigrationId,
        SemanticVersion FromVersion,
        SemanticVersion ToVersion
    ) : PluginInvocationContext
    {
        [JsonIgnore]
        public override PluginInvocationContextKind Kind => PluginInvocationContextKind.Migration;
    }

    public sealed record Page(
        PluginInstallationIdentity Plugin,
        PluginHostId Host,
        PluginPageId PageId,
        PluginPageSessionId SessionId
    ) : PluginInvocationContext
    {
        [JsonIgnore]
        public override PluginInvocationContextKind Kind => PluginInvocationContextKind.Page;
    }
}

public sealed record PluginActorContext(
    string Login,
    string DisplayName,
    string? TwitchUserId,
    bool IsBroadcaster,
    bool IsModerator,
    bool IsSubscriber
);

public sealed record PluginStreamContext(string? StreamId, bool IsLive);

public sealed record PluginCommandInvocation(string Route, IReadOnlyList<string> Arguments);

public sealed record PluginEventInvocation(
    PluginEventHandlerId HandlerId,
    string Source,
    string EventId,
    DateTimeOffset OccurredAtUtc
);

public sealed record PluginScheduleInvocation(
    PluginScheduleHandlerId HandlerId,
    Guid ScheduleId,
    DateTimeOffset DueAtUtc
);

internal sealed class PluginHostIdJsonConverter : JsonConverter<PluginHostId>
{
    public override PluginHostId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        (
            reader.TokenType == JsonTokenType.Number
            && reader.TryGetInt32(out var candidate)
            && PluginHostId.TryCreate(candidate, out var hostId)
        )
            ? hostId
            : throw new JsonException("Invalid plugin host ID.");

    public override void Write(
        Utf8JsonWriter writer,
        PluginHostId value,
        JsonSerializerOptions options
    ) => writer.WriteNumberValue(value.Value);
}
