using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed class EventSubClient(
    IHttpClientFactory httpClientFactory,
    TwitchEndpointPolicy endpointPolicy
)
{
    private const int _maxErrorBodyBytes = 16 * 1024;
    private const int _maxDiagnosticFieldLength = 256;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = httpClientFactory.CreateClient("twitch-helix");

    public Task<string> CreateChatMessageSubscriptionAsync(
        HelixRequestContext context,
        string broadcasterId,
        string botUserId,
        string sessionId,
        CancellationToken cancellationToken
    ) =>
        CreateSubscriptionAsync(
            context,
            new(
                "channel.chat.message",
                "1",
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = broadcasterId,
                    ["user_id"] = botUserId,
                },
                sessionId
            ),
            cancellationToken
        );

    public Task<string> CreateShoutoutCreateSubscriptionAsync(
        HelixRequestContext context,
        string broadcasterId,
        string moderatorId,
        string sessionId,
        CancellationToken cancellationToken
    ) =>
        CreateSubscriptionAsync(
            context,
            new(
                "channel.shoutout.create",
                "1",
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = broadcasterId,
                    ["moderator_user_id"] = moderatorId,
                },
                sessionId
            ),
            cancellationToken
        );

    public Task<string> CreateShoutoutReceiveSubscriptionAsync(
        HelixRequestContext context,
        string broadcasterId,
        string moderatorId,
        string sessionId,
        CancellationToken cancellationToken
    ) =>
        CreateSubscriptionAsync(
            context,
            new(
                "channel.shoutout.receive",
                "1",
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = broadcasterId,
                    ["moderator_user_id"] = moderatorId,
                },
                sessionId
            ),
            cancellationToken
        );

    public Task<string> CreateIncomingRaidSubscriptionAsync(
        HelixRequestContext context,
        string broadcasterId,
        string sessionId,
        CancellationToken cancellationToken
    ) =>
        CreateSubscriptionAsync(
            context,
            new(
                "channel.raid",
                "1",
                new Dictionary<string, string> { ["to_broadcaster_user_id"] = broadcasterId },
                sessionId
            ),
            cancellationToken
        );

    public Task<string> CreatePollSubscriptionAsync(
        HelixRequestContext context,
        string type,
        string broadcasterId,
        string sessionId,
        CancellationToken cancellationToken
    ) =>
        CreateSubscriptionAsync(
            context,
            new(
                type,
                "1",
                new Dictionary<string, string> { ["broadcaster_user_id"] = broadcasterId },
                sessionId
            ),
            cancellationToken
        );

    public async Task<string> CreateSubscriptionAsync(
        HelixRequestContext context,
        EventSubSubscriptionRequest subscription,
        CancellationToken cancellationToken
    )
    {
        var payload = new CreateSubscriptionRequest
        {
            Type = subscription.Type,
            Version = subscription.Version,
            Condition = subscription.Condition,
            Transport = new SubscriptionTransport
            {
                Method = "websocket",
                SessionId = subscription.SessionId,
            },
        };
        using var request = HelixRequest.Create(
            HttpMethod.Post,
            endpointPolicy.HelixEndpoint("eventsub/subscriptions").AbsoluteUri,
            context
        );
        request.Content = JsonContent.Create(payload, options: _jsonOptions);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateSubscriptionFailureAsync(
                response,
                cancellationToken,
                [
                    context.ClientId,
                    context.AccessToken,
                    subscription.SessionId,
                    .. subscription.Condition.Values,
                ]
            );
        }

        var result = await response.Content.ReadFromJsonAsync<SubscriptionResponse>(
            _jsonOptions,
            cancellationToken
        );
        return result?.Data.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException(
                "Twitch did not return an EventSub subscription ID."
            );
    }

    private static async ValueTask<EventSubSubscriptionCreationException> CreateSubscriptionFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        IReadOnlyList<string> sensitiveValues
    )
    {
        var body = await ReadBoundedBodyAsync(response, cancellationToken);
        string? providerError = null;
        string? providerMessage = null;
        string? existingSubscriptionId = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind is JsonValueKind.Object)
            {
                providerError = ReadDiagnosticText(document.RootElement, "error", sensitiveValues);
                providerMessage = ReadDiagnosticText(
                    document.RootElement,
                    "message",
                    sensitiveValues
                );
                existingSubscriptionId = ReadDiagnosticIdentifier(
                    document.RootElement,
                    sensitiveValues,
                    "existing_subscription_id",
                    "subscription_id",
                    "id"
                );
            }
        }
        catch (JsonException)
        {
            // Twitch occasionally returns a non-JSON error body. The bounded status remains useful.
        }

        return new EventSubSubscriptionCreationException(
            response.StatusCode,
            providerError,
            providerMessage,
            existingSubscriptionId
        );
    }

    private static async ValueTask<string> ReadBoundedBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[_maxErrorBodyBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(length, buffer.Length - length),
                cancellationToken
            );
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, length);
    }

    private static string? ReadDiagnosticText(
        JsonElement root,
        string propertyName,
        IReadOnlyList<string> sensitiveValues
    )
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        var value = property.ValueKind is JsonValueKind.String ? property.GetString() : null;
        return SanitizeDiagnosticText(value, sensitiveValues);
    }

    private static string? ReadDiagnosticIdentifier(
        JsonElement root,
        IReadOnlyList<string> sensitiveValues,
        params string[] propertyNames
    )
    {
        foreach (var propertyName in propertyNames)
        {
            if (
                root.TryGetProperty(propertyName, out var property)
                && property.ValueKind is JsonValueKind.String
            )
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return SanitizeDiagnosticIdentifier(value, sensitiveValues);
                }
            }
        }

        return null;
    }

    private static string? SanitizeDiagnosticText(
        string? value,
        IReadOnlyList<string> sensitiveValues
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (
            var sensitiveValue in sensitiveValues.Where(static value =>
                !string.IsNullOrEmpty(value)
            )
        )
        {
            value = value.Replace(sensitiveValue, "[redacted]", StringComparison.Ordinal);
        }

        if (
            value.Contains("access_token", StringComparison.OrdinalIgnoreCase)
            || value.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || value.Contains("client_secret", StringComparison.OrdinalIgnoreCase)
            || value.Contains("cookie", StringComparison.OrdinalIgnoreCase)
            || value.Contains("bearer ", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "[redacted]";
        }

        var sanitized = new string(
            value.Where(static character => !char.IsControl(character)).ToArray()
        );
        return sanitized.Trim() is { Length: > 0 } trimmed
            ? trimmed[..Math.Min(trimmed.Length, _maxDiagnosticFieldLength)]
            : null;
    }

    private static string? SanitizeDiagnosticIdentifier(
        string value,
        IReadOnlyList<string> sensitiveValues
    )
    {
        if (
            sensitiveValues.Any(sensitiveValue =>
                !string.IsNullOrEmpty(sensitiveValue)
                && value.Contains(sensitiveValue, StringComparison.Ordinal)
            )
        )
        {
            return null;
        }

        var sanitized = new string(
            value
                .Where(static character =>
                    char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                )
                .ToArray()
        );
        return sanitized.Length is 0
            ? null
            : sanitized[..Math.Min(sanitized.Length, _maxDiagnosticFieldLength)];
    }

    public async Task DeleteSubscriptionAsync(
        HelixRequestContext context,
        string subscriptionId,
        CancellationToken cancellationToken
    )
    {
        var uri =
            endpointPolicy.HelixEndpoint("eventsub/subscriptions").AbsoluteUri
            + $"?id={Uri.EscapeDataString(subscriptionId)}";
        using var request = HelixRequest.Create(HttpMethod.Delete, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return;
        }

        _ = response.EnsureSuccessStatusCode();
    }

    private sealed record CreateSubscriptionRequest
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("version")]
        public required string Version { get; init; }

        [JsonPropertyName("condition")]
        public required IReadOnlyDictionary<string, string> Condition { get; init; }

        [JsonPropertyName("transport")]
        public required SubscriptionTransport Transport { get; init; }
    }

    private sealed record SubscriptionTransport
    {
        [JsonPropertyName("method")]
        public required string Method { get; init; }

        [JsonPropertyName("session_id")]
        public required string SessionId { get; init; }
    }

    private sealed record SubscriptionResponse
    {
        [JsonPropertyName("data")]
        public required ImmutableArray<SubscriptionItem> Data { get; init; }
    }

    private sealed record SubscriptionItem
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }
    }
}
