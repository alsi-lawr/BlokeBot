using System.Collections.ObjectModel;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchEventSubRuntime(
    IOptions<TwitchBotOptions> options,
    ITwitchBotChannelProvider channels,
    ITwitchAccessTokenProvider tokens,
    TwitchCommandDispatcher dispatcher,
    TwitchHelixChatClient helix,
    ITwitchChatMessageSender sender,
    ITwitchBotChannelLifecycleNotifier lifecycleNotifier,
    TwitchBotRuntimeStatusStore status,
    ILogger<TwitchEventSubRuntime> log
)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TwitchBotOptions opts = options.Value;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunWebSocketAsync(null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (TwitchAccessTokenUnavailableException ex)
                when (!stoppingToken.IsCancellationRequested
                    && ex.Reason == TwitchAccessTokenUnavailableReason.MissingRefreshToken
                )
            {
                log.LogWarning(
                    "Twitch chat token is not available yet. Open {OAuthStartUri} and authorize the bot account; reconnecting after setup delay.",
                    TwitchBotSetup.CreateOAuthStartUri(opts.Identity.RedirectUri)
                );
                status.SetAuthorized(false);
                status.SetConnected(false, []);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "EventSub runtime crashed; reconnecting.");
                status.SetConnected(false, []);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RunWebSocketAsync(string? reconnectUrl, CancellationToken cancellationToken)
    {
        var accessToken = await tokens.GetAccessTokenAsync(cancellationToken);
        status.SetAuthorized(true);
        var channelLogins = TwitchChannelList.Normalize(
            await channels.GetChannelsAsync(cancellationToken)
        );
        if (channelLogins.Length == 0)
        {
            status.SetConnected(false, []);
            log.LogWarning(
                "No Twitch channels are configured for the bot runtime; waiting for hosted channels."
            );
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return;
        }

        using var socket = new ClientWebSocket();
        var uri = new Uri(reconnectUrl ?? "wss://eventsub.wss.twitch.tv/ws");
        await socket.ConnectAsync(uri, cancellationToken);
        status.SetConnected(true, channelLogins);
        log.LogInformation("Connected to Twitch EventSub WebSocket.");
        var activeSubscriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? activeSessionId = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var json = await ReadTextMessageAsync(socket, cancellationToken);
            if (json is null)
                throw new IOException("EventSub WebSocket disconnected.");

            var envelope = JsonSerializer.Deserialize<TwitchEventSubEnvelope>(json, JsonOptions);
            var rawMessageType = envelope?.Metadata.MessageType;
            var messageType = TwitchEventSubMessageTypes.Parse(rawMessageType);

            switch (messageType)
            {
                case TwitchEventSubMessageType.SessionWelcome:
                    var sessionId = envelope?.Payload.Session?.Id;
                    if (string.IsNullOrWhiteSpace(sessionId))
                        throw new InvalidOperationException(
                            "EventSub session welcome did not include a session ID."
                        );

                    activeSessionId = sessionId;
                    await SyncChatSubscriptionsAsync(
                        activeSubscriptions,
                        sessionId,
                        cancellationToken
                    );
                    break;

                case TwitchEventSubMessageType.SessionKeepalive:
                    if (activeSessionId is not null)
                    {
                        await SyncChatSubscriptionsAsync(
                            activeSubscriptions,
                            activeSessionId,
                            cancellationToken
                        );
                    }
                    break;

                case TwitchEventSubMessageType.SessionReconnect:
                    var reconnect = envelope?.Payload.Session?.ReconnectUrl;
                    if (string.IsNullOrWhiteSpace(reconnect))
                        throw new InvalidOperationException(
                            "EventSub reconnect message did not include a reconnect URL."
                        );

                    log.LogInformation("Twitch requested EventSub WebSocket reconnect.");
                    await RunWebSocketAsync(reconnect, cancellationToken);
                    return;

                case TwitchEventSubMessageType.Notification:
                    if (envelope?.Payload.Event is not { } chatEvent)
                        break;

                    await DispatchChatMessageAsync(chatEvent, json, cancellationToken);
                    break;

                case TwitchEventSubMessageType.Revocation:
                    log.LogWarning("EventSub subscription was revoked: {Payload}", json);
                    throw new InvalidOperationException("EventSub chat subscription was revoked.");

                case TwitchEventSubMessageType.Unknown:
                    log.LogDebug(
                        "Unhandled EventSub message type {MessageType}: {Payload}",
                        rawMessageType,
                        json
                    );
                    break;
            }
        }
    }

    private async Task SyncChatSubscriptionsAsync(
        Dictionary<string, string> activeSubscriptions,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        var desiredChannels = TwitchChannelList.Normalize(
            await channels.GetChannelsAsync(cancellationToken)
        );
        var accessToken = await tokens.GetAccessTokenAsync(cancellationToken);
        var startedChannels = new List<string>();
        var stoppedChannels = new List<string>();

        foreach (
            var channel in activeSubscriptions
                .Keys.Except(desiredChannels, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        )
        {
            await helix.DeleteEventSubSubscriptionAsync(
                accessToken,
                activeSubscriptions[channel],
                cancellationToken
            );
            activeSubscriptions.Remove(channel);
            stoppedChannels.Add(channel);
            log.LogInformation("Unsubscribed from EventSub chat messages for #{Channel}.", channel);
        }

        foreach (
            var channel in desiredChannels.Except(
                activeSubscriptions.Keys,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            var identities = await helix.ResolveChatIdentitiesAsync(
                channel,
                accessToken,
                cancellationToken
            );
            activeSubscriptions[channel] = await helix.CreateChatMessageSubscriptionAsync(
                accessToken,
                identities.BroadcasterId,
                identities.BotUserId,
                sessionId,
                cancellationToken
            );
            await SendStartupMessageAsync(channel, cancellationToken);
            startedChannels.Add(channel);
            log.LogInformation(
                "Subscribed to EventSub chat messages for #{Channel} as {BotUsername}.",
                channel,
                opts.Identity.BotUsername
            );
        }

        status.SetConnected(activeSubscriptions.Count > 0, activeSubscriptions.Keys.ToArray());
        foreach (var channel in stoppedChannels)
            await lifecycleNotifier.ChannelStoppedAsync(channel, cancellationToken);

        foreach (var channel in startedChannels)
            await lifecycleNotifier.ChannelStartedAsync(channel, cancellationToken);
    }

    private async Task SendStartupMessageAsync(string channel, CancellationToken cancellationToken)
    {
        var startupMessage = opts.StartupMessage;
        if (string.IsNullOrWhiteSpace(startupMessage))
            return;

        await sender.SendAsync(channel, startupMessage, cancellationToken);
    }

    private async Task DispatchChatMessageAsync(
        TwitchEventSubChatMessageEvent chatEvent,
        string rawJson,
        CancellationToken cancellationToken
    )
    {
        var text = chatEvent.Message?.Text;
        if (string.IsNullOrWhiteSpace(text))
            return;

        var message = new TwitchChatMessage(
            chatEvent.ChatterUserLogin,
            chatEvent.BroadcasterUserLogin,
            text,
            rawJson,
            CreateTags(chatEvent)
        );

        await dispatcher.DispatchAsync(
            message,
            async (reply, ct) => await sender.SendAsync(message.Channel, reply, ct),
            cancellationToken
        );
    }

    private static IReadOnlyDictionary<string, string> CreateTags(
        TwitchEventSubChatMessageEvent chatEvent
    )
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = chatEvent.MessageId,
            ["badges"] = string.Join(
                ',',
                chatEvent.Badges.Select(badge => $"{badge.SetId}/{badge.Id}")
            ),
        };

        if (
            chatEvent.Badges.Any(badge =>
                badge.SetId.Equals("moderator", StringComparison.OrdinalIgnoreCase)
            )
        )
            tags["mod"] = "1";

        return new ReadOnlyDictionary<string, string>(tags);
    }

    private static async Task<string?> ReadTextMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[8192];
        using var message = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }

        return Encoding.UTF8.GetString(message.ToArray());
    }
}
