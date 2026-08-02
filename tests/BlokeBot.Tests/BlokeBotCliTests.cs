using System.Globalization;
using BlokeBot.Cli;
using Shouldly;
using Spectre.Console;

namespace BlokeBot.Tests;

public sealed class BlokeBotCliTests
{
    [Test]
    public async Task NoArguments_RendersHelpWithSuccessfulExit()
    {
        var response = await TerminalAsync([]);

        response.ExitCode.ShouldBe(0);
        response.Output.ShouldContain("blokebot 0.0.0-dev+");
        response.Output.ShouldContain(
            "blokebot serve [--host HOST] [--port PORT] [--data-dir PATH] [--config PATH]"
        );
        response.Output.ShouldContain("TwitchBot__Identity__ClientSecret");
        response.Output.ShouldContain("$XDG_STATE_HOME/blokebot");
        response.Output.ShouldContain("Explicit database/token configuration overrides --data-dir");
    }

    [Test]
    public async Task VersionCommand_RendersDevelopmentVersionWithFullRevision()
    {
        var response = await TerminalAsync(["version"]);

        response.ExitCode.ShouldBe(0);
        response.Output.Trim().ShouldBe($"blokebot {BlokeBotVersion.Current}");
    }

    [Test]
    public async Task ServeCommand_HandsOnlyDocumentedOptionsToRuntime()
    {
        var runtime = new CapturingRuntime();

        var response = await TerminalAsync(
            [
                "serve",
                "--host",
                "0.0.0.0",
                "--port",
                "9090",
                "--data-dir",
                "/tmp/blokebot-state",
                "--config",
                "operator.json",
            ],
            runtime
        );

        response.ExitCode.ShouldBe(23);
        runtime.Options.ShouldBe(
            new BlokeBotServeOptions("0.0.0.0", 9090, "/tmp/blokebot-state", "operator.json")
        );
    }

    [Test]
    public async Task UnknownCommandAndAspNetPassThrough_ReturnSafeNonzeroSummary()
    {
        var unknown = await TerminalAsync(["start-secret-value"]);
        var passThrough = await TerminalAsync(["serve", "--urls", "http://secret.invalid"]);

        unknown.ExitCode.ShouldNotBe(0);
        unknown.Output.ShouldContain("blokebot failed (CommandParseException).");
        unknown.Output.ShouldNotContain("start-secret-value");
        passThrough.ExitCode.ShouldNotBe(0);
        passThrough.Output.ShouldNotContain("secret.invalid");
    }

    [Test]
    public void InformationalVersion_Display_IsDeterministicForTaggedAndDevelopmentBuilds()
    {
        BlokeBotVersion.Display("1.2.3+build.47").ShouldBe("1.2.3");
        BlokeBotVersion
            .Display("0.0.0-dev+0123456789abcdef0123456789abcdef01234567")
            .ShouldBe("0.0.0-dev+0123456789abcdef0123456789abcdef01234567");
    }

    private static async Task<CliResponse> TerminalAsync(
        IReadOnlyList<string> arguments,
        IBlokeBotCommandRuntime? runtime = null
    )
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var console = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Interactive = InteractionSupport.No,
                Out = new AnsiConsoleOutput(writer),
            }
        );

        var exitCode = await BlokeBotCli.RunAsync(arguments, runtime, console);
        return new CliResponse(exitCode, writer.ToString());
    }

    private sealed class CapturingRuntime : IBlokeBotCommandRuntime
    {
        internal BlokeBotServeOptions? Options { get; private set; }

        public Task<int> ServeAsync(
            BlokeBotServeOptions options,
            IAnsiConsole console,
            CancellationToken cancellationToken
        )
        {
            Options = options;
            return Task.FromResult(23);
        }
    }

    private sealed record CliResponse(int ExitCode, string Output);
}
