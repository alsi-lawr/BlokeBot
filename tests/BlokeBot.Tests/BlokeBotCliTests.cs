using BlokeBot.Cli;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class BlokeBotCliTests
{
    [Test]
    public void NoArguments_Parsing_RendersHelpWithSuccessfulExit()
    {
        var response = Terminal([]);

        response.ExitCode.ShouldBe(0);
        response.StandardOutput.ShouldContain("blokebot 0.1.0");
        response.StandardOutput.ShouldContain("blokebot serve [--data-dir PATH] [ASP.NET options]");
        response.StandardOutput.ShouldContain("BlokeBot is free, open-source, and easy to host.");
        response.StandardOutput.ShouldContain("BotUsername");
        response.StandardOutput.ShouldContain("ClientId");
        response.StandardOutput.ShouldContain("ClientSecret");
        response.StandardOutput.ShouldContain("RedirectUri");
        response.StandardOutput.ShouldContain(
            "must exactly match the callback registered with Twitch"
        );
        response.StandardOutput.ShouldContain("$XDG_STATE_HOME/blokebot");
        response.StandardOutput.ShouldContain("~/.local/state/blokebot");
        response.StandardOutput.ShouldContain("~/Library/Application Support/BlokeBot");
        response.StandardOutput.ShouldContain("%LOCALAPPDATA%\\BlokeBot");
        response.StandardOutput.ShouldContain(
            "Explicit database/token configuration overrides --data-dir"
        );
        response.StandardOutput.ShouldContain(
            "https://github.com/alsi-lawr/BlokeBot/wiki/User-Guide"
        );
        response.StandardOutput.ShouldContain(
            "https://github.com/alsi-lawr/BlokeBot/wiki/Server-Owner-Guide"
        );
        response.StandardOutput.ShouldNotContain("Self-hosted Twitch bot and dashboard.");
        response.StandardOutput.ShouldNotContain("does not open a browser or install a service");
        response.StandardError.ShouldBeEmpty();
    }

    [Test]
    public void HelpCommand_Parsing_RendersHelpWithSuccessfulExit()
    {
        var response = Terminal(["help"]);

        response.ExitCode.ShouldBe(0);
        response.StandardOutput.ShouldContain("Commands:");
        response.StandardError.ShouldBeEmpty();
    }

    [Test]
    public void VersionCommand_Parsing_RendersSemanticPackageVersion()
    {
        var response = Terminal(["version"]);

        response.ExitCode.ShouldBe(0);
        response.StandardOutput.ShouldBe($"blokebot 0.1.0{Environment.NewLine}");
        response.StandardError.ShouldBeEmpty();
        typeof(BlokeBotCli).Assembly.GetName().Name.ShouldBe("blokebot");
    }

    [Test]
    public void UnknownCommand_Parsing_RendersHelpWithNonzeroExit()
    {
        var response = Terminal(["start"]);

        response.ExitCode.ShouldBe(BlokeBotCli.InvalidCommandExitCode);
        response.StandardOutput.ShouldBeEmpty();
        response.StandardError.ShouldContain("Unknown command 'start'.");
        response.StandardError.ShouldContain("blokebot serve");
    }

    [Test]
    public void UndocumentedAliases_Parsing_AreUnknownCommands()
    {
        foreach (var alias in new[] { "-h", "--help", "-v", "--version" })
        {
            var response = Terminal([alias]);

            response.ExitCode.ShouldBe(BlokeBotCli.InvalidCommandExitCode);
            response.StandardOutput.ShouldBeEmpty();
            response.StandardError.ShouldContain($"Unknown command '{alias}'.");
            response.StandardError.ShouldContain("blokebot serve");
        }
    }

    [Test]
    public void ServeWithDataDirectory_Parsing_ConsumesOnlyDataDirectoryOption()
    {
        var invocation = BlokeBotCli.Parse([
            "serve",
            "--environment",
            "Development",
            "--data-dir",
            "/tmp/blokebot-state",
            "--urls",
            "http://127.0.0.1:0",
        ]);

        var serve = invocation.ShouldBeOfType<BlokeBotCliInvocation.Serve>();
        serve.DataDirectory.ShouldBe("/tmp/blokebot-state");
        serve.AspNetArguments.ShouldBe([
            "--environment",
            "Development",
            "--urls",
            "http://127.0.0.1:0",
        ]);
    }

    [Test]
    public void MissingOrRepeatedDataDirectory_Parsing_RendersActionableFailure()
    {
        var missing = Terminal(["serve", "--data-dir", "--urls", "http://127.0.0.1:0"]);
        var repeated = Terminal(["serve", "--data-dir", "/first", "--data-dir", "/second"]);

        missing.ExitCode.ShouldBe(BlokeBotCli.InvalidCommandExitCode);
        missing.StandardError.ShouldContain("requires a path");
        repeated.ExitCode.ShouldBe(BlokeBotCli.InvalidCommandExitCode);
        repeated.StandardError.ShouldContain("can only be specified once");
    }

    private static BlokeBotCliTerminalResponse Terminal(IReadOnlyList<string> arguments)
    {
        return BlokeBotCli.Render(BlokeBotCli.Parse(arguments));
    }
}
