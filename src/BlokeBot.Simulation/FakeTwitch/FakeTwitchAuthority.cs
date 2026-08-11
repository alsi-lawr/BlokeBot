using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Simulation.FakeTwitch;

/// <summary>Defines the deterministic state provided by a fake Twitch host.</summary>
public sealed record FakeTwitchScenarioDefinition
{
    public const string ReadyDashboardName = "ready-dashboard";

    public required string Name { get; init; }

    public required string ClientId { get; init; }

    public required FakeTwitchUser AuthorizedUser { get; init; }

    public required FakeTwitchUser BotUser { get; init; }

    public required IReadOnlySet<string> GrantedScopes { get; init; }

    public static FakeTwitchScenarioDefinition ReadyDashboard { get; } =
        new()
        {
            Name = ReadyDashboardName,
            ClientId = "fake-twitch-client",
            AuthorizedUser = new("1000", "samplechannel", "Sample Channel", "affiliate"),
            BotUser = new("2000", "blokebot", "BlokeBot", string.Empty),
            GrantedScopes = ImmutableHashSet.Create(
                StringComparer.Ordinal,
                "channel:bot",
                "user:bot",
                "chat:edit",
                "chat:read",
                "user:read:chat",
                "user:write:chat",
                "user:read:moderated_channels",
                "moderator:read:chatters",
                "moderator:read:followers",
                "moderator:manage:announcements",
                "moderator:manage:chat_messages",
                "moderator:read:shoutouts",
                "moderator:manage:shoutouts",
                "user:manage:whispers",
                "user:read:follows",
                "channel:read:polls",
                "channel:manage:polls",
                "clips:edit",
                "channel:manage:broadcast",
                "channel:manage:raids",
                "channel:read:redemptions",
                "channel:manage:redemptions",
                "channel:read:predictions",
                "channel:manage:predictions",
                "channel:read:subscriptions",
                "bits:read",
                "channel:read:hype_train"
            ),
        };
}

/// <summary>Defines a fake Twitch identity.</summary>
public sealed record FakeTwitchUser(
    string Id,
    string Login,
    string DisplayName,
    string BroadcasterType
);

public sealed record FakeTwitchClip(string Id, string Url, string EditUrl);

public sealed record FakeTwitchMarker(
    string Id,
    string Description,
    int PositionSeconds,
    DateTimeOffset CreatedAt,
    string Url
);

/// <summary>Records one ordered fake-provider interaction.</summary>
public sealed record FakeTwitchTranscriptEntry(string Id, string Kind, string Detail);

/// <summary>Owns one scenario's deterministic OAuth, Helix, and EventSub state.</summary>
public sealed class FakeTwitchAuthority
{
    public const string BotAccessToken = "fake-bot-access-token";
    public const string BroadcasterAccessToken = "fake-broadcaster-access-token";

    private readonly object _gate = new();
    private readonly Dictionary<string, AuthorizationCode> _codes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AccessGrant> _accessTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AccessGrant> _refreshTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FakeTwitchSubscription> _subscriptions = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, string> _subscriptionSecrets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FakeTwitchClip> _clips = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FakeTwitchMarker> _markers = new(StringComparer.Ordinal);
    private readonly List<FakeTwitchTranscriptEntry> _transcript = [];
    private int _nextId;

    public FakeTwitchAuthority(FakeTwitchScenarioDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _accessTokens.Add(
            BotAccessToken,
            new AccessGrant(Definition.BotUser, Definition.GrantedScopes)
        );
        _accessTokens.Add(
            BroadcasterAccessToken,
            new AccessGrant(Definition.AuthorizedUser, Definition.GrantedScopes)
        );
    }

    public FakeTwitchScenarioDefinition Definition { get; }

    public IReadOnlyList<FakeTwitchTranscriptEntry> Transcript
    {
        get
        {
            lock (_gate)
            {
                return [.. _transcript];
            }
        }
    }

    public IReadOnlyList<FakeTwitchSubscription> ActiveSubscriptions
    {
        get
        {
            lock (_gate)
            {
                return [.. _subscriptions.Values];
            }
        }
    }

    public string Authorize(string clientId, string redirectUri, IReadOnlySet<string> scopes)
    {
        if (
            !string.Equals(clientId, Definition.ClientId, StringComparison.Ordinal)
            || !scopes.IsSubsetOf(Definition.GrantedScopes)
        )
        {
            throw new FakeTwitchProtocolException(HttpStatusCode.Forbidden, "access_denied");
        }

        lock (_gate)
        {
            var code = NextId("code");
            var user = scopes.Contains("user:bot") ? Definition.BotUser : Definition.AuthorizedUser;
            _codes.Add(code, new AuthorizationCode(clientId, redirectUri, scopes, user));
            Record("oauth.authorize", user.Login);
            return code;
        }
    }

    public FakeTwitchToken Token(
        string clientId,
        string grantType,
        string? code,
        string? refreshToken,
        string? redirectUri
    )
    {
        if (!string.Equals(clientId, Definition.ClientId, StringComparison.Ordinal))
        {
            throw new FakeTwitchProtocolException(HttpStatusCode.Unauthorized, "invalid_client");
        }

        lock (_gate)
        {
            return grantType switch
            {
                "authorization_code" => ExchangeCode(code, redirectUri),
                "refresh_token" => Refresh(refreshToken),
                "client_credentials" => AppToken(),
                _ => throw new FakeTwitchProtocolException(
                    HttpStatusCode.BadRequest,
                    "unsupported_grant"
                ),
            };
        }
    }

