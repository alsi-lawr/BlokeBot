using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BlokeBot.Twitch.Runtime;

public sealed class TwitchHelixChatClient(
    IHttpClientFactory factory,
    IOptions<TwitchBotIdentityOptions> options,
    TwitchHelixApiClient helix
)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http = factory.CreateClient("twitch-helix");
    private readonly TwitchBotIdentityOptions opts = options.Value;

    public async Task<string> CreateChatMessageSubscriptionAsync(
        string accessToken,
        string broadcasterId,
        string botUserId,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        var payload = new
        {
            type = "channel.chat.message",
            version = "1",
            condition = new { broadcaster_user_id = broadcasterId, user_id = botUserId },
            transport = new { method = "websocket", session_id = sessionId },
        };

        using var request = CreateRequest(
            HttpMethod.Post,
            "https://api.twitch.tv/helix/eventsub/subscriptions",
            accessToken
        );
        request.Content = JsonContent.Create(payload);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TwitchEventSubSubscriptionResponse>(
            JsonOptions,
            cancellationToken
        );

        return result?.Data.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException(
                "Twitch did not return an EventSub subscription ID."
            );
    }

    public async Task DeleteEventSubSubscriptionAsync(
        string accessToken,
        string subscriptionId,
        CancellationToken cancellationToken
    )
    {
        var uri =
            "https://api.twitch.tv/helix/eventsub/subscriptions"
            + $"?id={Uri.EscapeDataString(subscriptionId)}";

        using var request = CreateRequest(HttpMethod.Delete, uri, accessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return;

        response.EnsureSuccessStatusCode();
    }

    public async Task<TwitchChatIdentitySet> ResolveChatIdentitiesAsync(
        string channelLogin,
        string botLogin,
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        var channel = TwitchLogin.Normalize(channelLogin);
        var bot = TwitchLogin.Normalize(botLogin);
        var users = await helix.GetUsersByLoginAsync(
            new TwitchHelixRequestContext(opts.ClientId, accessToken),
            [channel, bot],
            cancellationToken
        );
        var broadcaster = users.FirstOrDefault(user =>
            user.Login.Equals(channel, StringComparison.OrdinalIgnoreCase)
        );
        var botUser = users.FirstOrDefault(user =>
            user.Login.Equals(bot, StringComparison.OrdinalIgnoreCase)
        );

        if (string.IsNullOrWhiteSpace(broadcaster?.Id))
            throw new InvalidOperationException(
                $"Twitch channel login '{channelLogin}' was not found."
            );

        if (string.IsNullOrWhiteSpace(botUser?.Id))
            throw new InvalidOperationException($"Twitch bot login '{botLogin}' was not found.");

        return new TwitchChatIdentitySet(broadcaster.Id, botUser.Id);
    }

    public async Task<TwitchChatMessageSendResult> SendChatMessageAsync(
        string accessToken,
        string broadcasterId,
        string senderId,
        string message,
        CancellationToken cancellationToken
    )
    {
        var payload = new
        {
            broadcaster_id = broadcasterId,
            sender_id = senderId,
            message,
        };

        using var request = CreateRequest(
            HttpMethod.Post,
            "https://api.twitch.tv/helix/chat/messages",
            accessToken
        );
        request.Content = JsonContent.Create(payload);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TwitchChatMessageResponse>(
            JsonOptions,
            cancellationToken
        );

        return result?.Data.FirstOrDefault()
            ?? throw new InvalidOperationException("Twitch did not return a chat send result.");
    }

    public async Task<TwitchWhisperSendResult> SendWhisperAsync(
        string accessToken,
        string senderUserId,
        string recipientUserId,
        string message,
        CancellationToken cancellationToken
    )
    {
        var uri =
            "https://api.twitch.tv/helix/whispers?"
            + TwitchQueryString.Create(
                new Dictionary<string, string?>
                {
                    ["from_user_id"] = senderUserId,
                    ["to_user_id"] = recipientUserId,
                }
            );
        using var request = CreateRequest(HttpMethod.Post, uri, accessToken);
        request.Content = JsonContent.Create(new { message });
        using var response = await http.SendAsync(request, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.NoContent => new(TwitchWhisperSendStatus.Accepted, response.StatusCode),
            HttpStatusCode.TooManyRequests => new(
                TwitchWhisperSendStatus.RateLimited,
                response.StatusCode
            ),
            _ => new(TwitchWhisperSendStatus.Rejected, response.StatusCode),
        };
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", opts.ClientId);
        return request;
    }
}
