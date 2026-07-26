using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed class ChatClient(
    IHttpClientFactory httpClientFactory,
    TwitchEndpointPolicy endpointPolicy
)
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = httpClientFactory.CreateClient("twitch-helix");

    public async Task<ChatMessageSendResult> SendMessageAsync(
        HelixRequestContext context,
        string broadcasterId,
        string senderId,
        string message,
        CancellationToken cancellationToken
    )
    {
        var payload = new SendMessageRequest
        {
            BroadcasterId = broadcasterId,
            SenderId = senderId,
            Message = message,
        };
        using var request = HelixRequest.Create(HttpMethod.Post, endpointPolicy.HelixEndpoint("chat/messages").AbsoluteUri, context);
        request.Content = JsonContent.Create(payload, options: _jsonOptions);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SendMessageResponse>(
            _jsonOptions,
            cancellationToken
        );
        return result?.Data.FirstOrDefault()
            ?? throw new InvalidOperationException("Twitch did not return a chat send result.");
    }

    private sealed record SendMessageRequest
    {
        [JsonPropertyName("broadcaster_id")]
        public required string BroadcasterId { get; init; }

        [JsonPropertyName("sender_id")]
        public required string SenderId { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }
    }

    private sealed record SendMessageResponse
    {
        [JsonPropertyName("data")]
        public required ImmutableArray<ChatMessageSendResult> Data { get; init; }
    }
}
