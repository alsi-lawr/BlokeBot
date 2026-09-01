using System.Globalization;
using BlokeBot.Cli;
using Shouldly;
using Spectre.Console;

namespace BlokeBot.Tests;

public sealed class BlokeBotCliTests
{
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
    public async Task DatabaseCutover_MissingSourceDoesNotCreateSQLiteDatabase()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-cutover-cli-tests-{Guid.NewGuid():N}"
        );
        _ = Directory.CreateDirectory(root);
        try
        {
            var response = await TerminalAsync([
                "database",
                "cutover-postgresql",
                "--data-dir",
                root,
                "--postgresql-connection-string-file",
                Path.Combine(root, "target.connection"),
            ]);

            response.ExitCode.ShouldNotBe(0);
            File.Exists(Path.Combine(root, "blokebot.db")).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
