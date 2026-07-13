using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed record ChatMessageDropReason
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    public override string ToString()
    {
        return $"{nameof(ChatMessageDropReason)} {{ Code = {Code}, Message = [redacted] }}";
    }
}