    public FakeTwitchTokenValidation Validate(string? accessToken)
    {
        lock (_gate)
        {
            if (!TryGetGrant(accessToken, out var grant) || grant.User is null)
            {
                throw new FakeTwitchProtocolException(HttpStatusCode.Unauthorized, "invalid_token");
            }

            Record("oauth.validate", grant.User.Login);
            return new(grant.User.Id, grant.User.Login, grant.Scopes);
        }
    }

    public FakeTwitchUser RequireUserToken(HttpRequest request, params string[] scopes)
    {
        lock (_gate)
        {
            switch
                (
                    string.Equals(
                        request.Headers["Client-Id"],
                        Definition.ClientId,
                        StringComparison.Ordinal
                    ),
                    TryGetGrant(ReadBearerToken(request), out var grant),
                    grant is { User: not null }
                )

            {
                case (false, _, _):
                case (_, false, _):
                case (_, _, false):
                    throw new FakeTwitchProtocolException(
                        HttpStatusCode.Unauthorized,
                        "invalid_token"
                    );
            }

            return scopes.Any(scope => !grant.Scopes.Contains(scope)) switch
            {
                true => throw new FakeTwitchProtocolException(
                    HttpStatusCode.Forbidden,
                    "missing_scope"
                ),
                false => grant.User!,
            };
        }
    }

    public void RequireAppToken(HttpRequest request)
    {
        lock (_gate)
        {
            if (
                !string.Equals(
                    request.Headers["Client-Id"],
                    Definition.ClientId,
                    StringComparison.Ordinal
                )
                || !TryGetGrant(ReadBearerToken(request), out var grant)
                || grant.User is not null
            )
            {
                throw new FakeTwitchProtocolException(
                    HttpStatusCode.Unauthorized,
                    "invalid_app_token"
                );
            }
        }
    }

    public string SubscribeWebhook(
        HttpRequest request,
        string type,
        string version,
        IReadOnlyDictionary<string, string> condition,
        string callback,
        string secret
    )
    {
        RequireAppToken(request);
        var botSubscription =
            type
            is "channel.chat.message"
                or "channel.shoutout.create"
                or "channel.shoutout.receive";
        var raidSubscription = type is "channel.raid";
        var raidConditionKey =
            raidSubscription && condition.ContainsKey("from_broadcaster_user_id")
                ? "from_broadcaster_user_id"
                : "to_broadcaster_user_id";
        var channelUpdateSubscription = type is "channel.update";
        var broadcasterSubscription =
            channelUpdateSubscription
            || type
                is "channel.poll.begin"
                    or "channel.poll.progress"
                    or "channel.poll.end"
                    or "channel.prediction.begin"
                    or "channel.prediction.progress"
                    or "channel.prediction.lock"
                    or "channel.prediction.end"
                    or "channel.cheer"
                    or "channel.channel_points_custom_reward_redemption.add"
                    or "channel.channel_points_custom_reward_redemption.update";
        if (
            version != (channelUpdateSubscription ? "2" : "1")
            || (!botSubscription && !raidSubscription && !broadcasterSubscription)
            || !condition.TryGetValue(
                raidSubscription ? raidConditionKey : "broadcaster_user_id",
                out var broadcasterId
            )
            || broadcasterId != Definition.AuthorizedUser.Id
            || (
                botSubscription
                && (
                    !condition.TryGetValue(
                        type == "channel.chat.message" ? "user_id" : "moderator_user_id",
                        out var botId
                    )
                    || botId != Definition.BotUser.Id
                )
            )
            || string.IsNullOrWhiteSpace(callback)
            || string.IsNullOrWhiteSpace(secret)
        )
        {
            throw new FakeTwitchProtocolException(
                HttpStatusCode.BadRequest,
                "invalid_subscription"
            );
        }

        var id = NextId("subscription");
        var subscription = new FakeTwitchSubscription
        {
            Id = id,
            Type = type,
            Method = "webhook",
            Callback = callback,
            Status = "webhook_callback_verification_pending",
            Version = version,
            Condition = condition,
        };
        lock (_gate)
        {
            _subscriptions.Add(id, subscription);
            _subscriptionSecrets.Add(id, secret);
            Record("eventsub.subscribe", $"{type}:{id}");
        }

        _ = DeliverInitialWebhookAsync(subscription, secret);

        return id;
    }

    public async Task DeliverDuplicateNotificationAsync(
        string subscriptionId,
        CancellationToken cancellationToken
    )
    {
        var (subscription, secret) = GetSubscriptionDelivery(subscriptionId);
        var body =
            NotificationBodies(subscription).FirstOrDefault()
            ?? throw new InvalidOperationException(
                "The fake EventSub subscription has no deterministic notification."
            );
        var messageId = $"fake-eventsub-duplicate-{subscriptionId}";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await SendWebhookAsync(
                subscription.Callback,
                secret,
                messageId,
                "notification",
                subscription.Type,
                subscription.Version,
                body,
                cancellationToken
            );
            _ = response.EnsureSuccessStatusCode();
        }

