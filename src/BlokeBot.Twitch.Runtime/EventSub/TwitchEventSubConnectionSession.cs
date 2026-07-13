using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using BlokeBot.Eventing;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal interface ITwitchEventSubConnectionSession
{
    Task<TwitchRuntimeSessionEstablishment> EstablishAsync(
        TwitchRuntimeConnectionTarget target,
        CancellationToken cancellationToken
    );
}

internal sealed class TwitchEventSubConnectionSession(
    ITwitchBotChannelProvider channels,
    TwitchEventSubChannelSessionFactory channelSessions,
    TwitchCommandDispatcher dispatcher,
    ITwitchCommandResponseSender responses,
    TwitchBotRuntimeStatusStore status,
    IEnumerable<ITwitchChatMessageObserver> messageObservers,
    ObserverFanOut<
        TwitchEventSubMessageObserverBoundary,
        TwitchChatMessage,
        TwitchChatObserverDeadLetter
    > messageObserverFanOut,
    ILogger<TwitchEventSubConnectionSession> log
) : ITwitchEventSubConnectionSession
{
    private static readonly ObserverEventIdentity _chatMessageEvent = ObserverEventIdentity.Named(
        "TwitchChatMessage"
    );
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri _defaultEndpoint = new("wss://eventsub.wss.twitch.tv/ws");
    private readonly ITwitchChatMessageObserver[] _messageObservers = [.. messageObservers];
    private ILogger<TwitchEventSubConnectionSession> _log { get; } = log;

    public async Task<TwitchRuntimeSessionEstablishment> EstablishAsync(
        TwitchRuntimeConnectionTarget target,
        CancellationToken cancellationToken
    )
    {
        var channelLogins = TwitchChannelList.Normalize(
            await channels.GetChannelsAsync(cancellationToken)
        );
        if (
            channelLogins.Length == 0
            && !channelSessions.HasPendingReconciliation
            && target is TwitchRuntimeConnectionTarget.Initial
        )
        {
            status.SetConnected(false, []);
            _log.LogWarning(
                "No Twitch channels are configured for the bot runtime; waiting for hosted channels."
            );
            return new TwitchRuntimeSessionEstablishment.Idle();
        }

        var endpoint = target switch
        {
            TwitchRuntimeConnectionTarget.Initial => _defaultEndpoint,
            TwitchRuntimeConnectionTarget.EventSubReconnect reconnect => reconnect.Uri,
            _ => throw new UnreachableException("Unknown EventSub connection target."),
        };
        var socket = new ClientWebSocket();
        TwitchEventSubChannelSession? channelSession = null;
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken);
            _log.LogInformation("Connected to Twitch EventSub WebSocket.");
            var json =
                await ReadTextMessageAsync(socket, cancellationToken)
                ?? throw new IOException("EventSub WebSocket disconnected.");
            var envelope = JsonSerializer.Deserialize<TwitchEventSubEnvelope>(json, _jsonOptions);
            var messageType = TwitchEventSubMessageTypes.Parse(envelope?.Metadata.MessageType);
            if (messageType is not TwitchEventSubMessageType.SessionWelcome)
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
            return new TwitchRuntimeSessionEstablishment.Established
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
        return TwitchChannelList.Normalize(await channels.GetChannelsAsync(cancellationToken));
    }

    internal async Task DispatchChatMessageAsync(
        TwitchEventSubChatMessageEvent chatEvent,
        string rawJson,
        CancellationToken cancellationToken
    )
    {
        var text = chatEvent.Message?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

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
        _ = await messageObserverFanOut.DispatchAsync(
            _messageObservers,
            _ => new ObserverDispatch<TwitchChatMessage, TwitchChatObserverDeadLetter>
            {
                Event = message,
                EventIdentity = _chatMessageEvent,
                DeadLetter = new TwitchChatObserverDeadLetter(message.Channel),
            },
            observer => ObserverIdentity.For(observer.GetType()),
            static (observer, chatMessage, token) =>
                observer.MessageReceivedAsync(chatMessage, token),
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
        TwitchEventSubChannelSession? channelSession,
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
        TwitchEventSubConnectionSession owner,
        ClientWebSocket socket,
        TwitchEventSubChannelSession channelSession,
        IReadOnlyList<string> initialChannels
    ) : ITwitchRuntimeEstablishedSession
    {
        public async Task<TwitchRuntimeReconnectRequest> ListenAsync(
            CancellationToken cancellationToken
        )
        {
            channelSession.Start(initialChannels, cancellationToken);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var json =
                    await ReadTextMessageAsync(socket, cancellationToken)
                    ?? throw new IOException("EventSub WebSocket disconnected.");
                var envelope = JsonSerializer.Deserialize<TwitchEventSubEnvelope>(
                    json,
                    _jsonOptions
                );
                var rawMessageType = envelope?.Metadata.MessageType;
                var messageType = TwitchEventSubMessageTypes.Parse(rawMessageType);

                switch (messageType)
                {
                    case TwitchEventSubMessageType.SessionWelcome:
                        throw new InvalidOperationException(
                            "EventSub sent a duplicate session welcome message."
                        );

                    case TwitchEventSubMessageType.SessionKeepalive:
                        channelSession.TriggerReconciliation(
                            await owner.GetDesiredChannelsAsync(cancellationToken),
                            TwitchEventSubChannelRecoveryTrigger.Keepalive
                        );
                        break;

                    case TwitchEventSubMessageType.SessionReconnect:
                        owner._log.LogInformation("Twitch requested EventSub WebSocket reconnect.");
                        return new TwitchRuntimeReconnectRequest
                        {
                            Target = new TwitchRuntimeConnectionTarget.EventSubReconnect
                            {
                                Uri = RequireReconnectEndpoint(
                                    envelope?.Payload.Session?.ReconnectUrl
                                ),
                            },
                        };

                    case TwitchEventSubMessageType.Notification:
                        if (envelope?.Payload.Event is { } chatEvent)
                        {
                            await owner.DispatchChatMessageAsync(
                                chatEvent,
                                json,
                                cancellationToken
                            );
                        }

                        break;

                    case TwitchEventSubMessageType.Revocation:
                        owner._log.LogWarning("EventSub subscription was revoked.");
                        throw new InvalidOperationException(
                            "EventSub chat subscription was revoked."
                        );

                    case TwitchEventSubMessageType.Unknown:
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
