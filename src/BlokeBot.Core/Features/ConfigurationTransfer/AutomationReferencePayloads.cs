using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal interface IAutomationReferencePayload
{
    bool IsWellFormed { get; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationCustomCommandTransferPayload(
    [property: JsonRequired, JsonPropertyName("custom-command-id")] string CustomCommandId
) : IAutomationReferencePayload
{
    [JsonIgnore]
    public bool IsWellFormed => !string.IsNullOrWhiteSpace(CustomCommandId);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationCustomCommandPersistedPayload(
    [property: JsonRequired, JsonPropertyName("custom-command-id")] int CustomCommandId
) : IAutomationReferencePayload
{
    [JsonIgnore]
    public bool IsWellFormed => CustomCommandId > 0;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationOverlayTransferPayload(
    [property: JsonRequired, JsonPropertyName("target-id")] string TargetId,
    [property: JsonRequired, JsonPropertyName("cue-id")] string CueId
) : IAutomationReferencePayload
{
    [JsonIgnore]
    public bool IsWellFormed =>
        !string.IsNullOrWhiteSpace(TargetId) && !string.IsNullOrWhiteSpace(CueId);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationOverlayPersistedPayload(
    [property: JsonRequired, JsonPropertyName("target-id")] Guid TargetId,
    [property: JsonRequired, JsonPropertyName("cue-id")] Guid CueId
) : IAutomationReferencePayload
{
    [JsonIgnore]
    public bool IsWellFormed => TargetId != Guid.Empty && CueId != Guid.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationRewardTransferPayload(
    [property: JsonPropertyName("reward-id")] string? RewardId,
    [property: JsonRequired, JsonPropertyName("completion-policy")] string CompletionPolicy
) : IAutomationReferencePayload
{
    [JsonIgnore]
    public bool IsWellFormed =>
        !string.IsNullOrWhiteSpace(CompletionPolicy)
        && (RewardId is null || !string.IsNullOrWhiteSpace(RewardId));
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AutomationRewardPersistedPayload(
    [property: JsonPropertyName("reward-id")] string? RewardId,
    [property: JsonRequired, JsonPropertyName("completion-policy")] string CompletionPolicy
) : IAutomationReferencePayload
{
    [JsonIgnore]
    public bool IsWellFormed =>
        !string.IsNullOrWhiteSpace(CompletionPolicy)
        && (RewardId is null || !string.IsNullOrWhiteSpace(RewardId));
}

internal static class AutomationReferencePayloadSerializer
{
    internal static bool TryDeserialize<T>(JsonElement json, [NotNullWhen(true)] out T? payload)
        where T : class, IAutomationReferencePayload
    {
        try
        {
            payload = json.Deserialize<T>();
            return payload is { IsWellFormed: true };
        }
        catch (JsonException)
        {
            payload = null;
            return false;
        }
        catch (NotSupportedException)
        {
            payload = null;
            return false;
        }
    }

    internal static bool TryDeserializePersisted<T>(string json, [NotNullWhen(true)] out T? payload)
        where T : class, IAutomationReferencePayload
    {
        try
        {
            payload = JsonSerializer.Deserialize<T>(json);
            return payload is { IsWellFormed: true };
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            payload = null;
            return false;
        }
    }
}
