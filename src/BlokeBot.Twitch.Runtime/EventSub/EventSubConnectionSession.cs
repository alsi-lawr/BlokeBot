using System.Collections.ObjectModel;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using BlokeBot.Eventing;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal interface IEventSubConnectionSession
{
    Task<RuntimeSessionEstablishment> EstablishAsync(
        RuntimeConnectionTarget target,
        CancellationToken cancellationToken
    );
}

internal sealed class EventSubConnectionSession(
    IBotChannelProvider channels,
    EventSubChannelSessionFactory channelSessions,
    ChatCommandDispatcher dispatcher,
    ICommandResponseSender responses,
    BotRuntimeStatusStore status,
    IEnumerable<IChatMessageObserver> messageObservers,
    ObserverFanOut<
        EventSubMessageObserverBoundary,
        ChatMessage,
        ChatObserverDeadLetter
    > messageObserverFanOut,
    ILogger<EventSubConnectionSession> log,
    IEnumerable<IShoutoutEventObserver>? shoutoutObservers = null
) : IEventSubConnectionSession
{
    private static readonly ObserverEventIdentity _chatMessageEvent = ObserverEventIdentity.Named(
        "TwitchChatMessage"
    );
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri _defaultEndpoint = new("wss://eventsub.wss.twitch.tv/ws");
    private readonly IChatMessageObserver[] _messageObservers = [.. messageObservers];
    private readonly IShoutoutEventObserver[] _shoutoutObservers = [.. (shoutoutObservers ?? [])];
    private readonly HashSet<string> _deliveredMessageIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _deliveredMessageIdOrder = new();
    private const int _deliveredMessageCapacity = 512;
    private ILogger<EventSubConnectionSession> _log { get; } = log;

    public async Task<RuntimeSessionEstablishment> EstablishAsync(
        RuntimeConnectionTarget target,
        CancellationToken cancellationToken
    )
    {
        var channelLogins = BotChannelList.Normalize(
            await channels.GetChannelsAsync(cancellationToken)
        );
        var connectionTarget = target.Match(
            static _ => (Endpoint: _defaultEndpoint, IsInitial: true),
            static reconnect => (Endpoint: reconnect.Uri, IsInitial: false)
        );
        if (
            channelLogins.Length == 0
            && !channelSessions.HasPendingReconciliation
            && connectionTarget.IsInitial
        )
        {
            status.MarkDisconnected();
            _log.LogWarning(
                "No Twitch channels are configured for the bot runtime; waiting for hosted channels."
            );
            return new RuntimeSessionEstablishment.Idle();
        }

        var socket = new ClientWebSocket();
        EventSubChannelSession? channelSession = null;
        try
        {
            await socket.ConnectAsync(connectionTarget.Endpoint, cancellationToken);
            _log.LogInformation("Connected to Twitch EventSub WebSocket.");
            var json =
                await ReadTextMessageAsync(socket, cancellationToken)
                ?? throw new IOException("EventSub WebSocket disconnected.");
            var envelope = JsonSerializer.Deserialize<EventSubEnvelope>(json, _jsonOptions);
            var messageType = EventSubMessageTypes.Parse(envelope?.Metadata.MessageType);
            if (messageType is not EventSubMessageType.SessionWelcome)
            {
                throw new InvalidOperationException(
                    "EventSub did not begin with a session welcome message."
                );
            }

            var sessionId = envelope?.Payload.Session?.Id;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new InvalidOperationException(
                    "EventSub session welcome did not include a session ID."
                );
            }

            channelSession = channelSessions.Create(sessionId);
            var desiredChannels = await GetDesiredChannelsAsync(cancellationToken);
            return new RuntimeSessionEstablishment.Established
            {
                Session = new EstablishedSession(this, socket, channelSession, desiredChannels),
            };
        }
        catch (Exception establishmentException)
        {
            var cleanupException = await DisposeResourcesAsync(channelSession, socket);
            if (cleanupException is not null)
            {
                throw new AggregateException(
                    "EventSub establishment and cleanup both failed.",
                    establishmentException,
                    cleanupException
                );
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<string>> GetDesiredChannelsAsync(
        CancellationToken cancellationToken
    )
    {
        return BotChannelList.Normalize(await channels.GetChannelsAsync(cancellationToken));
    }

    internal async Task DispatchChatMessageAsync(
        EventSubChatMessageEvent chatEvent,
        string rawJson,
        CancellationToken cancellationToken
    )
    {
        var text = chatEvent.Message?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var message = new ChatMessage(
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

    private async Task DispatchShoutoutAsync(
        EventSubShoutoutEvent shoutout,
        CancellationToken cancellationToken
    )
    {
        foreach (var observer in _shoutoutObservers)
        {
            await observer.ShoutoutReceivedAsync(shoutout, cancellationToken);
        }
    }

    private bool ShouldDispatch(string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return true;
        }
        lock (_deliveredMessageIds)
        {
            if (!_deliveredMessageIds.Add(messageId))
            {
                return false;
            }
            _deliveredMessageIdOrder.Enqueue(messageId);
            if (_deliveredMessageIdOrder.Count > _deliveredMessageCapacity)
            {
                _deliveredMessageIds.Remove(_deliveredMessageIdOrder.Dequeue());
            }
            return true;
        }
    }

    private async ValueTask NotifyMessageObserversAsync(
        ChatMessage message,
        CancellationToken cancellationToken
    )
    {
        _ = await messageObserverFanOut.DispatchAsync(
            _messageObservers,
            _ => new ObserverDispatch<ChatMessage, ChatObserverDeadLetter>
            {
                Event = message,
                EventIdentity = _chatMessageEvent,
                DeadLetter = new ChatObserverDeadLetter(message.Channel),
            },
            observer => ObserverIdentity.For(observer.GetType()),
            static (observer, chatMessage, token) =>
                observer.MessageReceivedAsync(chatMessage, token),
            cancellationToken
        );
    }

    private static IReadOnlyDictionary<string, string> CreateTags(
        EventSubChatMessageEvent chatEvent
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
        {
            tags["mod"] = "1";
        }

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
            {
                return null;
            }

            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(message.ToArray());
    }

    private static Uri RequireReconnectEndpoint(string? reconnectUrl)
    {
        if (
            !Uri.TryCreate(reconnectUrl, UriKind.Absolute, out var endpoint)
            || (
                !endpoint.Scheme.Equals(Uri.UriSchemeWs, StringComparison.OrdinalIgnoreCase)
                && !endpoint.Scheme.Equals(Uri.UriSchemeWss, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            throw new InvalidOperationException(
                "EventSub reconnect message did not include a valid WebSocket URL."
            );
        }

        return endpoint;
    }

    private static async ValueTask<Exception?> DisposeResourcesAsync(
        EventSubChannelSession? channelSession,
        ClientWebSocket socket
    )
    {
        Exception? failure = null;
        if (channelSession is not null)
        {
            try
            {
                await channelSession.DisposeAsync();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        try
        {
            socket.Dispose();
        }
        catch (Exception exception)
        {
            failure = failure is null
                ? exception
                : new AggregateException(
                    "EventSub session resource cleanup failed.",
                    failure,
                    exception
                );
        }

        return failure;
    }

    private sealed class EstablishedSession(
        EventSubConnectionSession owner,
        ClientWebSocket socket,
        EventSubChannelSession channelSession,
        IReadOnlyList<string> initialChannels
    ) : IRuntimeEstablishedSession
    {
        public async Task<RuntimeReconnectRequest> ListenAsync(CancellationToken cancellationToken)
        {
            channelSession.Start(initialChannels, cancellationToken);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var json =
                    await ReadTextMessageAsync(socket, cancellationToken)
                    ?? throw new IOException("EventSub WebSocket disconnected.");
                var envelope = JsonSerializer.Deserialize<EventSubEnvelope>(json, _jsonOptions);
                var rawMessageType = envelope?.Metadata.MessageType;
                var messageType = EventSubMessageTypes.Parse(rawMessageType);

                switch (messageType)
                {
                    case EventSubMessageType.SessionWelcome:
                        throw new InvalidOperationException(
                            "EventSub sent a duplicate session welcome message."
                        );

                    case EventSubMessageType.SessionKeepalive:
                        channelSession.TriggerReconciliation(
                            await owner.GetDesiredChannelsAsync(cancellationToken),
                            EventSubChannelRecoveryTrigger.Keepalive
                        );
                        break;

                    case EventSubMessageType.SessionReconnect:
                        owner._log.LogInformation("Twitch requested EventSub WebSocket reconnect.");
                        return new RuntimeReconnectRequest
                        {
                            Target = new RuntimeConnectionTarget.EventSubReconnect
                            {
                                Uri = RequireReconnectEndpoint(
                                    envelope?.Payload.Session?.ReconnectUrl
                                ),
                            },
                        };

                    case EventSubMessageType.Notification:
                        if (
                            envelope is not null
                            && owner.ShouldDispatch(envelope.Metadata.MessageId)
                        )
                        {
                            switch (EventSubNotification.Parse(envelope, _jsonOptions))
                            {
                                case EventSubNotification.Chat { Event: var chatEvent }:
                                    await owner.DispatchChatMessageAsync(
                                        chatEvent,
                                        json,
                                        cancellationToken
                                    );
                                    break;
                                case EventSubNotification.Shoutout { Event: var shoutout }:
                                    await owner.DispatchShoutoutAsync(shoutout, cancellationToken);
                                    break;
                            }
                        }
                        break;

                    case EventSubMessageType.Revocation:
                        owner._log.LogWarning("EventSub subscription was revoked.");
                        throw new InvalidOperationException(
                            "EventSub chat subscription was revoked."
                        );

                    case EventSubMessageType.Unknown:
                        owner._log.LogDebug("Unhandled EventSub message was ignored.");
                        break;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            var failure = await DisposeResourcesAsync(channelSession, socket);
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }
}
