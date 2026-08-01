using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed class ChatPinClient(
    IHttpClientFactory httpClientFactory,
    TwitchEndpointPolicy endpointPolicy
)
{
    private readonly HttpClient _http = httpClientFactory.CreateClient("twitch-helix");

    public Task<ChatPinMutationResult> PinAsync(
        HelixRequestContext context,
        string broadcasterId,
        string moderatorId,
        string messageId,
        int? durationSeconds,
        CancellationToken cancellationToken
    )
    {
        if (durationSeconds is { } seconds && seconds is < 30 or > 1800)
        {
            return Task.FromResult<ChatPinMutationResult>(new ChatPinMutationResult.Invalid());
        }

        return MutateAsync(
            HttpMethod.Put,
            Uri(broadcasterId, moderatorId, messageId, durationSeconds),
            context,
            cancellationToken
        );
    }

    public Task<ChatPinMutationResult> UnpinAsync(
        HelixRequestContext context,
        string broadcasterId,
        string moderatorId,
        string messageId,
        CancellationToken cancellationToken
    ) =>
        MutateAsync(
            HttpMethod.Delete,
            Uri(broadcasterId, moderatorId, messageId, null),
            context,
            cancellationToken
        );

    public async Task<ChatPinnedMessageResult> GetAsync(
        HelixRequestContext context,
        string broadcasterId,
        string moderatorId,
        CancellationToken cancellationToken
    )
    {
        using var request = HelixRequest.Create(
            HttpMethod.Get,
            Uri(broadcasterId, moderatorId, null, null),
            context
        );
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var body = await response.Content.ReadFromJsonAsync<PinnedMessageResponse>(
                    cancellationToken
                );
                var current = body?.Data.FirstOrDefault();
                return current is null
                    ? new ChatPinnedMessageResult.Absent()
                    : new ChatPinnedMessageResult.Found(current.MessageId, current.PinnedByUserId);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    new ChatPinnedMessageResult.PermissionDenied(),
                HttpStatusCode.TooManyRequests => new ChatPinnedMessageResult.RateLimited(),
                _ => new ChatPinnedMessageResult.Unavailable(),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or TimeoutException)
        {
            return new ChatPinnedMessageResult.Unavailable();
        }
    }

    private async Task<ChatPinMutationResult> MutateAsync(
        HttpMethod method,
        string uri,
        HelixRequestContext context,
        CancellationToken cancellationToken
    )
    {
        using var request = HelixRequest.Create(method, uri, context);
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.NoContent => new ChatPinMutationResult.Succeeded(),
                HttpStatusCode.BadRequest => new ChatPinMutationResult.Invalid(),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    new ChatPinMutationResult.PermissionDenied(),
                HttpStatusCode.NotFound => new ChatPinMutationResult.NotFound(),
                HttpStatusCode.Conflict => new ChatPinMutationResult.Conflict(),
                HttpStatusCode.TooManyRequests => new ChatPinMutationResult.RateLimited(),
                _ => new ChatPinMutationResult.Ambiguous(),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or TimeoutException)
        {
            return new ChatPinMutationResult.Ambiguous();
        }
    }

    private string Uri(
        string broadcasterId,
        string moderatorId,
        string? messageId,
        int? durationSeconds
    ) =>
        $"{endpointPolicy.HelixEndpoint("chat/pins").AbsoluteUri}?"
        + QueryString.Create(
            new Dictionary<string, string?>
            {
                ["broadcaster_id"] = broadcasterId,
                ["moderator_id"] = moderatorId,
                ["message_id"] = messageId,
                ["duration_seconds"] = durationSeconds?.ToString(),
            }
        );

    private sealed record PinnedMessageResponse
    {
        [JsonPropertyName("data")]
        public required ImmutableArray<PinnedMessage> Data { get; init; }
    }

    private sealed record PinnedMessage
    {
        [JsonPropertyName("message_id")]
        public required string MessageId { get; init; }

        [JsonPropertyName("pinned_by_user_id")]
        public required string PinnedByUserId { get; init; }
    }
}

public abstract record ChatPinMutationResult
{
    private ChatPinMutationResult() { }

    public sealed record Succeeded : ChatPinMutationResult;

    public sealed record Invalid : ChatPinMutationResult;

    public sealed record PermissionDenied : ChatPinMutationResult;

    public sealed record NotFound : ChatPinMutationResult;

    public sealed record Conflict : ChatPinMutationResult;

    public sealed record RateLimited : ChatPinMutationResult;

    public sealed record Ambiguous : ChatPinMutationResult;
}

public abstract record ChatPinnedMessageResult
{
    private ChatPinnedMessageResult() { }

    public sealed record Found(string MessageId, string PinnedByUserId) : ChatPinnedMessageResult;

    public sealed record Absent : ChatPinnedMessageResult;

    public sealed record PermissionDenied : ChatPinnedMessageResult;

    public sealed record RateLimited : ChatPinnedMessageResult;

    public sealed record Unavailable : ChatPinnedMessageResult;
}
