using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed class EventSubClient(
    IHttpClientFactory httpClientFactory,
    TwitchEndpointPolicy endpointPolicy
)
{
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
        _ = response.EnsureSuccessStatusCode();
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
