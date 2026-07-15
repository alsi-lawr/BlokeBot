using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed class HelixClient(IHttpClientFactory httpClientFactory)
{
    private const string _usersEndpoint = "https://api.twitch.tv/helix/users";
    private const string _streamsEndpoint = "https://api.twitch.tv/helix/streams";
    private const string _followersEndpoint = "https://api.twitch.tv/helix/channels/followers";
    private const string _moderatedChannelsEndpoint =
        "https://api.twitch.tv/helix/moderation/channels";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = httpClientFactory.CreateClient("twitch-helix");

    public async Task<HelixUser?> GetCurrentUserAsync(
        HelixRequestContext context,
        CancellationToken cancellationToken
    )
    {
        using var request = HelixRequest.Create(HttpMethod.Get, _usersEndpoint, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<UsersResponse>(
            _jsonOptions,
            cancellationToken
        );
        return payload?.Data.FirstOrDefault();
    }

    public async Task<IReadOnlyList<HelixUser>> GetUsersByLoginAsync(
        HelixRequestContext context,
        IEnumerable<string?> logins,
        CancellationToken cancellationToken
    )
    {
        var normalized = Login.NormalizeMany(logins);
        if (normalized.Length == 0)
        {
            return [];
        }

        var uri =
            $"{_usersEndpoint}?"
            + QueryString.Create(
                normalized.Select(login => new KeyValuePair<string, string?>("login", login))
            );
        using var request = HelixRequest.Create(HttpMethod.Get, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<UsersResponse>(
            _jsonOptions,
            cancellationToken
        );
        return payload?.Data ?? [];
    }

    public async Task<IReadOnlyList<ModeratedChannel>> GetModeratedChannelsAsync(
        HelixRequestContext context,
        string userId,
        CancellationToken cancellationToken
    )
    {
        var channels = new List<ModeratedChannel>();
        string? cursor = null;
        do
        {
            using var request = HelixRequest.Create(
                HttpMethod.Get,
                ModeratedChannelsUri(userId, cursor),
                context
            );
            using var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<ModeratedChannelsResponse>(
                _jsonOptions,
                cancellationToken
            );
            if (payload?.Data is { Count: > 0 } data)
            {
                channels.AddRange(data);
            }

            cursor = payload?.Pagination.Cursor;
        } while (!string.IsNullOrWhiteSpace(cursor));

        return channels;
    }

    public async Task<ModeratedChannelStatus> GetModeratedChannelStatusAsync(
        HelixRequestContext context,
        string userId,
        string broadcasterId,
        CancellationToken cancellationToken
    )
    {
        string? cursor = null;
        do
        {
            using var request = HelixRequest.Create(
                HttpMethod.Get,
                ModeratedChannelsUri(userId, cursor),
                context
            );
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new ModeratedChannelStatus.NeedsAuthorization();
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new ModeratedChannelStatus.MissingPermission();
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ModeratedChannelStatus.Unknown();
            }

            var payload = await response.Content.ReadFromJsonAsync<ModeratedChannelsResponse>(
                _jsonOptions,
                cancellationToken
            );
            if (
                payload?.Data.Any(channel =>
                    string.Equals(channel.BroadcasterId, broadcasterId, StringComparison.Ordinal)
                ) == true
            )
            {
                return new ModeratedChannelStatus.IsModerator();
            }

            cursor = payload?.Pagination.Cursor;
        } while (!string.IsNullOrWhiteSpace(cursor));

        return new ModeratedChannelStatus.NotModerator();
    }

    public async Task<bool> IsStreamLiveAsync(
        HelixRequestContext context,
        string channelLogin,
        CancellationToken cancellationToken
    )
    {
        var uri =
            $"{_streamsEndpoint}?"
            + QueryString.Create([
                new KeyValuePair<string, string?>("user_login", Login.Normalize(channelLogin)),
            ]);
        using var request = HelixRequest.Create(HttpMethod.Get, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<StreamResponse>(
            _jsonOptions,
            cancellationToken
        );
        return payload?.Data.Length > 0;
    }

    public async Task<FollowerStatus> GetFollowerStatusAsync(
        HelixRequestContext context,
        string broadcasterId,
        string userId,
        string moderatorId,
        CancellationToken cancellationToken
    )
    {
        var uri =
            $"{_followersEndpoint}?"
            + QueryString.Create(
                new Dictionary<string, string?>
                {
                    ["broadcaster_id"] = broadcasterId,
                    ["moderator_id"] = moderatorId,
                    ["user_id"] = userId,
                }
            );
        using var request = HelixRequest.Create(HttpMethod.Get, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return new FollowerStatus.Unavailable();
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<FollowerResponse>(
            _jsonOptions,
            cancellationToken
        );
        return payload?.Data.Length > 0
            ? new FollowerStatus.Follows()
            : new FollowerStatus.DoesNotFollow();
    }

    private static string ModeratedChannelsUri(string userId, string? cursor)
    {
        var query = new Dictionary<string, string?> { ["first"] = "100", ["user_id"] = userId };

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            query["after"] = cursor;
        }

        return $"{_moderatedChannelsEndpoint}?{QueryString.Create(query)}";
    }

    private sealed record UsersResponse
    {
        [JsonPropertyName("data")]
        public IReadOnlyList<HelixUser> Data { get; init; } = [];
    }

    private sealed record ModeratedChannelsResponse
    {
        [JsonPropertyName("data")]
        public IReadOnlyList<ModeratedChannel> Data { get; init; } = [];

        [JsonPropertyName("pagination")]
        public Pagination Pagination { get; init; } = new();
    }

    private sealed record StreamResponse
    {
        [JsonPropertyName("data")]
        public required ImmutableArray<StreamItem> Data { get; init; }
    }

    private sealed record FollowerResponse
    {
        [JsonPropertyName("data")]
        public required ImmutableArray<FollowerItem> Data { get; init; }
    }

    private sealed record StreamItem
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("user_id")]
        public required string UserId { get; init; }

        [JsonPropertyName("user_login")]
        public required string UserLogin { get; init; }

        [JsonPropertyName("user_name")]
        public required string UserName { get; init; }

        [JsonPropertyName("game_id")]
        public required string GameId { get; init; }

        [JsonPropertyName("game_name")]
        public required string GameName { get; init; }

        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("tags")]
        public required ImmutableArray<string> Tags { get; init; }

        [JsonPropertyName("viewer_count")]
        public required int ViewerCount { get; init; }

        [JsonPropertyName("started_at")]
        public required DateTimeOffset StartedAt { get; init; }

        [JsonPropertyName("language")]
        public required string Language { get; init; }

        [JsonPropertyName("thumbnail_url")]
        public required string ThumbnailUrl { get; init; }

        [JsonPropertyName("is_mature")]
        public required bool IsMature { get; init; }
    }

    private sealed record FollowerItem
    {
        [JsonPropertyName("user_id")]
        public required string UserId { get; init; }

        [JsonPropertyName("user_login")]
        public required string UserLogin { get; init; }

        [JsonPropertyName("user_name")]
        public required string UserName { get; init; }

        [JsonPropertyName("followed_at")]
        public required DateTimeOffset FollowedAt { get; init; }
    }

    private sealed record Pagination
    {
        [JsonPropertyName("cursor")]
        public string? Cursor { get; init; }
    }
}
