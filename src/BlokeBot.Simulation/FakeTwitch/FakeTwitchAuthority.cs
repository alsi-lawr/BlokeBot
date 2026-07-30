using System.Collections.Immutable;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
                "channel:read:redemptions",
                "channel:manage:redemptions",
                "channel:read:predictions",
                "channel:manage:predictions"
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
    private readonly Dictionary<string, FakeTwitchSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FakeTwitchSubscription> _subscriptions = new(
        StringComparer.Ordinal
    );
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
            if (
                !string.Equals(
                    request.Headers["Client-Id"],
                    Definition.ClientId,
                    StringComparison.Ordinal
                )
                || !TryGetGrant(ReadBearerToken(request), out var grant)
                || grant.User is null
            )
            {
                throw new FakeTwitchProtocolException(HttpStatusCode.Unauthorized, "invalid_token");
            }

            if (scopes.Any(scope => !grant.Scopes.Contains(scope)))
            {
                throw new FakeTwitchProtocolException(HttpStatusCode.Forbidden, "missing_scope");
            }

            return grant.User;
        }
    }

    public FakeTwitchSession OpenSession(WebSocket socket)
    {
        lock (_gate)
        {
            var session = new FakeTwitchSession(NextId("session"), socket);
            _sessions.Add(session.Id, session);
            Record("eventsub.connect", session.Id);
            return session;
        }
    }

    public string Subscribe(
        HttpRequest request,
        string type,
        string version,
        IReadOnlyDictionary<string, string> condition,
        string sessionId
    )
    {
        var subscriber = RequireUserToken(request);
        var botSubscription =
            type
            is "channel.chat.message"
                or "channel.shoutout.create"
                or "channel.shoutout.receive";
        var raidSubscription = type is "channel.raid";
        var broadcasterSubscription =
            type
            is "channel.poll.begin"
                or "channel.poll.progress"
                or "channel.poll.end"
                or "channel.prediction.begin"
                or "channel.prediction.progress"
                or "channel.prediction.lock"
                or "channel.prediction.end"
                or "channel.channel_points_custom_reward_redemption.add"
                or "channel.channel_points_custom_reward_redemption.update";
        if (
            version != "1"
            || !botSubscription && !raidSubscription && !broadcasterSubscription
            || !condition.TryGetValue(
                raidSubscription ? "to_broadcaster_user_id" : "broadcaster_user_id",
                out var broadcasterId
            )
            || broadcasterId != Definition.AuthorizedUser.Id
            || botSubscription
                && (
                    subscriber.Id != Definition.BotUser.Id
                    || !condition.TryGetValue(
                        type == "channel.chat.message" ? "user_id" : "moderator_user_id",
                        out var botId
                    )
                    || botId != Definition.BotUser.Id
                )
            || raidSubscription && subscriber.Id != Definition.BotUser.Id
            || broadcasterSubscription && subscriber.Id != Definition.AuthorizedUser.Id
        )
        {
            throw new FakeTwitchProtocolException(
                HttpStatusCode.BadRequest,
                "invalid_subscription"
            );
        }

        FakeTwitchSession session;
        string id;
        lock (_gate)
        {
            if (
                !_sessions.TryGetValue(sessionId, out session!)
                || session.Socket.State is not WebSocketState.Open
            )
            {
                throw new FakeTwitchProtocolException(HttpStatusCode.Conflict, "unknown_session");
            }

            id = NextId("subscription");
            _subscriptions.Add(
                id,
                new(
                    id,
                    type,
                    sessionId,
                    broadcasterId,
                    botSubscription || raidSubscription ? Definition.BotUser.Id : null,
                    subscriber.Id
                )
            );
            Record("eventsub.subscribe", $"{type}:{id}");
        }

        _ = DeliverInitialEventsAsync(session, type);
        return id;
    }

    public void Unsubscribe(HttpRequest request, string id)
    {
        _ = RequireUserToken(request);
        lock (_gate)
        {
            if (!_subscriptions.Remove(id))
            {
                throw new FakeTwitchProtocolException(
                    HttpStatusCode.NotFound,
                    "unknown_subscription"
                );
            }

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

    public void CloseSession(string id)
    {
        lock (_gate)
        {
            _sessions.Remove(id);
            Record("eventsub.disconnect", id);
        }
    }

    private FakeTwitchToken ExchangeCode(string? code, string? redirectUri)
    {
        if (
            string.IsNullOrWhiteSpace(code)
            || !_codes.Remove(code, out var authorization)
            || !string.Equals(authorization.RedirectUri, redirectUri, StringComparison.Ordinal)
        )
        {
            throw new FakeTwitchProtocolException(HttpStatusCode.BadRequest, "invalid_code");
        }

        return UserToken(authorization.User, authorization.Scopes, "oauth.exchange");
    }

    private FakeTwitchToken Refresh(string? refreshToken)
    {
        if (
            string.IsNullOrWhiteSpace(refreshToken)
            || !_refreshTokens.Remove(refreshToken, out var grant)
        )
        {
            throw new FakeTwitchProtocolException(
                HttpStatusCode.BadRequest,
                "invalid_refresh_token"
            );
        }

        return UserToken(grant.User!, grant.Scopes, "oauth.refresh");
    }

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

    private void Record(string kind, string detail)
    {
        _transcript.Add(new(NextId("transcript"), kind, detail));
    }

    private async Task DeliverInitialEventsAsync(FakeTwitchSession session, string type)
    {
        try
        {
            foreach (
                var payload in type switch
                {
                    "channel.chat.message" => ChatEvents(),
                    "channel.poll.begin" => PollEvents(),
                    _ => [],
                }
            )
            {
                await session.SendAsync(payload);
                lock (_gate)
                {
                    Record("eventsub.deliver", type);
                }
            }
        }
        catch (WebSocketException)
        {
            CloseSession(session.Id);
        }
    }

    private IEnumerable<string> ChatEvents()
    {
        yield return Notification(
            "chat-ordinary-0001",
            "channel.chat.message",
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
            "chat-moderator-0002",
            "channel.chat.message",
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
            "chat-custom-command-0003",
            "channel.chat.message",
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

    private IEnumerable<string> PollEvents()
    {
        yield return Notification(
            "poll-begin-0001",
            "channel.poll.begin",
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

    private static string Notification(string id, string type, object @event)
    {
        return JsonSerializer.Serialize(
            new
            {
                metadata = new
                {
                    message_id = id,
                    message_type = "notification",
                    subscription_type = type,
                    subscription_version = "1",
                },
                payload = new { subscription = new { type, version = "1" }, @event },
            }
        );
    }

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
        services.AddSingleton(new FakeTwitchAuthority(scenario));
        return services;
    }

    public static IServiceCollection AddFakeTwitch(
        this IServiceCollection services,
        FakeTwitchAuthority authority
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(authority);
        services.AddSingleton(authority);
        return services;
    }

    public static WebApplication MapFakeTwitch(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseWebSockets();
        var authority = app.Services.GetRequiredService<FakeTwitchAuthority>();

        app.MapGet("/oauth2/authorize", (HttpRequest request) => Authorize(authority, request));
        app.MapPost("/oauth2/token", (HttpRequest request) => TokenAsync(authority, request));
        app.MapGet("/oauth2/validate", (HttpRequest request) => Validate(authority, request));
        app.MapGet("/helix/users", (HttpRequest request) => Users(authority, request));
        app.MapGet("/helix/streams", (HttpRequest request) => Streams(authority, request));
        app.MapGet("/profile-images/{login}.svg", (string login) => ProfileImage(authority, login));
        app.MapGet(
            "/helix/channels/followers",
            (HttpRequest request) => Followers(authority, request)
        );
        app.MapGet(
            "/helix/moderation/channels",
            (HttpRequest request) => ModeratedChannels(authority, request)
        );
        app.MapGet(
            "/helix/chat/settings",
            (HttpRequest request) => ChatSettings(authority, request)
        );
        app.MapPost(
            "/helix/eventsub/subscriptions",
            (HttpRequest request) => SubscribeAsync(authority, request)
        );
        app.MapDelete(
            "/helix/eventsub/subscriptions",
            (HttpRequest request) => Unsubscribe(authority, request)
        );
        app.MapPost(
            "/helix/chat/messages",
            (HttpRequest request) => ChatMessageAsync(authority, request)
        );
        app.Map("/ws", context => WebSocketAsync(authority, context));
        app.MapMethods(
            "/oauth2/{**path}",
            ["GET", "POST", "PUT", "PATCH", "DELETE"],
            (HttpRequest request) => Unsupported(request)
        );
        app.MapMethods(
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
            _ = authority.RequireUserToken(request);
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

    private static async Task<IResult> SubscribeAsync(
        FakeTwitchAuthority authority,
        HttpRequest request
    )
    {
        try
        {
            var payload = await request.ReadFromJsonAsync<SubscriptionRequest>();
            if (
                payload is null
                || payload.Transport is null
                || payload.Transport.Method != "websocket"
                || string.IsNullOrWhiteSpace(payload.Transport.SessionId)
            )
            {
                throw new FakeTwitchProtocolException(
                    HttpStatusCode.BadRequest,
                    "invalid_subscription"
                );
            }

            var id = authority.Subscribe(
                request,
                payload.Type,
                payload.Version,
                payload.Condition,
                payload.Transport.SessionId
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

    private static async Task WebSocketAsync(FakeTwitchAuthority authority, HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "websocket_required" });
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var session = authority.OpenSession(socket);
        try
        {
            await session.SendAsync(
                JsonSerializer.Serialize(
                    new
                    {
                        metadata = new
                        {
                            message_id = "welcome-0001",
                            message_type = "session_welcome",
                            subscription_type = string.Empty,
                            subscription_version = string.Empty,
                        },
                        payload = new
                        {
                            session = new
                            {
                                id = session.Id,
                                status = "connected",
                                connected_at = "2026-07-15T12:00:00Z",
                                keepalive_timeout_seconds = 30,
                                reconnect_url = (string?)null,
                            },
                        },
                    }
                )
            );
            var buffer = new byte[128];
            while (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                var received = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        finally
        {
            authority.CloseSession(session.Id);
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

    private static IResult Error(FakeTwitchProtocolException failure)
    {
        return Results.Json(
            new { error = failure.Error, status = (int)failure.StatusCode },
            statusCode: (int)failure.StatusCode
        );
    }

    private static IResult Unsupported(HttpRequest request)
    {
        return Results.Json(
            new
            {
                error = "unsupported_route",
                method = request.Method,
                path = request.Path.Value,
            },
            statusCode: StatusCodes.Status404NotFound
        );
    }

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

        [JsonPropertyName("session_id")]
        public required string SessionId { get; init; }
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
}

/// <summary>Represents a fake OAuth token response.</summary>
public sealed record FakeTwitchToken(string AccessToken, string RefreshToken, int ExpiresIn);

/// <summary>Represents a fake OAuth validation response.</summary>
public sealed record FakeTwitchTokenValidation(
    string UserId,
    string Login,
    IReadOnlySet<string> Scopes
);

/// <summary>Represents a fake EventSub WebSocket session.</summary>
public sealed class FakeTwitchSession(string id, WebSocket socket)
{
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    public string Id { get; } = id;

    public WebSocket Socket { get; } = socket;

    public async Task SendAsync(string payload)
    {
        await _sendGate.WaitAsync();
        try
        {
            await Socket.SendAsync(
                Encoding.UTF8.GetBytes(payload),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );
        }
        finally
        {
            _sendGate.Release();
        }
    }
}

/// <summary>Represents one fake EventSub subscription.</summary>
public sealed record FakeTwitchSubscription(
    string Id,
    string Type,
    string SessionId,
    string BroadcasterId,
    string? BotUserId,
    string SubscriberUserId
);

internal sealed class FakeTwitchProtocolException(HttpStatusCode statusCode, string error)
    : Exception(error)
{
    internal HttpStatusCode StatusCode { get; } = statusCode;

    internal string Error { get; } = error;
}
