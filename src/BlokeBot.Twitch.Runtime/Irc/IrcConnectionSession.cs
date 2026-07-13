using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using BlokeBot.Eventing;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal interface IIrcConnectionSession
{
    Task<RuntimeSessionEstablishment> EstablishAsync(
        RuntimeConnectionTarget target,
        CancellationToken cancellationToken
    );
}

internal sealed class IrcConnectionSession(
    BotSettings settings,
    IBotChannelProvider channels,
    IAccessTokenProvider tokens,
    ChatCommandDispatcher dispatcher,
    IBotChannelLifecycleNotifier lifecycleNotifier,
    IPublicChatMessageSender sender,
    ICommandResponseSender responses,
    BotRuntimeStatusStore status,
    IEnumerable<IChatMessageObserver> messageObservers,
    ObserverFanOut<
        IrcMessageObserverBoundary,
        ChatMessage,
        ChatObserverDeadLetter
    > messageObserverFanOut,
    ILogger<IrcConnectionSession> log
) : IIrcConnectionSession
{
    private static readonly ObserverEventIdentity _chatMessageEvent = ObserverEventIdentity.Named(
        "TwitchChatMessage"
    );
    private readonly IChatMessageObserver[] _messageObservers = [.. messageObservers];
    private readonly BotSettings _opts = settings;
    private ILogger<IrcConnectionSession> _log { get; } = log;

    public async Task<RuntimeSessionEstablishment> EstablishAsync(
        RuntimeConnectionTarget target,
        CancellationToken cancellationToken
    )
    {
        if (target is not RuntimeConnectionTarget.Initial)
        {
            throw new UnreachableException(
                "IRC sessions can only establish the default Twitch endpoint."
            );
        }

        var channelLogins = BotChannelList.Normalize(
            await channels.GetChannelsAsync(cancellationToken)
        );
        if (channelLogins.Length == 0)
        {
            status.SetConnected(false, []);
            _log.LogWarning(
                "No Twitch channels are configured for the bot runtime; waiting for hosted channels."
            );
            return new RuntimeSessionEstablishment.Idle();
        }

        var accessToken = await tokens.GetAccessTokenAsync(cancellationToken);
        status.SetAuthorized(true);

        var tcp = new TcpClient();
        StreamReader? reader = null;
        StreamWriter? writer = null;
        try
        {
            await tcp.ConnectAsync(_opts.Connection.Host, _opts.Connection.Port, cancellationToken);
            var stream = await OpenStreamAsync(tcp, cancellationToken);
            reader = new StreamReader(stream, Encoding.UTF8);
            writer = new StreamWriter(stream, Encoding.UTF8) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteLineAsync(
                "CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership"
            );
            await writer.WriteLineAsync($"PASS oauth:{accessToken}");
            await writer.WriteLineAsync($"NICK {_opts.Identity.BotUsername}");
            var joinedChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var startedChannels = new List<string>();
            foreach (var channel in channelLogins)
            {
                await JoinChannelAsync(writer, channel, cancellationToken);
                joinedChannels.Add(channel);
                startedChannels.Add(channel);
            }
            await AwaitAuthenticationAsync(reader, writer, cancellationToken);
            status.SetConnected(joinedChannels.Count > 0, joinedChannels.ToArray());
            foreach (var channel in startedChannels)
            {
                await lifecycleNotifier.ChannelStartedAsync(channel, cancellationToken);
            }

            _log.LogInformation(
                "Twitch IRC authentication sent for {BotUsername}; joining {ChannelCount} channels.",
                _opts.Identity.BotUsername,
                channelLogins.Length
            );

            return new RuntimeSessionEstablishment.Established
            {
                Session = new EstablishedSession(this, tcp, reader, writer, joinedChannels),
            };
        }
        catch
        {
            if (writer is not null)
            {
                await writer.DisposeAsync();
            }

            reader?.Dispose();
            tcp.Dispose();
            throw;
        }
    }

    internal async Task DispatchChatMessageAsync(
        ChatMessage message,
        CancellationToken cancellationToken
    )
    {
        await NotifyMessageObserversAsync(message, cancellationToken);
        await dispatcher.DispatchResponsesAsync(
            message,
            async (response, ct) =>
            {
                _log.LogInformation(
                    "Queueing Twitch {Target} response to #{Channel}.",
                    response.Target,
                    message.Channel
                );
                await responses.SendAsync(message, response, ct);
            },
            cancellationToken
        );
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

    internal async Task SyncJoinedChannelsAsync(
        StreamWriter writer,
        HashSet<string> joinedChannels,
        CancellationToken cancellationToken
    )
    {
        var desiredChannels = BotChannelList.Normalize(
            await channels.GetChannelsAsync(cancellationToken)
        );
        var startedChannels = new List<string>();
        var stoppedChannels = new List<string>();

        foreach (
            var channel in joinedChannels
                .Except(desiredChannels, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        )
        {
            await writer.WriteLineAsync($"PART #{channel}");
            joinedChannels.Remove(channel);
            stoppedChannels.Add(channel);
            _log.LogInformation("Parted Twitch IRC channel #{Channel}.", channel);
        }

        foreach (
            var channel in desiredChannels.Except(joinedChannels, StringComparer.OrdinalIgnoreCase)
        )
        {
            await JoinChannelAsync(writer, channel, cancellationToken);
            joinedChannels.Add(channel);
            startedChannels.Add(channel);
            _log.LogInformation("Joined Twitch IRC channel #{Channel}.", channel);
        }

        status.SetConnected(joinedChannels.Count > 0, joinedChannels.ToArray());
        foreach (var channel in stoppedChannels)
        {
            await lifecycleNotifier.ChannelStoppedAsync(channel, cancellationToken);
        }

        foreach (var channel in startedChannels)
        {
            await lifecycleNotifier.ChannelStartedAsync(channel, cancellationToken);
        }
    }

    private async Task JoinChannelAsync(
        StreamWriter writer,
        string channel,
        CancellationToken cancellationToken
    )
    {
        await writer.WriteLineAsync($"JOIN #{channel}");
        var startupMessage = _opts.StartupMessage;
        if (string.IsNullOrWhiteSpace(startupMessage))
        {
            return;
        }

        var outcome = await sender.SendAsync(
            channel,
            startupMessage,
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            cancellationToken
        );
        outcome
            .Match<Action>(
                static _ => static () => { },
                _ =>
                    () =>
                        _log.LogWarning(
                            "IRC startup public-chat message for channel #{Channel} was rejected before durable enqueue; no delivery was attempted.",
                            channel
                        )
            )
            .Invoke();
    }

    private async Task AwaitAuthenticationAsync(
        StreamReader reader,
        StreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var line =
                await reader.ReadLineAsync(cancellationToken)
                ?? throw new IOException("Disconnected before IRC authentication completed.");
            if (IrcProtocol.IsPing(line))
            {
                await writer.WriteLineAsync(IrcProtocol.CreatePong(line));
                continue;
            }

            LogServerLine(line);
            if (line.Contains(" NOTICE ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Twitch rejected IRC session establishment.");
            }

            if (line.Contains(" 001 ", StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    private void LogServerLine(string line)
    {
        if (line.Contains(" NOTICE ", StringComparison.Ordinal))
        {
            _log.LogWarning("Twitch IRC notice received.");
            return;
        }

        if (line.Contains(" 001 ", StringComparison.Ordinal))
        {
            _log.LogInformation(
                "Twitch IRC authenticated as {BotUsername}.",
                _opts.Identity.BotUsername
            );
            return;
        }

        if (line.Contains(" JOIN ", StringComparison.Ordinal))
        {
            _log.LogInformation("Twitch IRC join event received.");
            return;
        }

        if (line.Contains(" ROOMSTATE ", StringComparison.Ordinal))
        {
            _log.LogInformation("Twitch IRC room state received.");
            return;
        }

        if (line.Contains(" USERSTATE ", StringComparison.Ordinal))
        {
            _log.LogInformation("Twitch IRC user state received.");
            return;
        }

        if (line.Contains(" GLOBALUSERSTATE ", StringComparison.Ordinal))
        {
            _log.LogInformation("Twitch IRC global user state received.");
            return;
        }

        if (line.Contains(" CLEARCHAT ", StringComparison.Ordinal))
        {
            _log.LogWarning("Twitch IRC clear chat event received.");
            return;
        }

        _log.LogTrace("Twitch IRC message received.");
    }

    private async ValueTask<Stream> OpenStreamAsync(
        TcpClient tcp,
        CancellationToken cancellationToken
    )
    {
        var networkStream = tcp.GetStream();
        if (!_opts.Connection.UseTls)
        {
            return networkStream;
        }

        var ssl = new SslStream(networkStream, leaveInnerStreamOpen: false);
        await ssl.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = _opts.Connection.Host },
            cancellationToken
        );
        return ssl;
    }

    private sealed class EstablishedSession(
        IrcConnectionSession owner,
        TcpClient tcp,
        StreamReader reader,
        StreamWriter writer,
        HashSet<string> joinedChannels
    ) : IRuntimeEstablishedSession
    {
        public async Task<RuntimeReconnectRequest> ListenAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await owner.SyncJoinedChannelsAsync(writer, joinedChannels, cancellationToken);
                var line =
                    await reader.ReadLineAsync(cancellationToken)
                    ?? throw new IOException("Disconnected.");
                if (IrcProtocol.IsPing(line))
                {
                    await writer.WriteLineAsync(IrcProtocol.CreatePong(line));
                    continue;
                }

                owner.LogServerLine(line);

                var parseResult = IrcProtocol.ParsePrivMsg(line);
                if (!parseResult.Success)
                {
                    continue;
                }

                var message = parseResult.Message;
                owner._log.LogDebug(
                    "Received Twitch chat message from {Login} in #{Channel}.",
                    message.Login,
                    message.Channel
                );

                await owner.DispatchChatMessageAsync(message, cancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Exception? failure = null;
            try
            {
                await writer.DisposeAsync();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                reader.Dispose();
            }
            catch (Exception exception)
            {
                failure = CombineCleanupFailures(failure, exception);
            }

            try
            {
                tcp.Dispose();
            }
            catch (Exception exception)
            {
                failure = CombineCleanupFailures(failure, exception);
            }

            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private static Exception CombineCleanupFailures(Exception? previous, Exception current)
        {
            return previous is null
                ? current
                : new AggregateException("IRC session cleanup failed.", previous, current);
        }
    }
}
