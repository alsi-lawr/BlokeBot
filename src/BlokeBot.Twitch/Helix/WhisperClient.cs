using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed class WhisperClient(IHttpClientFactory httpClientFactory)
{
    private const string _whispersEndpoint = "https://api.twitch.tv/helix/whispers";
    private const int _maximumResponseBodyLength = 1000;

    private readonly HttpClient _http = httpClientFactory.CreateClient("twitch-helix");

    public async Task<WhisperSendResult> SendAsync(
        HelixRequestContext context,
        string senderUserId,
        string recipientUserId,
        string message,
        CancellationToken cancellationToken
    )
    {
        var uri =
            $"{_whispersEndpoint}?"
            + QueryString.Create(
                new Dictionary<string, string?>
                {
                    ["from_user_id"] = senderUserId,
                    ["to_user_id"] = recipientUserId,
                }
            );
        using var request = HelixRequest.Create(HttpMethod.Post, uri, context);
        request.Content = JsonContent.Create(new SendWhisperRequest { Message = message });
        using var response = await _http.SendAsync(request, cancellationToken);
        var responseBody =
            response.StatusCode is HttpStatusCode.NoContent
                ? null
                : await ReadResponseBodyAsync(response, cancellationToken);
        return new WhisperSendResult
        {
            Status = response.StatusCode switch
            {
                HttpStatusCode.NoContent => WhisperSendStatus.Accepted,
                HttpStatusCode.TooManyRequests => WhisperSendStatus.RateLimited,
                _ => WhisperSendStatus.Rejected,
            },
            StatusCode = response.StatusCode,
            ResponseBody = responseBody,
        };
    }

    private static async Task<string?> ReadResponseBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        if (response.Content is null)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        return body.Length <= _maximumResponseBodyLength
            ? body
            : body[.._maximumResponseBodyLength];
    }

    private sealed record SendWhisperRequest
    {
        [JsonPropertyName("message")]
        public required string Message { get; init; }
    }
}
