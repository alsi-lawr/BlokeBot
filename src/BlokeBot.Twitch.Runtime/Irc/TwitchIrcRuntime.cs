using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchIrcRuntime(
    IOptions<TwitchBotOptions> options,
    ITwitchBotChannelProvider channels,
    ITwitchAccessTokenProvider tokens,
    TwitchCommandDispatcher dispatcher,
    ITwitchBotChannelLifecycleNotifier lifecycleNotifier,
    ITwitchChatMessageSender sender,
    ITwitchCommandResponseSender responses,
    TwitchBotRuntimeStatusStore status,
    IEnumerable<ITwitchChatMessageObserver> messageObservers,
    ILogger<TwitchIrcRuntime> log
)
{
    private readonly TwitchBotOptions opts = options.Value;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunIrcLoopAsync(stoppingToken);
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
                log.LogError(ex, "IRC loop crashed; reconnecting.");
                status.SetConnected(false, []);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task RunIrcLoopAsync(CancellationToken cancellationToken)
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

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(opts.Connection.Host, opts.Connection.Port, cancellationToken);

        await using var stream = await OpenStreamAsync(tcp, cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        await using var writer = new StreamWriter(stream, Encoding.UTF8)
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
        status.SetConnected(joinedChannels.Count > 0, joinedChannels.ToArray());
        foreach (var channel in startedChannels)
            await lifecycleNotifier.ChannelStartedAsync(channel, cancellationToken);

        log.LogInformation(
            "Twitch IRC authentication sent for {BotUsername}; joining {Channels}.",
            opts.Identity.BotUsername,
            string.Join(", ", channelLogins.Select(channel => $"#{channel}"))
        );

        while (!cancellationToken.IsCancellationRequested)
        {
            await SyncJoinedChannelsAsync(writer, joinedChannels, cancellationToken);
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
                throw new IOException("Disconnected.");

            if (TwitchIrcProtocol.IsPing(line))
            {
                await writer.WriteLineAsync(TwitchIrcProtocol.CreatePong(line));
                continue;
            }

            LogServerLine(line);

            var parseResult = TwitchIrcProtocol.ParsePrivMsg(line);
            if (!parseResult.Success)
                continue;

            var message = parseResult.Message;
            log.LogDebug(
                "Received Twitch chat message from {Login} in #{Channel}: {Text}",
                message.Login,
                message.Channel,
                message.Text
            );

            await DispatchChatMessageAsync(message, cancellationToken);
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
                log.LogInformation(
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
                log.LogWarning(ex, "Twitch IRC chat message observer failed.");
            }
        }
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
            log.LogInformation("Parted Twitch IRC channel #{Channel}.", channel);
        }

        foreach (
            var channel in desiredChannels.Except(joinedChannels, StringComparer.OrdinalIgnoreCase)
        )
        {
            await JoinChannelAsync(writer, channel, cancellationToken);
            joinedChannels.Add(channel);
            startedChannels.Add(channel);
            log.LogInformation("Joined Twitch IRC channel #{Channel}.", channel);
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

    private void LogServerLine(string line)
    {
        if (line.Contains(" NOTICE ", StringComparison.Ordinal))
        {
            log.LogWarning("Twitch IRC notice: {Line}", line);
            return;
        }

        if (line.Contains(" 001 ", StringComparison.Ordinal))
        {
            log.LogInformation(
                "Twitch IRC authenticated as {BotUsername}.",
                opts.Identity.BotUsername
            );
            return;
        }

        if (line.Contains(" JOIN ", StringComparison.Ordinal))
        {
            log.LogInformation("Twitch IRC join event: {Line}", line);
            return;
        }

        if (line.Contains(" ROOMSTATE ", StringComparison.Ordinal))
        {
            log.LogInformation("Twitch IRC room state: {Line}", line);
            return;
        }

        if (line.Contains(" USERSTATE ", StringComparison.Ordinal))
        {
            log.LogInformation("Twitch IRC user state: {Line}", line);
            return;
        }

        if (line.Contains(" GLOBALUSERSTATE ", StringComparison.Ordinal))
        {
            log.LogInformation("Twitch IRC global user state: {Line}", line);
            return;
        }

        if (line.Contains(" CLEARCHAT ", StringComparison.Ordinal))
        {
            log.LogWarning("Twitch IRC clear chat event: {Line}", line);
            return;
        }

        log.LogTrace("Twitch IRC line: {Line}", line);
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
}