        lock (_gate)
        {
            Record("eventsub.duplicate", subscriptionId);
        }
    }

    public async Task RevokeAuthorizationAsync(
        string subscriptionId,
        CancellationToken cancellationToken
    )
    {
        FakeTwitchSubscription revoked;
        string secret;
        lock (_gate)
        {
            if (
                !_subscriptions.TryGetValue(subscriptionId, out var subscription)
                || !_subscriptionSecrets.TryGetValue(subscriptionId, out secret!)
            )
            {
                throw new ArgumentException(
                    "The fake EventSub subscription does not exist.",
                    nameof(subscriptionId)
                );
            }

            revoked = subscription with { Status = "authorization_revoked" };
            _subscriptions[subscriptionId] = revoked;
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(
            new { subscription = SubscriptionPayload(revoked) }
        );
        using var response = await SendWebhookAsync(
            revoked.Callback,
            secret,
            $"fake-eventsub-revocation-{subscriptionId}",
            "revocation",
            revoked.Type,
            revoked.Version,
            body,
            cancellationToken
        );
        _ = response.EnsureSuccessStatusCode();
        lock (_gate)
        {
            Record("eventsub.revoke", subscriptionId);
        }
    }

    public void RequireAccessToken(HttpRequest request)
    {
        lock (_gate)
        {
            if (
                !string.Equals(
                    request.Headers["Client-Id"],
                    Definition.ClientId,
                    StringComparison.Ordinal
                ) || !TryGetGrant(ReadBearerToken(request), out _)
            )
            {
                throw new FakeTwitchProtocolException(HttpStatusCode.Unauthorized, "invalid_token");
            }
        }
    }

    public FakeTwitchClip CreateClip(HttpRequest request, string broadcasterId)
    {
        var user = RequireUserToken(request, "clips:edit");
        if (user.Id != Definition.AuthorizedUser.Id || broadcasterId != user.Id)
        {
            throw new FakeTwitchProtocolException(HttpStatusCode.BadRequest, "invalid_broadcaster");
        }
        lock (_gate)
        {
            var id = $"fake-clip-{_clips.Count + 1:D4}";
            var clip = new FakeTwitchClip(
                id,
                $"https://clips.twitch.tv/{id}",
                $"https://clips.twitch.tv/{id}/edit"
            );
            _clips.Add(id, clip);
            Record("helix.clip.create", id);
            return clip;
        }
    }

    public IReadOnlyList<FakeTwitchClip> Clips(HttpRequest request, IReadOnlyList<string?> ids)
    {
        RequireAccessToken(request);
        lock (_gate)
        {
            return ids.Where(id => id is not null && _clips.ContainsKey(id))
                .Select(id => _clips[id!])
                .ToArray();
        }
    }

    public FakeTwitchMarker CreateMarker(
        HttpRequest request,
        string broadcasterId,
        string description
    )
    {
        var user = RequireUserToken(request, "channel:manage:broadcast");
        if (
            user.Id != Definition.AuthorizedUser.Id
            || broadcasterId != user.Id
            || string.IsNullOrWhiteSpace(description)
        )
        {
            throw new FakeTwitchProtocolException(HttpStatusCode.BadRequest, "invalid_marker");
        }
        lock (_gate)
        {
            var id = $"fake-marker-{_markers.Count + 1:D4}";
            var marker = new FakeTwitchMarker(
                id,
                description.Trim(),
                42,
                new DateTimeOffset(2026, 7, 15, 12, 0, 42, TimeSpan.Zero),
                "https://twitch.tv/videos/fake-video?t=42s"
            );
            _markers.Add(id, marker);
            Record("helix.marker.create", id);
            return marker;
        }
    }

    public IReadOnlyList<FakeTwitchMarker> Markers(HttpRequest request)
    {
        _ = RequireUserToken(request, "channel:manage:broadcast");
        lock (_gate)
        {
            return [.. _markers.Values];
        }
    }

    public void Unsubscribe(HttpRequest request, string id)
    {
        RequireAppToken(request);
        lock (_gate)
        {
            if (!_subscriptions.Remove(id))
            {
                throw new FakeTwitchProtocolException(
                    HttpStatusCode.NotFound,
                    "unknown_subscription"
                );
            }

            _ = _subscriptionSecrets.Remove(id);
            Record("eventsub.unsubscribe", id);
        }
    }

    public IReadOnlyList<FakeTwitchUser> Users(
        IReadOnlyList<string?> logins,
        FakeTwitchUser current
    )
    {
        var users = new[] { Definition.AuthorizedUser, Definition.BotUser };
        return logins.Count == 0
            ? [current]
            : users
                .Where(user => logins.Contains(user.Login, StringComparer.OrdinalIgnoreCase))
                .ToArray();
    }

    public void RecordChatMessage(
        HttpRequest request,
        string broadcasterId,
        string senderId,
        string message
    )
    {
        lock (_gate)
        {
            if (
                !string.Equals(
                    request.Headers["Client-Id"],
                    Definition.ClientId,
                    StringComparison.Ordinal
                ) || !TryGetGrant(ReadBearerToken(request), out var grant)
            )
            {
                throw new FakeTwitchProtocolException(HttpStatusCode.Unauthorized, "invalid_token");
            }

            var validSender = grant.User is null
                ? senderId == Definition.BotUser.Id
                : senderId == grant.User.Id && grant.Scopes.Contains("user:write:chat");
            if (
                broadcasterId != Definition.AuthorizedUser.Id
                || !validSender
                || string.IsNullOrWhiteSpace(message)
            )
            {
                throw new FakeTwitchProtocolException(
                    HttpStatusCode.BadRequest,
                    "invalid_chat_message"
                );
            }

            Record("helix.chat.message", message);
        }
    }

    public string NextMessageId()
    {
        lock (_gate)
        {
            return NextId("message");
        }
    }

    private FakeTwitchToken ExchangeCode(string? code, string? redirectUri) =>
        string.IsNullOrWhiteSpace(code)
        || !_codes.Remove(code, out var authorization)
        || !string.Equals(authorization.RedirectUri, redirectUri, StringComparison.Ordinal)
            ? throw new FakeTwitchProtocolException(HttpStatusCode.BadRequest, "invalid_code")
            : UserToken(authorization.User, authorization.Scopes, "oauth.exchange");

    private FakeTwitchToken Refresh(string? refreshToken) =>
        string.IsNullOrWhiteSpace(refreshToken)
        || !_refreshTokens.Remove(refreshToken, out var grant)
            ? throw new FakeTwitchProtocolException(
                HttpStatusCode.BadRequest,
                "invalid_refresh_token"
            )
            : UserToken(grant.User!, grant.Scopes, "oauth.refresh");

    private FakeTwitchToken UserToken(
        FakeTwitchUser user,
        IReadOnlySet<string> scopes,
        string transcriptKind
    )
    {
        var access = NextId("access");
        var refresh = NextId("refresh");
        var grant = new AccessGrant(user, scopes);
        _accessTokens.Add(access, grant);
        _refreshTokens.Add(refresh, grant);
        Record(transcriptKind, user.Login);
        return new(access, refresh, 3600);
    }

    private FakeTwitchToken AppToken()
    {
        const string Token = "fake-app-token";
        _accessTokens[Token] = new(null, new HashSet<string>());
        Record("oauth.app-token", Definition.ClientId);
        return new(Token, string.Empty, 3600);
    }

    private bool TryGetGrant(string? token, out AccessGrant grant)
    {
        grant = null!;
        return !string.IsNullOrWhiteSpace(token) && _accessTokens.TryGetValue(token, out grant!);
    }

    private string NextId(string kind)
    {
        _nextId++;
        return $"{Definition.Name}-{kind}-{_nextId:D4}";
    }

    private void Record(string kind, string detail) =>
        _transcript.Add(new(NextId("transcript"), kind, detail));

    private (FakeTwitchSubscription Subscription, string Secret) GetSubscriptionDelivery(
        string subscriptionId
    )
    {
        lock (_gate)
        {
            if (
                _subscriptions.TryGetValue(subscriptionId, out var subscription)
                && _subscriptionSecrets.TryGetValue(subscriptionId, out var secret)
            )
            {
                return (subscription, secret);
            }
        }

        throw new ArgumentException(
            "The fake EventSub subscription does not exist.",
            nameof(subscriptionId)
        );
    }

    private async Task DeliverInitialWebhookAsync(
        FakeTwitchSubscription subscription,
        string secret
    )
    {
        try
        {
            var challenge = $"fake-challenge-{subscription.Id}";
            var verificationBody = JsonSerializer.SerializeToUtf8Bytes(
                new { challenge, subscription = SubscriptionPayload(subscription) }
            );
            using (
                var verification = await SendWebhookAsync(
                    subscription.Callback,
                    secret,
                    $"fake-eventsub-verification-{subscription.Id}",
                    "webhook_callback_verification",
                    subscription.Type,
                    subscription.Version,
                    verificationBody,
                    CancellationToken.None
                )
            )
            {
                if (
                    verification.StatusCode is not HttpStatusCode.OK
                    || !string.Equals(
                        await verification.Content.ReadAsStringAsync(),
                        challenge,
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new InvalidOperationException(
                        "The fake EventSub callback challenge was rejected."
                    );
                }
            }

            lock (_gate)
            {
                if (_subscriptions.TryGetValue(subscription.Id, out var pending))
                {
                    subscription = pending with { Status = "enabled" };
                    _subscriptions[subscription.Id] = subscription;
                    Record("eventsub.challenge", subscription.Type);
                }
            }

            var deliveryNumber = 0;
            foreach (var body in NotificationBodies(subscription))
            {
                var messageId = $"fake-eventsub-{subscription.Type}-{++deliveryNumber:D4}";
                using var response = await SendWebhookAsync(
                    subscription.Callback,
                    secret,
                    messageId,
                    "notification",
                    subscription.Type,
                    subscription.Version,
                    body,
                    CancellationToken.None
                );
                _ = response.EnsureSuccessStatusCode();
                lock (_gate)
                {
                    Record("eventsub.deliver", subscription.Type);
                }
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                Record("eventsub.delivery.error", exception.GetType().Name);
            }
        }
    }

    private static async Task<HttpResponseMessage> SendWebhookAsync(
        string callback,
        string secret,
        string messageId,
        string messageType,
        string subscriptionType,
        string subscriptionVersion,
        byte[] body,
        CancellationToken cancellationToken
    )
    {
        var timestamp = SimulationMode.Now.ToString("O");
        var prefix = Encoding.UTF8.GetBytes(messageId + timestamp);
        var signed = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signed, 0);
        body.CopyTo(signed, prefix.Length);
        var signature =
            "sha256="
            + Convert
                .ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed))
                .ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, callback)
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("Twitch-Eventsub-Message-Id", messageId);
        request.Headers.Add("Twitch-Eventsub-Message-Type", messageType);
        request.Headers.Add("Twitch-Eventsub-Message-Timestamp", timestamp);
        request.Headers.Add("Twitch-Eventsub-Message-Signature", signature);
        request.Headers.Add("Twitch-Eventsub-Subscription-Type", subscriptionType);
        request.Headers.Add("Twitch-Eventsub-Subscription-Version", subscriptionVersion);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        return await client.SendAsync(request, cancellationToken);
    }

    private static object SubscriptionPayload(FakeTwitchSubscription subscription) =>
        new
        {
            id = subscription.Id,
            status = subscription.Status,
            type = subscription.Type,
            version = subscription.Version,
            condition = subscription.Condition,
            transport = new { method = subscription.Method, callback = subscription.Callback },
            created_at = "2026-08-03T12:00:00Z",
            cost = 0,
        };

    private IEnumerable<byte[]> NotificationBodies(FakeTwitchSubscription subscription) =>
        subscription.Type switch
        {
            "channel.chat.message" => ChatEvents(subscription),
            "channel.update" => ChannelUpdateEvents(subscription),
            "channel.poll.begin" => PollEvents(subscription),
            _ => [],
        };

    private IEnumerable<byte[]> ChatEvents(FakeTwitchSubscription subscription)
    {
        yield return Notification(
            subscription,
            new
            {
                broadcaster_user_id = Definition.AuthorizedUser.Id,
                broadcaster_user_login = Definition.AuthorizedUser.Login,
                chatter_user_id = "3000",
                chatter_user_login = "nightowl",
                message_id = "chat-message-0001",
                message = new { text = "!hello" },
                badges = Array.Empty<object>(),
            }
        );
        yield return Notification(
            subscription,
            new
            {
                broadcaster_user_id = Definition.AuthorizedUser.Id,
                broadcaster_user_login = Definition.AuthorizedUser.Login,
                chatter_user_id = "4000",
                chatter_user_login = "channelmod",
                message_id = "chat-message-0002",
                message = new { text = "!mod" },
                badges = new[]
                {
                    new
                    {
                        set_id = "moderator",
                        id = "1",
                        info = "",
                    },
                },
            }
        );
        yield return Notification(
            subscription,
            new
            {
                broadcaster_user_id = Definition.AuthorizedUser.Id,
                broadcaster_user_login = Definition.AuthorizedUser.Login,
                chatter_user_id = "3000",
                chatter_user_login = "nightowl",
                message_id = "chat-message-0003",
                message = new { text = "!welcome" },
                badges = Array.Empty<object>(),
            }
        );
    }

    private IEnumerable<byte[]> ChannelUpdateEvents(FakeTwitchSubscription subscription)
    {
        yield return Notification(
            subscription,
            new
            {
                broadcaster_user_id = Definition.AuthorizedUser.Id,
                broadcaster_user_login = Definition.AuthorizedUser.Login,
                broadcaster_user_name = Definition.AuthorizedUser.DisplayName,
                title = "Bingo night",
                language = "en",
                category_id = "509658",
                category_name = "Just Chatting",
                content_classification_labels = Array.Empty<string>(),
            }
        );
    }

    private IEnumerable<byte[]> PollEvents(FakeTwitchSubscription subscription)
    {
        yield return Notification(
            subscription,
            new
            {
                id = "poll-0001",
                broadcaster_user_id = Definition.AuthorizedUser.Id,
                broadcaster_user_login = Definition.AuthorizedUser.Login,
                title = "Ready poll",
                choices = new[]
                {
                    new
                    {
                        id = "choice-0001",
                        title = "Ready",
                        votes = 0,
                        channel_points_votes = 0,
                    },
                },
                status = "ACTIVE",
                started_at = "2026-07-15T12:00:00Z",
                ends_at = "2026-07-15T12:05:00Z",
            }
        );
    }

    private static byte[] Notification(FakeTwitchSubscription subscription, object @event) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new { subscription = SubscriptionPayload(subscription), @event }
        );

    private static string? ReadBearerToken(HttpRequest request)
    {
        const string Prefix = "Bearer ";
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? header[Prefix.Length..]
            : null;
    }

    private sealed record AuthorizationCode(
        string ClientId,
        string RedirectUri,
        IReadOnlySet<string> Scopes,
        FakeTwitchUser User
    );

    private sealed record AccessGrant(FakeTwitchUser? User, IReadOnlySet<string> Scopes);
}

