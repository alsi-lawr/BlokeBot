using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using BlokeBot.Eventing;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal interface ITwitchIrcConnectionSession
{
    Task<TwitchRuntimeSessionEstablishment> EstablishAsync(
        TwitchRuntimeConnectionTarget target,
        CancellationToken cancellationToken
    );
}

internal sealed class TwitchIrcConnectionSession(
    TwitchBotSettings settings,
    ITwitchBotChannelProvider channels,
    ITwitchAccessTokenProvider tokens,
    TwitchCommandDispatcher dispatcher,
    ITwitchBotChannelLifecycleNotifier lifecycleNotifier,
    ITwitchChatMessageSender sender,
    ITwitchCommandResponseSender responses,
    TwitchBotRuntimeStatusStore status,
    IEnumerable<ITwitchChatMessageObserver> messageObservers,
    ObserverFanOut<
        TwitchIrcMessageObserverBoundary,
        TwitchChatMessage,
        TwitchChatObserverDeadLetter
    > messageObserverFanOut,
    ILogger<TwitchIrcConnectionSession> log
) : ITwitchIrcConnectionSession
{
    private static readonly ObserverEventIdentity ChatMessageEvent =
        ObserverEventIdentity.Named("TwitchChatMessage");
    private readonly ITwitchChatMessageObserver[] messageObservers =
        [.. messageObservers];
    private readonly TwitchBotSettings opts = settings;
    private ILogger<TwitchIrcConnectionSession> Log { get; } = log;

    public async Task<TwitchRuntimeSessionEstablishment> EstablishAsync(
        TwitchRuntimeConnectionTarget target,
        CancellationToken cancellationToken
    )
    {
        if (target is not TwitchRuntimeConnectionTarget.Initial)
            throw new UnreachableException(
                "IRC sessions can only establish the default Twitch endpoint."
            );

        var channelLogins = TwitchChannelList.Normalize(
            await channels.GetChannelsAsync(cancellationToken)
        );
        if (channelLogins.Length == 0)
        {
            status.SetConnected(false, []);
            Log.LogWarning(
                "No Twitch channels are configured for the bot runtime; waiting for hosted channels."
            );
            return new TwitchRuntimeSessionEstablishment.Idle();
        }

        var accessToken = await tokens.GetAccessTokenAsync(cancellationToken);
        status.SetAuthorized(true);

        var tcp = new TcpClient();
        StreamReader? reader = null;
        StreamWriter? writer = null;
        try
        {
            await tcp.ConnectAsync(
                opts.Connection.Host,
                opts.Connection.Port,
                cancellationToken
            );
            var stream = await OpenStreamAsync(tcp, cancellationToken);
            reader = new StreamReader(stream, Encoding.UTF8);
            writer = new StreamWriter(stream, Encoding.UTF8)
            {
                NewLine = "\r\n",
                AutoFlush = true,
            };

            await writer.WriteLineAsync(
                "CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership"
            );
            await writer.WriteLineAsync($"PASS oauth:{accessToken}");
            await writer.WriteLineAsync($"NICK {opts.Identity.BotUsername}");
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
                await lifecycleNotifier.ChannelStartedAsync(channel, cancellationToken);

            Log.LogInformation(
                "Twitch IRC authentication sent for {BotUsername}; joining {Channels}.",
                opts.Identity.BotUsername,
                string.Join(", ", channelLogins.Select(channel => $"#{channel}"))
            );

            return new TwitchRuntimeSessionEstablishment.Established
            {
                Session = new EstablishedSession(
                    this,
                    tcp,
                    reader,
                    writer,
                    joinedChannels
                ),
            };
        }
        catch
        {
            if (writer is not null)
                await writer.DisposeAsync();

            reader?.Dispose();
            tcp.Dispose();
            throw;
        }
    }

    internal async Task DispatchChatMessageAsync(
        TwitchChatMessage message,
        CancellationToken cancellationToken
    )
    {
        await NotifyMessageObserversAsync(message, cancellationToken);
        await dispatcher.DispatchResponsesAsync(
            message,
            async (response, ct) =>
            {
                Log.LogInformation(
                    "Queueing Twitch {Target} response to #{Channel}: {Reply}",
                    response.Target,
                    message.Channel,
                    response.Message
                );
                await responses.SendAsync(message, response, ct);
            },
            cancellationToken
        );
    }

    private async ValueTask NotifyMessageObserversAsync(
        TwitchChatMessage message,
        CancellationToken cancellationToken
    )
    {
        _ = await messageObserverFanOut.DispatchAsync(
            messageObservers,
            _ =>
                new ObserverDispatch<TwitchChatMessage, TwitchChatObserverDeadLetter>
                {
                    Event = message,
                    EventIdentity = ChatMessageEvent,
                    DeadLetter = new TwitchChatObserverDeadLetter(message.Channel),
                },
            observer => ObserverIdentity.For(observer.GetType()),
            static (observer, chatMessage, token) =>
                observer.MessageReceivedAsync(chatMessage, token),
            cancellationToken
        );
    }

    private async Task SyncJoinedChannelsAsync(
        StreamWriter writer,
        HashSet<string> joinedChannels,
        CancellationToken cancellationToken
    )
    {
        var desiredChannels = TwitchChannelList.Normalize(
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
            Log.LogInformation("Parted Twitch IRC channel #{Channel}.", channel);
        }

        foreach (
            var channel in desiredChannels.Except(joinedChannels, StringComparer.OrdinalIgnoreCase)
        )
        {
            await JoinChannelAsync(writer, channel, cancellationToken);
            joinedChannels.Add(channel);
            startedChannels.Add(channel);
            Log.LogInformation("Joined Twitch IRC channel #{Channel}.", channel);
        }

        status.SetConnected(joinedChannels.Count > 0, joinedChannels.ToArray());
        foreach (var channel in stoppedChannels)
            await lifecycleNotifier.ChannelStoppedAsync(channel, cancellationToken);

        foreach (var channel in startedChannels)
            await lifecycleNotifier.ChannelStartedAsync(channel, cancellationToken);
    }

    private async Task JoinChannelAsync(
        StreamWriter writer,
        string channel,
        CancellationToken cancellationToken
    )
    {
        await writer.WriteLineAsync($"JOIN #{channel}");
        var startupMessage = opts.StartupMessage;
        if (string.IsNullOrWhiteSpace(startupMessage))
            return;

        await sender.SendAsync(channel, startupMessage, cancellationToken);
    }

    private async Task AwaitAuthenticationAsync(
        StreamReader reader,
        StreamWriter writer,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
                throw new IOException("Disconnected before IRC authentication completed.");

            if (TwitchIrcProtocol.IsPing(line))
            {
                await writer.WriteLineAsync(TwitchIrcProtocol.CreatePong(line));
                continue;
            }

            LogServerLine(line);
            if (line.Contains(" NOTICE ", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Twitch rejected IRC session establishment."
                );

            if (line.Contains(" 001 ", StringComparison.Ordinal))
                return;
        }
    }

    private void LogServerLine(string line)
    {
        if (line.Contains(" NOTICE ", StringComparison.Ordinal))
        {
            Log.LogWarning("Twitch IRC notice: {Line}", line);
            return;
        }

        if (line.Contains(" 001 ", StringComparison.Ordinal))
        {
            Log.LogInformation(
                "Twitch IRC authenticated as {BotUsername}.",
                opts.Identity.BotUsername
            );
            return;
        }

        if (line.Contains(" JOIN ", StringComparison.Ordinal))
        {
            Log.LogInformation("Twitch IRC join event: {Line}", line);
            return;
        }

        if (line.Contains(" ROOMSTATE ", StringComparison.Ordinal))
        {
            Log.LogInformation("Twitch IRC room state: {Line}", line);
            return;
        }

        if (line.Contains(" USERSTATE ", StringComparison.Ordinal))
        {
            Log.LogInformation("Twitch IRC user state: {Line}", line);
            return;
        }

        if (line.Contains(" GLOBALUSERSTATE ", StringComparison.Ordinal))
        {
            Log.LogInformation("Twitch IRC global user state: {Line}", line);
            return;
        }

        if (line.Contains(" CLEARCHAT ", StringComparison.Ordinal))
        {
            Log.LogWarning("Twitch IRC clear chat event: {Line}", line);
            return;
        }

        Log.LogTrace("Twitch IRC line: {Line}", line);
    }

    private async ValueTask<Stream> OpenStreamAsync(
        TcpClient tcp,
        CancellationToken cancellationToken
    )
    {
        var networkStream = tcp.GetStream();
        if (!opts.Connection.UseTls)
            return networkStream;

        var ssl = new SslStream(networkStream, leaveInnerStreamOpen: false);
        await ssl.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = opts.Connection.Host },
            cancellationToken
        );
        return ssl;
    }

    private sealed class EstablishedSession(
        TwitchIrcConnectionSession owner,
        TcpClient tcp,
        StreamReader reader,
        StreamWriter writer,
        HashSet<string> joinedChannels
    ) : ITwitchRuntimeEstablishedSession
    {
        public async Task<TwitchRuntimeReconnectRequest> ListenAsync(
            CancellationToken cancellationToken
        )
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await owner.SyncJoinedChannelsAsync(
                    writer,
                    joinedChannels,
                    cancellationToken
                );
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    throw new IOException("Disconnected.");

                if (TwitchIrcProtocol.IsPing(line))
                {
                    await writer.WriteLineAsync(TwitchIrcProtocol.CreatePong(line));
                    continue;
                }

                owner.LogServerLine(line);

                var parseResult = TwitchIrcProtocol.ParsePrivMsg(line);
                if (!parseResult.Success)
                    continue;

                var message = parseResult.Message;
                owner.Log.LogDebug(
                    "Received Twitch chat message from {Login} in #{Channel}: {Text}",
                    message.Login,
                    message.Channel,
                    message.Text
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
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static Exception CombineCleanupFailures(
            Exception? previous,
            Exception current
        ) =>
            previous is null
                ? current
                : new AggregateException("IRC session cleanup failed.", previous, current);
    }
}
