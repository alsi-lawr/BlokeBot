using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

public sealed class TwitchBotWorker : BackgroundService
{
    private readonly TwitchBotOptions _opts;
    private readonly ICounterStore _store;
    private readonly ILogger<TwitchBotWorker> _log;
    private readonly IAccessTokenProvider _tokens;

    private int deaths;

    public TwitchBotWorker(
        IOptions<TwitchBotOptions> options,
        ICounterStore store,
        ILogger<TwitchBotWorker> log,
        IAccessTokenProvider tokens
    )
    {
        _opts = options.Value;
        _store = store;
        _log = log;
        _tokens = tokens;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        deaths = await _store.LoadAsync(CounterKeys.Deaths, stoppingToken);

        var allowed = new HashSet<string>(
            _opts.Filters.AllowedLogins,
            StringComparer.OrdinalIgnoreCase
        );

        var app = new CommandApp()
            .Use(new AllowedUsersFilter(allowed))
            .Map(
                "deaths",
                async (ctx, args) =>
                {
                    if (args.Length != 1 || !int.TryParse(args.Span[0], out var v) || v < 0)
                    {
                        await ctx.Say("Usage: !deaths <deaths>");
                        return;
                    }

                    deaths = v;
                    await _store.SaveAsync(CounterKeys.Deaths, deaths, stoppingToken);
                    await ctx.Say($"Oh no, I've died {deaths} times");
                }
            )
            .Map(
                "deathsi",
                async (ctx, _) =>
                {
                    deaths++;
                    await _store.SaveAsync(CounterKeys.Deaths, deaths, stoppingToken);
                    await ctx.Say($"Oh no, I've died {deaths} times");
                }
            );

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunIrcLoop(app, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _log.LogError(ex, "IRC loop crashed; reconnecting.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task RunIrcLoop(CommandApp app, CancellationToken ct)
    {
        // OAuth: get a valid access token (refreshes / first-time auth if needed)
        var accessToken = await _tokens.GetAsync(ct);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(_opts.Connection.Host, _opts.Connection.Port, ct);

        await using var stream = tcp.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        await using var writer = new StreamWriter(stream, Encoding.UTF8)
        {
            NewLine = "\r\n",
            AutoFlush = true,
        };

        await writer.WriteLineAsync("CAP REQ :twitch.tv/tags twitch.tv/commands");
        await writer.WriteLineAsync($"PASS oauth:{accessToken}");
        await writer.WriteLineAsync($"NICK {_opts.Identity.BotUsername}");
        await writer.WriteLineAsync($"JOIN #{_opts.Channel}");

        var say = (ChatSender)(msg => writer.WriteLineAsync($"PRIVMSG #{_opts.Channel} :{msg}"));

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
                throw new IOException("Disconnected.");

            if (line.StartsWith("PING ", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync(line.Replace("PING", "PONG", StringComparison.Ordinal));
                continue;
            }

            if (!TryParsePrivMsg(line, out var login, out var message))
                continue;

            await app.Dispatch(new CommandContext(login, message, say));
        }
    }

    private static bool TryParsePrivMsg(string line, out string login, out string message)
    {
        login = string.Empty;
        message = string.Empty;

        if (!line.Contains(" PRIVMSG ", StringComparison.Ordinal))
            return false;

        var excl = line.IndexOf('!');
        if (excl <= 1 || line[0] != ':')
            return false;

        var msgIdx = line.IndexOf(" :", StringComparison.Ordinal);
        if (msgIdx < 0)
            return false;

        login = line[1..excl];
        message = line[(msgIdx + 2)..];
        return true;
    }
}