/// <summary>Maps a fake Twitch provider only when a host explicitly opts into it.</summary>
public static class FakeTwitchHostingExtensions
{
    public static IServiceCollection AddFakeTwitch(
        this IServiceCollection services,
        FakeTwitchScenarioDefinition scenario
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.AddSingleton(new FakeTwitchAuthority(scenario));
        return services;
    }

    public static IServiceCollection AddFakeTwitch(
        this IServiceCollection services,
        FakeTwitchAuthority authority
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(authority);
        _ = services.AddSingleton(authority);
        return services;
    }

    public static WebApplication MapFakeTwitch(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var authority = app.Services.GetRequiredService<FakeTwitchAuthority>();

        _ = app.MapGet("/oauth2/authorize", (HttpRequest request) => Authorize(authority, request));
        _ = app.MapPost("/oauth2/token", (HttpRequest request) => TokenAsync(authority, request));
        _ = app.MapGet("/oauth2/validate", (HttpRequest request) => Validate(authority, request));
        _ = app.MapGet("/helix/users", (HttpRequest request) => Users(authority, request));
        _ = app.MapGet("/helix/streams", (HttpRequest request) => Streams(authority, request));
        _ = app.MapPost("/helix/clips", (HttpRequest request) => CreateClip(authority, request));
        _ = app.MapGet("/helix/clips", (HttpRequest request) => Clips(authority, request));
        _ = app.MapPost(
            "/helix/streams/markers",
            (HttpRequest request) => CreateMarkerAsync(authority, request)
        );
        _ = app.MapGet(
            "/helix/streams/markers",
            (HttpRequest request) => Markers(authority, request)
        );
        _ = app.MapGet(
            "/profile-images/{login}.svg",
            (string login) => ProfileImage(authority, login)
        );
        _ = app.MapGet(
            "/helix/channels/followers",
            (HttpRequest request) => Followers(authority, request)
        );
        _ = app.MapGet(
            "/helix/moderation/channels",
            (HttpRequest request) => ModeratedChannels(authority, request)
        );
        _ = app.MapGet(
            "/helix/chat/settings",
            (HttpRequest request) => ChatSettings(authority, request)
        );
        _ = app.MapGet(
            "/helix/chat/chatters",
            (HttpRequest request) => Chatters(authority, request)
        );
        _ = app.MapPost(
            "/helix/eventsub/subscriptions",
            (HttpRequest request) => SubscribeAsync(authority, request)
        );
        _ = app.MapGet(
            "/helix/eventsub/subscriptions",
            (HttpRequest request) => ListSubscriptions(authority, request)
        );
        _ = app.MapDelete(
            "/helix/eventsub/subscriptions",
            (HttpRequest request) => Unsubscribe(authority, request)
        );
        _ = app.MapPost(
            "/helix/chat/messages",
            (HttpRequest request) => ChatMessageAsync(authority, request)
        );
        _ = app.MapMethods(
            "/oauth2/{**path}",
            ["GET", "POST", "PUT", "PATCH", "DELETE"],
            (HttpRequest request) => Unsupported(request)
        );
        _ = app.MapMethods(
            "/helix/{**path}",
            ["GET", "POST", "PUT", "PATCH", "DELETE"],
            (HttpRequest request) => Unsupported(request)
        );
        return app;
    }

