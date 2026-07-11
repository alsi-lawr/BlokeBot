using System.Collections.ObjectModel;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchEventSubRuntime(
    TwitchBotSettings settings,
    ITwitchBotChannelProvider channels,
    ITwitchBotAccountProvider botAccounts,
    TwitchCommandDispatcher dispatcher,
    TwitchHelixChatClient helix,
    ITwitchChatMessageSender sender,
    ITwitchCommandResponseSender responses,
    ITwitchBotChannelLifecycleNotifier lifecycleNotifier,
    TwitchBotRuntimeStatusStore status,
    IEnumerable<ITwitchChatMessageObserver> messageObservers,
    ILogger<TwitchEventSubRuntime> log
)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TwitchBotSettings opts = settings;

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
        log.LogInformation("Connected to Twitch EventSub WebSocket.");
        var activeSubscriptions = new Dictionary<string, ActiveEventSubSubscription>(
            StringComparer.OrdinalIgnoreCase
        );
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
        Dictionary<string, ActiveEventSubSubscription> activeSubscriptions,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        var desiredChannels = TwitchChannelList.Normalize(
            await channels.GetChannelsAsync(cancellationToken)
        );
        var desiredAccounts = new Dictionary<string, TwitchBotAccount>(
            StringComparer.OrdinalIgnoreCase
        );
        var startedChannels = new List<string>();
        var stoppedChannels = new List<string>();

        foreach (var channel in desiredChannels)
        {
            try
            {
                desiredAccounts[channel] = await botAccounts.GetBotAccountAsync(
                    channel,
                    cancellationToken
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.LogWarning(
                    ex,
                    "Bot account is not available for EventSub channel #{Channel}; skipping subscription.",
                    channel
                );
            }
        }

        foreach (
            var channel in activeSubscriptions
                .Keys.Except(desiredAccounts.Keys, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        )
        {
            await TryDeleteSubscriptionAsync(
                channel,
                activeSubscriptions[channel],
                cancellationToken
            );
            activeSubscriptions.Remove(channel);
            stoppedChannels.Add(channel);
            log.LogInformation("Unsubscribed from EventSub chat messages for #{Channel}.", channel);
        }

        foreach (var (channel, botAccount) in desiredAccounts.ToArray())
        {
            if (
                activeSubscriptions.TryGetValue(channel, out var active)
                && string.Equals(
                    active.BotLogin,
                    botAccount.Login,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }

            if (activeSubscriptions.Remove(channel, out var replaced))
            {
                await TryDeleteSubscriptionAsync(channel, replaced, cancellationToken);
            }

            try
            {
                var identities = await helix.ResolveChatIdentitiesAsync(
                    channel,
                    botAccount.Login,
                    botAccount.AccessToken,
                    cancellationToken
                );
                activeSubscriptions[channel] = new ActiveEventSubSubscription(
                    await helix.CreateChatMessageSubscriptionAsync(
                        botAccount.AccessToken,
                        identities.BroadcasterId,
                        identities.BotUserId,
                        sessionId,
                        cancellationToken
                    ),
                    botAccount.Login,
                    botAccount.AccessToken
                );
                await SendStartupMessageAsync(channel, cancellationToken);
                startedChannels.Add(channel);
                log.LogInformation(
                    "Subscribed to EventSub chat messages for #{Channel} as {BotUsername}.",
                    channel,
                    botAccount.Login
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                stoppedChannels.Add(channel);
                log.LogWarning(
                    ex,
                    "Could not subscribe to EventSub chat messages for #{Channel} as {BotUsername}.",
                    channel,
                    botAccount.Login
                );
            }
        }

        status.SetAuthorized(desiredAccounts.Count > 0);
        status.SetConnected(activeSubscriptions.Count > 0, activeSubscriptions.Keys.ToArray());
        foreach (var channel in stoppedChannels)
            await lifecycleNotifier.ChannelStoppedAsync(channel, cancellationToken);

        foreach (var channel in startedChannels)
            await lifecycleNotifier.ChannelStartedAsync(channel, cancellationToken);
    }

    private async Task TryDeleteSubscriptionAsync(
        string channel,
        ActiveEventSubSubscription subscription,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await helix.DeleteEventSubSubscriptionAsync(
                subscription.AccessToken,
                subscription.SubscriptionId,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(
                ex,
                "Could not delete EventSub chat subscription for #{Channel}; removing local subscription state.",
                channel
            );
        }
    }

    private async Task SendStartupMessageAsync(string channel, CancellationToken cancellationToken)
    {
        var startupMessage = opts.StartupMessage;
        if (string.IsNullOrWhiteSpace(startupMessage))
            return;

        await sender.SendAsync(channel, startupMessage, cancellationToken);
    }

    internal async Task DispatchChatMessageAsync(
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

        await NotifyMessageObserversAsync(message, cancellationToken);
        await dispatcher.DispatchResponsesAsync(
            message,
            async (response, ct) => await responses.SendAsync(message, response, ct),
            cancellationToken
        );
    }

    private async ValueTask NotifyMessageObserversAsync(
        TwitchChatMessage message,
        CancellationToken cancellationToken
    )
    {
        foreach (var observer in messageObservers)
        {
            try
            {
                await observer.MessageReceivedAsync(message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Twitch EventSub chat message observer failed.");
            }
        }
    }

    private static IReadOnlyDictionary<string, string> CreateTags(
        TwitchEventSubChatMessageEvent chatEvent
    )
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = chatEvent.MessageId,
            ["user-id"] = chatEvent.ChatterUserId,
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

    private sealed record ActiveEventSubSubscription(
        string SubscriptionId,
        string BotLogin,
        string AccessToken
    );
}
