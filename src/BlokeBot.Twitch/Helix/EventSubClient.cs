using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed class EventSubClient(IHttpClientFactory httpClientFactory)
{
    private const string _subscriptionsEndpoint =
        "https://api.twitch.tv/helix/eventsub/subscriptions";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = httpClientFactory.CreateClient("twitch-helix");

    public async Task<string> CreateChatMessageSubscriptionAsync(
        HelixRequestContext context,
        string broadcasterId,
        string botUserId,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        var payload = new CreateSubscriptionRequest
        {
            Type = "channel.chat.message",
            Version = "1",
            Condition = new SubscriptionCondition
            {
                BroadcasterUserId = broadcasterId,
                UserId = botUserId,
            },
            Transport = new SubscriptionTransport { Method = "websocket", SessionId = sessionId },
        };
        using var request = HelixRequest.Create(HttpMethod.Post, _subscriptionsEndpoint, context);
        request.Content = JsonContent.Create(payload, options: _jsonOptions);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SubscriptionResponse>(
            _jsonOptions,
            cancellationToken
        );
        return result?.Data.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException(
                "Twitch did not return an EventSub subscription ID."
            );
    }

    public async Task DeleteSubscriptionAsync(
        HelixRequestContext context,
        string subscriptionId,
        CancellationToken cancellationToken
    )
    {
        var uri = _subscriptionsEndpoint + $"?id={Uri.EscapeDataString(subscriptionId)}";
        using var request = HelixRequest.Create(HttpMethod.Delete, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    private sealed record CreateSubscriptionRequest
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("version")]
        public required string Version { get; init; }

        [JsonPropertyName("condition")]
        public required SubscriptionCondition Condition { get; init; }

        [JsonPropertyName("transport")]
        public required SubscriptionTransport Transport { get; init; }
    }

    private sealed record SubscriptionCondition
    {
        [JsonPropertyName("broadcaster_user_id")]
        public required string BroadcasterUserId { get; init; }

        [JsonPropertyName("user_id")]
        public required string UserId { get; init; }
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
