using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed class ChatAnnouncementClient(
    IHttpClientFactory httpClientFactory,
    TwitchEndpointPolicy endpointPolicy
)
{
    private const int _maximumMessageLength = 500;

    private readonly HttpClient _http = httpClientFactory.CreateClient("twitch-helix");

    public async Task<ChatAnnouncementSendResult> SendAsync(
        HelixRequestContext context,
        string broadcasterId,
        string moderatorId,
        string message,
        TwitchAnnouncementColor color,
        CancellationToken cancellationToken
    )
    {
        if (
            string.IsNullOrWhiteSpace(message)
            || message.Length > _maximumMessageLength
            || !Enum.IsDefined(color)
        )
        {
            return new ChatAnnouncementSendResult.Invalid();
        }

        var uri =
            $"{endpointPolicy.HelixEndpoint("chat/announcements").AbsoluteUri}?"
            + QueryString.Create(
                new Dictionary<string, string?>
                {
                    ["broadcaster_id"] = broadcasterId,
                    ["moderator_id"] = moderatorId,
                }
            );
        using var request = HelixRequest.Create(HttpMethod.Post, uri, context);
        request.Content = JsonContent.Create(
            new ChatAnnouncementRequest { Message = message, Color = ToProviderColor(color) }
        );

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.NoContent => new ChatAnnouncementSendResult.Sent(),
                HttpStatusCode.BadRequest => new ChatAnnouncementSendResult.Invalid(),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    new ChatAnnouncementSendResult.PermissionDenied(),
                HttpStatusCode.TooManyRequests => new ChatAnnouncementSendResult.RateLimited(),
                _ when (int)response.StatusCode >= 500 =>
                    new ChatAnnouncementSendResult.Ambiguous(),
                _ => new ChatAnnouncementSendResult.Unexpected(),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new ChatAnnouncementSendResult.Ambiguous();
        }
        catch (IOException)
        {
            return new ChatAnnouncementSendResult.Ambiguous();
        }
        catch (TimeoutException)
        {
            return new ChatAnnouncementSendResult.Ambiguous();
        }
    }

    private static string ToProviderColor(TwitchAnnouncementColor color)
    {
        return color switch
        {
            TwitchAnnouncementColor.Primary => "primary",
            TwitchAnnouncementColor.Blue => "blue",
            TwitchAnnouncementColor.Green => "green",
            TwitchAnnouncementColor.Orange => "orange",
            TwitchAnnouncementColor.Purple => "purple",
            _ => throw new ArgumentOutOfRangeException(
                nameof(color),
                color,
                "Unsupported announcement color."
            ),
        };
    }

    private sealed record ChatAnnouncementRequest
    {
        [JsonPropertyName("message")]
        public required string Message { get; init; }

        [JsonPropertyName("color")]
        public required string Color { get; init; }
    }
}
