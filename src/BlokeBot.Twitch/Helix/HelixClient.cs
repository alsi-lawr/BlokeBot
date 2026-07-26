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
    private const string _chatSettingsEndpoint = "https://api.twitch.tv/helix/chat/settings";
    private const string _followersEndpoint = "https://api.twitch.tv/helix/channels/followers";
    private const string _followedChannelsEndpoint =
        "https://api.twitch.tv/helix/channels/followed";
    private const string _moderatedChannelsEndpoint =
        "https://api.twitch.tv/helix/moderation/channels";
    private const string _shoutoutsEndpoint = "https://api.twitch.tv/helix/chat/shoutouts";
    private const string _pollsEndpoint = "https://api.twitch.tv/helix/polls";
    private const string _clipsEndpoint = "https://api.twitch.tv/helix/clips";
    private const string _streamMarkersEndpoint = "https://api.twitch.tv/helix/streams/markers";
    private const string _customRewardsEndpoint =
        "https://api.twitch.tv/helix/channel_points/custom_rewards";
    private const string _redemptionsEndpoint =
        "https://api.twitch.tv/helix/channel_points/custom_rewards/redemptions";
    private const int _streamMarkerLookupPageLimit = 3;

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

    public async Task<HelixShoutoutTarget?> GetShoutoutTargetAsync(
        HelixRequestContext context,
        string login,
        CancellationToken cancellationToken
    )
    {
        var users = await GetUsersByLoginAsync(context, [login], cancellationToken);
        return users.FirstOrDefault() is { } user
            ? new HelixShoutoutTarget(user.Id, user.Login, user.DisplayName)
            : null;
    }

    public async Task<ShoutoutSendResult> SendShoutoutAsync(
        HelixRequestContext context,
        string broadcasterId,
        string moderatorId,
        string targetId,
        CancellationToken cancellationToken
    )
    {
        var uri =
            _shoutoutsEndpoint
            + "?"
            + QueryString.Create(
                new Dictionary<string, string?>
                {
                    ["from_broadcaster_id"] = broadcasterId,
                    ["to_broadcaster_id"] = targetId,
                    ["moderator_id"] = moderatorId,
                }
            );
        using var request = HelixRequest.Create(HttpMethod.Post, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.NoContent => new ShoutoutSendResult.Sent(),
            HttpStatusCode.BadRequest => new ShoutoutSendResult.InvalidTarget(),
            HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests =>
                new ShoutoutSendResult.Cooldown(),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new ShoutoutSendResult.Unauthorized(),
            _ => new ShoutoutSendResult.Unavailable(),
        };
    }

    public async Task<HelixPollLookupOutcome> GetLatestPollAsync(
        HelixRequestContext context,
        string broadcasterId,
        CancellationToken cancellationToken
    )
    {
        var uri =
            _pollsEndpoint
            + "?"
            + QueryString.Create([
                new KeyValuePair<string, string?>("broadcaster_id", broadcasterId),
                new KeyValuePair<string, string?>("first", "1"),
            ]);
        using var request = HelixRequest.Create(HttpMethod.Get, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new HelixPollLookupOutcome.Unavailable();
        }

        var payload = await response.Content.ReadFromJsonAsync<HelixPollResponse>(
            _jsonOptions,
            cancellationToken
        );
        var poll = payload?.Data.FirstOrDefault()?.ToDomain();
        return poll is null
            ? new HelixPollLookupOutcome.NoPoll()
            : new HelixPollLookupOutcome.Found(poll);
    }

    public async Task<HelixPollCreateOutcome> CreatePollAsync(
        HelixRequestContext context,
        string broadcasterId,
        HelixPollCreateRequest poll,
        CancellationToken cancellationToken
    )
    {
        using var request = HelixRequest.Create(HttpMethod.Post, _pollsEndpoint, context);
        request.Content = JsonContent.Create(
            new
            {
                broadcaster_id = broadcasterId,
                title = poll.Title,
                choices = poll.Choices.Select(title => new { title }).ToArray(),
                duration = poll.DurationSeconds,
                channel_points_voting_enabled = poll.ChannelPointsVotingEnabled,
                channel_points_per_vote = poll.ChannelPointsVotingEnabled
                    ? poll.ChannelPointsPerVote
                    : null,
            },
            options: _jsonOptions
        );
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            return IsActivePollConflict(error)
                ? new HelixPollCreateOutcome.ActivePollExists()
                : new HelixPollCreateOutcome.ProviderRejected();
        }
        if (!response.IsSuccessStatusCode)
        {
            return new HelixPollCreateOutcome.ProviderRejected();
        }

        var payload = await response.Content.ReadFromJsonAsync<HelixPollResponse>(
            _jsonOptions,
            cancellationToken
        );
        var created = payload?.Data.FirstOrDefault()?.ToDomain();
        return created is null
            ? new HelixPollCreateOutcome.ProviderRejected()
            : new HelixPollCreateOutcome.Created(created);
    }

    public async Task<HelixClipCreateOutcome> CreateClipAsync(
        HelixRequestContext context,
        string broadcasterId,
        bool hasDelay,
        CancellationToken cancellationToken
    )
    {
        var uri =
            _clipsEndpoint
            + "?"
            + QueryString.Create([
                new KeyValuePair<string, string?>("broadcaster_id", broadcasterId),
                new KeyValuePair<string, string?>("has_delay", hasDelay ? "true" : "false"),
            ]);
        try
        {
            using var request = HelixRequest.Create(HttpMethod.Post, uri, context);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<HelixClipCreateResponse>(
                    _jsonOptions,
                    cancellationToken
                );
                var created = payload?.Data.FirstOrDefault();
                return created is not { Id.Length: > 0, EditUrl.Length: > 0 }
                    ? new HelixClipCreateOutcome.Ambiguous()
                    : new HelixClipCreateOutcome.Created(created.ToDomain());
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new HelixClipCreateOutcome.Unauthorized();
            }
            if ((int)response.StatusCode >= 500)
            {
                return new HelixClipCreateOutcome.Ambiguous();
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            return ClassifyClipFailure(error);
        }
        catch (HttpRequestException)
        {
            return new HelixClipCreateOutcome.Ambiguous();
        }
        catch (JsonException)
        {
            return new HelixClipCreateOutcome.Ambiguous();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HelixClipCreateOutcome.Ambiguous();
        }
    }

    public async Task<HelixClipLookupOutcome> GetClipAsync(
        HelixRequestContext context,
        string clipId,
        CancellationToken cancellationToken
    )
    {
        var uri =
            _clipsEndpoint
            + "?"
            + QueryString.Create([new KeyValuePair<string, string?>("id", clipId)]);
        using var request = HelixRequest.Create(HttpMethod.Get, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new HelixClipLookupOutcome.Unavailable();
        }

        var payload = await response.Content.ReadFromJsonAsync<HelixClipsResponse>(
            _jsonOptions,
            cancellationToken
        );
        var clip = payload?.Data.FirstOrDefault()?.ToDomain();
        return clip is null
            ? new HelixClipLookupOutcome.NotFound()
            : new HelixClipLookupOutcome.Found(clip);
    }

    public async Task<HelixStreamMarkerCreateOutcome> CreateStreamMarkerAsync(
        HelixRequestContext context,
        string broadcasterId,
        string description,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var request = HelixRequest.Create(
                HttpMethod.Post,
                _streamMarkersEndpoint,
                context
            );
            request.Content = JsonContent.Create(
                new { user_id = broadcasterId, description },
                options: _jsonOptions
            );
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<HelixStreamMarkerResponse>(
                    _jsonOptions,
                    cancellationToken
                );
                var marker = payload?.Data.FirstOrDefault();
                return
                    marker is not { Id.Length: > 0, Description.Length: > 0 }
                    || marker.CreatedAt == default
                    ? new HelixStreamMarkerCreateOutcome.Ambiguous()
                    : new HelixStreamMarkerCreateOutcome.Created(marker.ToDomain(null));
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new HelixStreamMarkerCreateOutcome.Unauthorized();
            }
            if ((int)response.StatusCode >= 500)
            {
                return new HelixStreamMarkerCreateOutcome.Ambiguous();
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            return ClassifyMarkerFailure(error);
        }
        catch (HttpRequestException)
        {
            return new HelixStreamMarkerCreateOutcome.Ambiguous();
        }
        catch (JsonException)
        {
            return new HelixStreamMarkerCreateOutcome.Ambiguous();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HelixStreamMarkerCreateOutcome.Ambiguous();
        }
    }

    public async Task<HelixStreamMarkerLookupOutcome> GetStreamMarkersAsync(
        HelixRequestContext context,
        string broadcasterId,
        IReadOnlySet<string> retainedProviderMarkerIds,
        CancellationToken cancellationToken
    )
    {
        var unmatched = retainedProviderMarkerIds.ToHashSet(StringComparer.Ordinal);
        var markers = new List<HelixStreamMarker>();
        string? cursor = null;
        for (var page = 0; page < _streamMarkerLookupPageLimit && unmatched.Count > 0; page++)
        {
            var query = new List<KeyValuePair<string, string?>>
            {
                new("user_id", broadcasterId),
                new("first", "100"),
            };
            if (cursor is not null)
            {
                query.Add(new("after", cursor));
            }

            var uri = _streamMarkersEndpoint + "?" + QueryString.Create(query);
            using var request = HelixRequest.Create(HttpMethod.Get, uri, context);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new HelixStreamMarkerLookupOutcome.Unavailable();
            }

            var payload = await response.Content.ReadFromJsonAsync<HelixStreamMarkersResponse>(
                _jsonOptions,
                cancellationToken
            );
            foreach (
                var marker in (payload?.Data ?? [])
                    .SelectMany(user => user.Videos)
                    .SelectMany(video =>
                        video.Markers.Select(marker => marker.ToDomain(video.VideoId))
                    )
            )
            {
                markers.Add(marker);
                unmatched.Remove(marker.Id);
            }

            cursor = payload?.Pagination?.Cursor;
            if (string.IsNullOrEmpty(cursor))
            {
                break;
            }
        }

        return new HelixStreamMarkerLookupOutcome.Found(markers);
    }

    private static HelixClipCreateOutcome ClassifyClipFailure(string error)
    {
        if (Contains(error, "rerun") || Contains(error, "premiere"))
        {
            return new HelixClipCreateOutcome.RerunOrPremiere();
        }
        if (Contains(error, "vod") || Contains(error, "clip"))
        {
            return new HelixClipCreateOutcome.VodsDisabled();
        }
        if (Contains(error, "live") || Contains(error, "streaming"))
        {
            return new HelixClipCreateOutcome.Offline();
        }
        return new HelixClipCreateOutcome.ProviderRejected();
    }

    private static HelixStreamMarkerCreateOutcome ClassifyMarkerFailure(string error)
    {
        if (Contains(error, "rerun") || Contains(error, "premiere"))
        {
            return new HelixStreamMarkerCreateOutcome.RerunOrPremiere();
        }
        if (Contains(error, "vod"))
        {
            return new HelixStreamMarkerCreateOutcome.VodsDisabled();
        }
        if (Contains(error, "live") || Contains(error, "streaming"))
        {
            return new HelixStreamMarkerCreateOutcome.Offline();
        }
        return new HelixStreamMarkerCreateOutcome.ProviderRejected();
    }

    private static bool Contains(string text, string value)
    {
        return text.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActivePollConflict(string error)
    {
        try
        {
            using var document = JsonDocument.Parse(error);
            return document.RootElement.TryGetProperty("message", out var message)
                && message.GetString() is { } text
                && (
                    text.Contains("active poll", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("poll is already active", StringComparison.OrdinalIgnoreCase)
                );
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<HelixPoll?> EndPollAsync(
        HelixRequestContext context,
        string broadcasterId,
        string pollId,
        HelixPollEndStatus status,
        CancellationToken cancellationToken
    )
    {
        using var request = HelixRequest.Create(HttpMethod.Patch, _pollsEndpoint, context);
        request.Content = JsonContent.Create(
            new
            {
                broadcaster_id = broadcasterId,
                id = pollId,
                status = status is HelixPollEndStatus.Terminated ? "TERMINATED" : "ARCHIVED",
            },
            options: _jsonOptions
        );
        using var response = await _http.SendAsync(request, cancellationToken);
        if (
            response.StatusCode
            is HttpStatusCode.NotFound
                or HttpStatusCode.Forbidden
                or HttpStatusCode.Unauthorized
        )
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<HelixPollResponse>(
            _jsonOptions,
            cancellationToken
        );
        return payload?.Data.FirstOrDefault()?.ToDomain();
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
        return await GetStreamAsync(context, channelLogin, cancellationToken) is not null;
    }

    public async Task<HelixStream?> GetStreamAsync(
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
        return payload?.Data.FirstOrDefault() is { } stream
            ? new HelixStream(stream.Id, stream.UserId, stream.UserLogin, stream.ViewerCount)
            : null;
    }

    public async Task<ChatSettings> GetChatSettingsAsync(
        HelixRequestContext context,
        string broadcasterId,
        CancellationToken cancellationToken
    )
    {
        var uri =
            $"{_chatSettingsEndpoint}?"
            + QueryString.Create([
                new KeyValuePair<string, string?>("broadcaster_id", broadcasterId),
            ]);
        using var request = HelixRequest.Create(HttpMethod.Get, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ChatSettingsResponse>(
            _jsonOptions,
            cancellationToken
        );
        var settings =
            payload?.Data.SingleOrDefault()
            ?? throw new JsonException("Twitch did not return chat settings.");

        return new ChatSettings(
            settings.FollowerMode,
            settings.FollowerModeDuration is { } duration ? TimeSpan.FromMinutes(duration) : null
        );
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

    public async Task<ActiveBotFollowStatus> GetFollowedChannelStatusAsync(
        HelixRequestContext context,
        string userId,
        string broadcasterId,
        CancellationToken cancellationToken
    )
    {
        var uri =
            $"{_followedChannelsEndpoint}?"
            + QueryString.Create(
                new Dictionary<string, string?>
                {
                    ["user_id"] = userId,
                    ["broadcaster_id"] = broadcasterId,
                }
            );
        using var request = HelixRequest.Create(HttpMethod.Get, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return new ActiveBotFollowStatus.Unavailable();
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<FollowerResponse>(
            _jsonOptions,
            cancellationToken
        );
        return payload?.Data.FirstOrDefault() is { } follow
            ? new ActiveBotFollowStatus.Follows(follow.FollowedAt)
            : new ActiveBotFollowStatus.DoesNotFollow();
    }

    public async Task<HelixCustomRewardsLookupOutcome> GetCustomRewardsAsync(
        HelixRequestContext context,
        string broadcasterId,
        bool onlyManageableRewards,
        CancellationToken cancellationToken
    )
    {
        var uri =
            _customRewardsEndpoint
            + "?"
            + QueryString.Create([
                new("broadcaster_id", broadcasterId),
                new("only_manageable_rewards", onlyManageableRewards ? "true" : null),
            ]);
        using var request = HelixRequest.Create(HttpMethod.Get, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        var outcome = await ChannelPointsOutcomeAsync(response, cancellationToken);
        if (outcome is not HelixChannelPointsOutcome.Success)
        {
            return ToCustomRewardsLookupOutcome(outcome);
        }
        var payload = await response.Content.ReadFromJsonAsync<HelixCustomRewardsResponse>(
            _jsonOptions,
            cancellationToken
        );
        return new HelixCustomRewardsLookupOutcome.Found(
            (payload?.Data ?? []).Select(x => x.ToDomain()).ToArray()
        );
    }

    public async Task<(
        HelixChannelPointsOutcome Outcome,
        HelixCustomReward? Reward
    )> CreateCustomRewardAsync(
        HelixRequestContext context,
        string broadcasterId,
        HelixCustomRewardDraft draft,
        CancellationToken cancellationToken
    )
    {
        var uri =
            _customRewardsEndpoint
            + "?"
            + QueryString.Create([new("broadcaster_id", broadcasterId)]);
        using var request = HelixRequest.Create(HttpMethod.Post, uri, context);
        request.Content = JsonContent.Create(
            new
            {
                title = draft.Title,
                prompt = draft.Prompt,
                cost = draft.Cost,
                is_user_input_required = draft.IsUserInputRequired,
                is_max_per_stream_enabled = draft.IsMaxPerStreamEnabled,
                max_per_stream = draft.IsMaxPerStreamEnabled ? draft.MaxPerStream : null,
                is_max_per_user_per_stream_enabled = draft.IsMaxPerUserPerStreamEnabled,
                max_per_user_per_stream = draft.IsMaxPerUserPerStreamEnabled
                    ? draft.MaxPerUserPerStream
                    : null,
                is_global_cooldown_enabled = draft.IsGlobalCooldownEnabled,
                global_cooldown_seconds = draft.IsGlobalCooldownEnabled
                    ? draft.GlobalCooldownSeconds
                    : null,
                should_redemptions_skip_request_queue = draft.ShouldRedemptionsSkipRequestQueue,
                background_color = draft.BackgroundColor,
            },
            options: _jsonOptions
        );
        using var response = await _http.SendAsync(request, cancellationToken);
        var outcome = await ChannelPointsOutcomeAsync(response, cancellationToken);
        if (outcome is not HelixChannelPointsOutcome.Success)
        {
            return (outcome, null);
        }
        var payload = await response.Content.ReadFromJsonAsync<HelixCustomRewardsResponse>(
            _jsonOptions,
            cancellationToken
        );
        return payload?.Data.FirstOrDefault() is { } value
            ? (outcome, value.ToDomain())
            : (new HelixChannelPointsOutcome.Unavailable(), null);
    }

    public async Task<HelixChannelPointsOutcome> UpdateCustomRewardAsync(
        HelixRequestContext context,
        string broadcasterId,
        string rewardId,
        HelixCustomRewardDraft draft,
        bool isEnabled,
        bool isPaused,
        CancellationToken cancellationToken
    )
    {
        var uri =
            _customRewardsEndpoint
            + "?"
            + QueryString.Create([new("broadcaster_id", broadcasterId), new("id", rewardId)]);
        using var request = HelixRequest.Create(HttpMethod.Patch, uri, context);
        request.Content = JsonContent.Create(
            new
            {
                title = draft.Title,
                prompt = draft.Prompt,
                cost = draft.Cost,
                is_user_input_required = draft.IsUserInputRequired,
                is_max_per_stream_enabled = draft.IsMaxPerStreamEnabled,
                max_per_stream = draft.IsMaxPerStreamEnabled ? draft.MaxPerStream : null,
                is_max_per_user_per_stream_enabled = draft.IsMaxPerUserPerStreamEnabled,
                max_per_user_per_stream = draft.IsMaxPerUserPerStreamEnabled
                    ? draft.MaxPerUserPerStream
                    : null,
                is_global_cooldown_enabled = draft.IsGlobalCooldownEnabled,
                global_cooldown_seconds = draft.IsGlobalCooldownEnabled
                    ? draft.GlobalCooldownSeconds
                    : null,
                should_redemptions_skip_request_queue = draft.ShouldRedemptionsSkipRequestQueue,
                background_color = draft.BackgroundColor,
                is_enabled = isEnabled,
                is_paused = isPaused,
            },
            options: _jsonOptions
        );
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ChannelPointsOutcomeAsync(response, cancellationToken);
    }

    public async Task<HelixChannelPointsOutcome> DeleteCustomRewardAsync(
        HelixRequestContext context,
        string broadcasterId,
        string rewardId,
        CancellationToken cancellationToken
    )
    {
        var uri =
            _customRewardsEndpoint
            + "?"
            + QueryString.Create([new("broadcaster_id", broadcasterId), new("id", rewardId)]);
        using var request = HelixRequest.Create(HttpMethod.Delete, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ChannelPointsOutcomeAsync(response, cancellationToken);
    }

    public async Task<HelixRewardRedemptionsLookupOutcome> GetRewardRedemptionsAsync(
        HelixRequestContext context,
        string broadcasterId,
        string rewardId,
        HelixRewardRedemptionStatus status,
        string? cursor,
        CancellationToken cancellationToken
    )
    {
        var uri =
            _redemptionsEndpoint
            + "?"
            + QueryString.Create([
                new("broadcaster_id", broadcasterId),
                new("reward_id", rewardId),
                new("status", RedemptionStatusToken(status)),
                new("first", "50"),
                new("after", cursor),
            ]);
        using var request = HelixRequest.Create(HttpMethod.Get, uri, context);
        using var response = await _http.SendAsync(request, cancellationToken);
        var outcome = await ChannelPointsOutcomeAsync(response, cancellationToken);
        if (outcome is not HelixChannelPointsOutcome.Success)
        {
            return ToRedemptionsLookupOutcome(outcome);
        }
        var payload = await response.Content.ReadFromJsonAsync<HelixRewardRedemptionsResponse>(
            _jsonOptions,
            cancellationToken
        );
        return new HelixRewardRedemptionsLookupOutcome.Found(
            new(
                (payload?.Data ?? []).Select(x => x.ToDomain()).ToArray(),
                payload?.Pagination.Cursor
            )
        );
    }

    public async Task<HelixChannelPointsOutcome> UpdateRedemptionStatusAsync(
        HelixRequestContext context,
        string broadcasterId,
        string rewardId,
        string redemptionId,
        HelixRewardRedemptionStatus status,
        CancellationToken cancellationToken
    )
    {
        var uri =
            _redemptionsEndpoint
            + "?"
            + QueryString.Create([
                new("broadcaster_id", broadcasterId),
                new("reward_id", rewardId),
                new("id", redemptionId),
            ]);
        using var request = HelixRequest.Create(HttpMethod.Patch, uri, context);
        request.Content = JsonContent.Create(
            new { status = RedemptionStatusToken(status) },
            options: _jsonOptions
        );
        using var response = await _http.SendAsync(request, cancellationToken);
        return await ChannelPointsOutcomeAsync(response, cancellationToken);
    }

    private static async Task<HelixChannelPointsOutcome> ChannelPointsOutcomeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        if (response.IsSuccessStatusCode)
        {
            return new HelixChannelPointsOutcome.Success();
        }
        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            return new HelixChannelPointsOutcome.Unauthorized();
        }
        if (response.StatusCode is HttpStatusCode.Forbidden)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return
                body.Contains("affiliate", StringComparison.OrdinalIgnoreCase)
                || body.Contains("partner", StringComparison.OrdinalIgnoreCase)
                ? new HelixChannelPointsOutcome.Ineligible()
                : new HelixChannelPointsOutcome.ExternalReward();
        }
        return new HelixChannelPointsOutcome.Unavailable();
    }

    private static HelixCustomRewardsLookupOutcome ToCustomRewardsLookupOutcome(
        HelixChannelPointsOutcome outcome
    )
    {
        return outcome switch
        {
            HelixChannelPointsOutcome.Unauthorized =>
                new HelixCustomRewardsLookupOutcome.Unauthorized(),
            HelixChannelPointsOutcome.Ineligible =>
                new HelixCustomRewardsLookupOutcome.Ineligible(),
            _ => new HelixCustomRewardsLookupOutcome.Unavailable(),
        };
    }

    private static HelixRewardRedemptionsLookupOutcome ToRedemptionsLookupOutcome(
        HelixChannelPointsOutcome outcome
    )
    {
        return outcome switch
        {
            HelixChannelPointsOutcome.Unauthorized =>
                new HelixRewardRedemptionsLookupOutcome.Unauthorized(),
            HelixChannelPointsOutcome.Ineligible =>
                new HelixRewardRedemptionsLookupOutcome.Ineligible(),
            _ => new HelixRewardRedemptionsLookupOutcome.Unavailable(),
        };
    }

    private static string RedemptionStatusToken(HelixRewardRedemptionStatus status)
    {
        return status switch
        {
            HelixRewardRedemptionStatus.Unfulfilled => "UNFULFILLED",
            HelixRewardRedemptionStatus.Fulfilled => "FULFILLED",
            HelixRewardRedemptionStatus.Canceled => "CANCELED",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
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

    private sealed record ChatSettingsResponse
    {
        [JsonPropertyName("data")]
        public required ImmutableArray<ChatSettingsItem> Data { get; init; }
    }

    private sealed record ChatSettingsItem
    {
        [JsonPropertyName("follower_mode")]
        public required bool FollowerMode { get; init; }

        [JsonPropertyName("follower_mode_duration")]
        public required int? FollowerModeDuration { get; init; }
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
