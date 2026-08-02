using System.Diagnostics;
using BlokeBot.Cli;
using Shouldly;

namespace BlokeBot.Tests;

public sealed class BlokeBotProcessTests
{
    [Test]
    public async Task HelpAndVersion_RunFromUnrelatedWorkingDirectory()
    {
        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-process-tests-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(workingDirectory);
        try
        {
            var help = await RunAsync(workingDirectory, "help");
            var version = await RunAsync(workingDirectory, "version");

            help.ExitCode.ShouldBe(0);
            help.StandardOutput.ShouldContain("blokebot serve");
            version.ExitCode.ShouldBe(0);
            version.StandardOutput.Trim().ShouldBe($"blokebot {BlokeBotVersion.Current}");
            help.StandardError.ShouldBeEmpty();
            version.StandardError.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Test]
    public async Task MissingRelativeConfig_ReturnsSafeNonzeroProcessExit()
    {
        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-process-tests-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(workingDirectory);
        try
        {
            var response = await RunAsync(
                workingDirectory,
                "serve",
                "--data-dir",
                workingDirectory,
                "--config",
                "operator-secret.json"
            );

            response.ExitCode.ShouldNotBe(0);
            response.StandardOutput.ShouldContain("blokebot failed (FileNotFoundException).");
            (response.StandardOutput + response.StandardError).ShouldNotContain(
                "operator-secret.json"
            );
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    private static async Task<ProcessResponse> RunAsync(
        string workingDirectory,
        params string[] arguments
    )
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        startInfo.ArgumentList.Add(typeof(BlokeBotCli).Assembly.Location);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the BlokeBot process.");
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResponse(process.ExitCode, standardOutput, standardError);
    }

    private sealed record ProcessResponse(
        int ExitCode,
        string StandardOutput,
        string StandardError
    );
}