    private static IResult Authorize(FakeTwitchAuthority authority, HttpRequest request)
    {
        try
        {
            var redirect = request.Query["redirect_uri"].ToString();
            var state = request.Query["state"].ToString();
            var scopes = request
                .Query["scope"]
                .ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);
            if (
                request.Query["response_type"] != "code"
                || string.IsNullOrWhiteSpace(redirect)
                || string.IsNullOrWhiteSpace(state)
            )
            {
                throw new FakeTwitchProtocolException(
                    HttpStatusCode.BadRequest,
                    "invalid_authorize_request"
                );
            }

            var code = authority.Authorize(request.Query["client_id"].ToString(), redirect, scopes);
            var callback = new UriBuilder(redirect)
            {
                Query = $"code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}",
            };
            return Results.Redirect(callback.Uri.AbsoluteUri);
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static async Task<IResult> TokenAsync(
        FakeTwitchAuthority authority,
        HttpRequest request
    )
    {
        var form = await request.ReadFormAsync();
        try
        {
            var token = authority.Token(
                form["client_id"].ToString(),
                form["grant_type"].ToString(),
                form["code"].ToString(),
                form["refresh_token"].ToString(),
                form["redirect_uri"].ToString()
            );
            return Results.Json(
                new
                {
                    access_token = token.AccessToken,
                    refresh_token = token.RefreshToken,
                    expires_in = token.ExpiresIn,
                    token_type = "bearer",
                }
            );
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static IResult Validate(FakeTwitchAuthority authority, HttpRequest request)
    {
        try
        {
            var validation = authority.Validate(ReadOAuthToken(request));
            return Results.Json(
                new
                {
                    client_id = authority.Definition.ClientId,
                    user_id = validation.UserId,
                    login = validation.Login,
                    scopes = validation.Scopes,
                }
            );
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static IResult Users(FakeTwitchAuthority authority, HttpRequest request)
    {
        try
        {
            var current = authority.RequireUserToken(request);
            return Results.Json(
                new
                {
                    data = authority
                        .Users(request.Query["login"].ToArray(), current)
                        .Select(user => ToHelixUser(user, request)),
                }
            );
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static IResult Streams(FakeTwitchAuthority authority, HttpRequest request)
    {
        try
        {
            authority.RequireAccessToken(request);
            var userId = request.Query["user_id"].ToString();
            var live =
                string.IsNullOrWhiteSpace(userId)
                || userId == authority.Definition.AuthorizedUser.Id;
            return Results.Json(
                new
                {
                    data = live
                        ? new[]
                        {
                            new
                            {
                                id = "stream-0001",
                                user_id = authority.Definition.AuthorizedUser.Id,
                                user_login = authority.Definition.AuthorizedUser.Login,
                                user_name = authority.Definition.AuthorizedUser.DisplayName,
                                game_id = "game-0001",
                                game_name = "Fake Game",
                                type = "live",
                                title = "Ready dashboard stream",
                                tags = new[] { "fake" },
                                viewer_count = 42,
                                started_at = "2026-07-15T11:00:00Z",
                                language = "en",
                                thumbnail_url = "https://fake.invalid/stream.png",
                                is_mature = false,
                            },
                        }
                        : [],
                }
            );
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static IResult CreateClip(FakeTwitchAuthority authority, HttpRequest request)
    {
        try
        {
            var clip = authority.CreateClip(request, request.Query["broadcaster_id"].ToString());
            return Results.Json(
                new { data = new[] { new { id = clip.Id, edit_url = clip.EditUrl } } },
                statusCode: StatusCodes.Status202Accepted
            );
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static IResult Clips(FakeTwitchAuthority authority, HttpRequest request)
    {
        try
        {
            return Results.Json(
                new
                {
                    data = authority
                        .Clips(request, request.Query["id"].ToArray())
                        .Select(clip => new
                        {
                            id = clip.Id,
                            url = clip.Url,
                            edit_url = clip.EditUrl,
                            broadcaster_id = authority.Definition.AuthorizedUser.Id,
                            broadcaster_login = authority.Definition.AuthorizedUser.Login,
                            creator_id = authority.Definition.AuthorizedUser.Id,
                            creator_name = authority.Definition.AuthorizedUser.Login,
                            video_id = "fake-video",
                        }),
                }
            );
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static async Task<IResult> CreateMarkerAsync(
        FakeTwitchAuthority authority,
        HttpRequest request
    )
    {
        try
        {
            var payload =
                await request.ReadFromJsonAsync<MarkerRequest>()
                ?? throw new FakeTwitchProtocolException(
                    HttpStatusCode.BadRequest,
                    "invalid_marker"
                );
            var marker = authority.CreateMarker(request, payload.UserId, payload.Description);
            return Results.Json(
                new
                {
                    data = new[]
                    {
                        new
                        {
                            id = marker.Id,
                            description = marker.Description,
                            position_seconds = marker.PositionSeconds,
                            created_at = marker.CreatedAt,
                            URL = marker.Url,
                        },
                    },
                }
            );
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static IResult Markers(FakeTwitchAuthority authority, HttpRequest request)
    {
        try
        {
            return Results.Json(
                new
                {
                    data = new[]
                    {
                        new
                        {
                            user_id = authority.Definition.AuthorizedUser.Id,
                            videos = new[]
                            {
                                new
                                {
                                    video_id = "fake-video",
                                    markers = authority
                                        .Markers(request)
                                        .Select(static marker => new
                                        {
                                            id = marker.Id,
                                            description = marker.Description,
                                            position_seconds = marker.PositionSeconds,
                                            created_at = marker.CreatedAt,
                                            URL = marker.Url,
                                        }),
                                },
                            },
                        },
                    },
                    pagination = new { cursor = (string?)null },
                }
            );
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static IResult Followers(FakeTwitchAuthority authority, HttpRequest request)
    {
        try
        {
            _ = authority.RequireUserToken(request, "moderator:read:followers");
            return Results.Json(new { total = 1, data = new[] { new { user_id = "3000" } } });
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static IResult ModeratedChannels(FakeTwitchAuthority authority, HttpRequest request)
    {
        try
        {
            _ = authority.RequireUserToken(request, "user:read:moderated_channels");
            var user = authority.Definition.AuthorizedUser;
            return Results.Json(
                new
                {
                    data = new[]
                    {
                        new
                        {
                            broadcaster_id = user.Id,
                            broadcaster_login = user.Login,
                            broadcaster_name = user.DisplayName,
                        },
                    },
                }
            );
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static IResult ChatSettings(FakeTwitchAuthority authority, HttpRequest request)
    {
        try
        {
            _ = authority.RequireUserToken(request);
            return Results.Json(
                new
                {
                    data = new[]
                    {
                        new
                        {
                            emote_mode = false,
                            follower_mode = false,
                            non_moderator_chat_delay_duration = 0,
                            non_moderator_chat_delay = false,
                            slow_mode = false,
                            subscriber_mode = false,
                            unique_chat_mode = false,
                        },
                    },
                }
            );
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static IResult Chatters(FakeTwitchAuthority authority, HttpRequest request)
    {
        try
        {
            var bot = authority.RequireUserToken(request, "moderator:read:chatters");
            var channel = authority.Definition.AuthorizedUser;
            return Results.Json(
                new
                {
                    data = new[]
                    {
                        new
                        {
                            user_id = bot.Id,
                            user_login = bot.Login,
                            user_name = bot.DisplayName,
                        },
                        new
                        {
                            user_id = channel.Id,
                            user_login = channel.Login,
                            user_name = channel.DisplayName,
                        },
                        new
                        {
                            user_id = "3000",
                            user_login = "simulationviewer",
                            user_name = "Simulation Viewer",
                        },
                    },
                    pagination = new { },
                }
            );
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static async Task<IResult> SubscribeAsync(
        FakeTwitchAuthority authority,
        HttpRequest request
    )
    {
        try
        {
            var payload = await request.ReadFromJsonAsync<SubscriptionRequest>();
            if (payload is null || payload.Transport is null)
            {
                throw new FakeTwitchProtocolException(
                    HttpStatusCode.BadRequest,
                    "invalid_subscription"
                );
            }

            if (!payload.Transport.Method.Equals("webhook", StringComparison.Ordinal))
            {
                throw new FakeTwitchProtocolException(
                    HttpStatusCode.BadRequest,
                    "invalid_transport"
                );
            }

            var id = authority.SubscribeWebhook(
                request,
                payload.Type,
                payload.Version,
                payload.Condition,
                payload.Transport.Callback ?? string.Empty,
                payload.Transport.Secret ?? string.Empty
            );
            return Results.Json(
                new { data = new[] { new { id } } },
                statusCode: StatusCodes.Status202Accepted
            );
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static IResult ListSubscriptions(FakeTwitchAuthority authority, HttpRequest request)
    {
        try
        {
            authority.RequireAppToken(request);
            const int PageSize = 5;
            var offset = int.TryParse(request.Query["after"], out var parsed) ? parsed : 0;
            var subscriptions = authority
                .ActiveSubscriptions.OrderBy(static subscription => subscription.Id)
                .ToArray();
            var items = subscriptions
                .Skip(offset)
                .Take(PageSize)
                .Select(subscription => new
                {
                    id = subscription.Id,
                    status = subscription.Status,
                    type = subscription.Type,
                    version = subscription.Version,
                    condition = subscription.Condition,
                    transport = new
                    {
                        method = subscription.Method,
                        callback = subscription.Callback,
                    },
                });
            string? next =
                offset + PageSize < subscriptions.Length
                    ? (offset + PageSize).ToString(CultureInfo.InvariantCulture)
                    : null;
            return Results.Json(new { data = items, pagination = new { cursor = next } });
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static IResult Unsubscribe(FakeTwitchAuthority authority, HttpRequest request)
    {
        try
        {
            var id = request.Query["id"].ToString();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new FakeTwitchProtocolException(
                    HttpStatusCode.BadRequest,
                    "missing_subscription_id"
                );
            }

            authority.Unsubscribe(request, id);
            return Results.NoContent();
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static IResult ProfileImage(FakeTwitchAuthority authority, string login)
    {
        var user = new[]
        {
            authority.Definition.AuthorizedUser,
            authority.Definition.BotUser,
        }.SingleOrDefault(candidate =>
            string.Equals(candidate.Login, login, StringComparison.OrdinalIgnoreCase)
        );
        if (user is null)
        {
            return Results.NotFound();
        }

        var background = user == authority.Definition.BotUser ? "#15803d" : "#7c3aed";
        var avatar = $$"""
            <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" role="img" aria-label="{{WebUtility.HtmlEncode(
                user.DisplayName
            )}} profile">
              <rect width="64" height="64" rx="14" fill="{{background}}"/>
              <circle cx="32" cy="24" r="12" fill="#ffffff" fill-opacity=".92"/>
              <path d="M12 58c1.5-12.5 9.2-20 20-20s18.5 7.5 20 20" fill="#ffffff" fill-opacity=".92"/>
            </svg>
            """;
        return Results.Bytes(Encoding.UTF8.GetBytes(avatar), "image/svg+xml");
    }

    private static object ToHelixUser(FakeTwitchUser user, HttpRequest request)
    {
        var profileImageUrl = new UriBuilder(request.Scheme, request.Host.Host)
        {
            Port = request.Host.Port ?? -1,
            Path = $"/profile-images/{Uri.EscapeDataString(user.Login)}.svg",
        }
            .Uri
            .AbsoluteUri;
        return new
        {
            id = user.Id,
            login = user.Login,
            display_name = user.DisplayName,
            profile_image_url = profileImageUrl,
            broadcaster_type = user.BroadcasterType,
        };
    }

    private static string? ReadOAuthToken(HttpRequest request)
    {
        const string Prefix = "OAuth ";
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? header[Prefix.Length..]
            : null;
    }

    private static IResult Error(FakeTwitchProtocolException failure) =>
        Results.Json(
            new { error = failure.Error, status = (int)failure.StatusCode },
            statusCode: (int)failure.StatusCode
        );

    private static async Task<IResult> ChatMessageAsync(
        FakeTwitchAuthority authority,
        HttpRequest request
    )
    {
        try
        {
            var message = await request.ReadFromJsonAsync<ChatMessageRequest>();
            if (message is not { })
            {
                throw new FakeTwitchProtocolException(
                    HttpStatusCode.BadRequest,
                    "invalid_chat_message"
                );
            }

            authority.RecordChatMessage(
                request,
                message.BroadcasterId,
                message.SenderId,
                message.Message
            );
            return Results.Json(
                new
                {
                    data = new[]
                    {
                        new
                        {
                            message_id = authority.NextMessageId(),
                            is_sent = true,
                            drop_reason = (object?)null,
                        },
                    },
                }
            );
        }
        catch (FakeTwitchProtocolException failure)
        {
            return Error(failure);
        }
    }

    private static IResult Unsupported(HttpRequest request) =>
        Results.Json(
            new
            {
                error = "unsupported_route",
                method = request.Method,
                path = request.Path.Value,
            },
            statusCode: StatusCodes.Status404NotFound
        );

    private sealed record SubscriptionRequest
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

        [JsonPropertyName("callback")]
        public string? Callback { get; init; }

        [JsonPropertyName("secret")]
        public string? Secret { get; init; }
    }

    private sealed record ChatMessageRequest
    {
        [JsonPropertyName("broadcaster_id")]
        public required string BroadcasterId { get; init; }

        [JsonPropertyName("sender_id")]
        public required string SenderId { get; init; }

        [JsonPropertyName("message")]
        public required string Message { get; init; }
    }

    private sealed record MarkerRequest
    {
        [JsonPropertyName("user_id")]
        public required string UserId { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }
    }
}

/// <summary>Represents a fake OAuth token response.</summary>
public sealed record FakeTwitchToken(string AccessToken, string RefreshToken, int ExpiresIn);

/// <summary>Represents a fake OAuth validation response.</summary>
public sealed record FakeTwitchTokenValidation(
    string UserId,
    string Login,
    IReadOnlySet<string> Scopes
);

/// <summary>Represents one fake EventSub subscription.</summary>
public sealed record FakeTwitchSubscription
{
    public required string Id { get; init; }

    public required string Type { get; init; }

    public required string Method { get; init; }

    public required string Callback { get; init; }

    public required string Status { get; init; }

    public required string Version { get; init; }

    public required IReadOnlyDictionary<string, string> Condition { get; init; }
}

internal sealed class FakeTwitchProtocolException(HttpStatusCode statusCode, string error)
    : Exception(error)
{
    internal HttpStatusCode StatusCode { get; } = statusCode;

    internal string Error { get; } = error;
}
