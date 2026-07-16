using System.Reflection;

namespace BlokeBot.Cli;

internal static class BlokeBotCli
{
    internal const int InvalidCommandExitCode = 2;

    internal static string Version =>
        typeof(BlokeBotCli)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+', 2)[0]
        ?? "0.1.0";

    internal static string HelpText =>
        $$"""
            blokebot {{Version}}
            BlokeBot is free, open-source, and easy to host.

            Usage:
              blokebot
              blokebot help
              blokebot version
              blokebot serve [--data-dir PATH] [ASP.NET options]

            Commands:
              help     Show this help and exit.
              version  Show package version information and exit.
              serve    Start the bot and dashboard.

            Serve options:
              --data-dir PATH  Store blokebot.db and twitch.tokens.json in PATH unless
                               either path is set explicitly in configuration.

            Required Twitch configuration for online mode:
              BotUsername  TwitchBot__Identity__BotUsername; the bot account login.
              ClientId     TwitchBot__Identity__ClientId; the Twitch application ID.
              ClientSecret TwitchBot__Identity__ClientSecret; keep this credential private.
              RedirectUri  TwitchBot__Identity__RedirectUri; the OAuth callback URL, which
                           must exactly match the callback registered with Twitch.

            State data defaults:
              Linux   $XDG_STATE_HOME/blokebot, or ~/.local/state/blokebot
              macOS   ~/Library/Application Support/BlokeBot
              Windows %LOCALAPPDATA%\BlokeBot

            Explicit database/token configuration overrides --data-dir, which overrides
            the platform default. The default dashboard URL is http://127.0.0.1:8080.

            Guides:
              User Guide: https://github.com/alsi-lawr/BlokeBot/wiki/User-Guide
              Server Owner Guide: https://github.com/alsi-lawr/BlokeBot/wiki/Server-Owner-Guide
            """;

    internal static BlokeBotCliInvocation Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return new BlokeBotCliInvocation.Help();
        }

        return arguments[0] switch
        {
            "help" => arguments.Count == 1
                ? new BlokeBotCliInvocation.Help()
                : Invalid("The help command does not accept arguments."),
            "version" => arguments.Count == 1
                ? new BlokeBotCliInvocation.Version()
                : Invalid("The version command does not accept arguments."),
            "serve" => ParseServe(arguments),
            _ => Invalid($"Unknown command '{arguments[0]}'."),
        };
    }

    internal static BlokeBotCliTerminalResponse Render(BlokeBotCliInvocation invocation)
    {
        return invocation switch
        {
            BlokeBotCliInvocation.Help => new(0, HelpText + Environment.NewLine, string.Empty),
            BlokeBotCliInvocation.Version => new(
                0,
                $"blokebot {Version}{Environment.NewLine}",
                string.Empty
            ),
            BlokeBotCliInvocation.Invalid invalid => new(
                InvalidCommandExitCode,
                string.Empty,
                $"blokebot: {invalid.Message}{Environment.NewLine}{Environment.NewLine}{HelpText}{Environment.NewLine}"
            ),
            BlokeBotCliInvocation.Serve => throw new InvalidOperationException(
                "Serve commands do not have terminal output."
            ),
            _ => throw new InvalidOperationException("Unknown blokebot CLI invocation."),
        };
    }

    private static BlokeBotCliInvocation ParseServe(IReadOnlyList<string> arguments)
    {
        string? dataDirectory = null;
        var aspNetArguments = new List<string>();
        for (var index = 1; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument.StartsWith("--data-dir=", StringComparison.Ordinal))
            {
                return Invalid("Use '--data-dir PATH' with the path as a separate argument.");
            }

            if (!string.Equals(argument, "--data-dir", StringComparison.Ordinal))
            {
                aspNetArguments.Add(argument);
                continue;
            }

            if (dataDirectory is not null)
            {
                return Invalid("The --data-dir option can only be specified once.");
            }

            if (
                index + 1 >= arguments.Count
                || string.IsNullOrWhiteSpace(arguments[index + 1])
                || arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
            )
            {
                return Invalid("The --data-dir option requires a path.");
            }

            dataDirectory = arguments[++index];
        }

        return new BlokeBotCliInvocation.Serve(dataDirectory, aspNetArguments);
    }

    private static BlokeBotCliInvocation.Invalid Invalid(string message)
    {
        return new(message);
    }
}

internal abstract record BlokeBotCliInvocation
{
    private BlokeBotCliInvocation() { }

    internal sealed record Help : BlokeBotCliInvocation;

    internal sealed record Version : BlokeBotCliInvocation;

    internal sealed record Invalid(string Message) : BlokeBotCliInvocation;

    internal sealed record Serve : BlokeBotCliInvocation
    {
        internal Serve(string? dataDirectory, IEnumerable<string> aspNetArguments)
        {
            DataDirectory = dataDirectory;
            AspNetArguments = Array.AsReadOnly(aspNetArguments.ToArray());
        }

        internal string? DataDirectory { get; }

        internal IReadOnlyList<string> AspNetArguments { get; }
    }
}

internal sealed record BlokeBotCliTerminalResponse(
    int ExitCode,
    string StandardOutput,
    string StandardError
);
