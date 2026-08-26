using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.PluginHarness;

public enum PluginHarnessExitCode
{
    Success = 0,
    InvalidUsage = 2,
    SourceInvalid = 3,
    ValidationFailed = 4,
    WorkerUnavailable = 5,
    TestFailed = 6,
    OutputFailed = 7,
    Cancelled = 130,
}

public static class PluginHarnessCli
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (arguments.Count != 2)
        {
            await WriteUsageAsync(error);
            return (int)PluginHarnessExitCode.InvalidUsage;
        }

        try
        {
            return arguments[0] switch
            {
                "validate" => await ValidateAsync(arguments[1], output, error, cancellationToken),
                "test" => await TestAsync(arguments[1], output, error, cancellationToken),
                "generate-sdk" => await GenerateSdkAsync(arguments[1], output, cancellationToken),
                _ => await UnknownCommandAsync(arguments[0], error),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("cancelled: the author operation was cancelled.");
            return (int)PluginHarnessExitCode.Cancelled;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            await error.WriteLineAsync($"io-failed: {exception.Message}");
            return (int)PluginHarnessExitCode.OutputFailed;
        }
    }

    private static async Task<int> ValidateAsync(
        string source,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken
    )
    {
        var validation = await PublishedPluginExampleHarness.ValidateAsync(
            new(source),
            cancellationToken
        );
        return await ReportValidationAsync(validation, output, error);
    }

    private static async Task<int> TestAsync(
        string source,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken
    )
    {
        var worker = WorkerExecutable();
        var outcome = await PublishedPluginExampleHarness.RunAsync(
            new(source, worker),
            cancellationToken
        );
        switch (outcome)
        {
            case PublishedPluginExampleHarnessOutcome.Passed passed:
                foreach (var observation in passed.Observations)
                {
                    await output.WriteLineAsync(
                        $"tested: {observation.Example}; targets={observation.ValidatedRuntimeIdentifiers.Length}; runtime={observation.ExecutedRuntimeIdentifier}; scenarios={observation.ExecutedScenarios.Length}"
                    );
                }
                return (int)PluginHarnessExitCode.Success;
            case PublishedPluginExampleHarnessOutcome.Failed failed:
                await ReportFailuresAsync(failed.Failures, error);
                return (int)ExitCode(failed.Failures, PluginHarnessExitCode.TestFailed);
            default:
                throw new InvalidOperationException("Unknown plugin harness outcome.");
        }
    }

    private static async Task<int> GenerateSdkAsync(
        string destination,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var fullDestination = Path.GetFullPath(destination);
        await PluginAuthoringArtifacts.WriteAsync(fullDestination, cancellationToken);
        foreach (var artifact in PluginAuthoringArtifacts.Generate())
        {
            await output.WriteLineAsync(
                $"generated: {Path.Combine(fullDestination, artifact.RelativePath)}"
            );
        }
        return (int)PluginHarnessExitCode.Success;
    }

    private static async Task<int> ReportValidationAsync(
        PublishedPluginExampleValidationOutcome validation,
        TextWriter output,
        TextWriter error
    )
    {
        switch (validation)
        {
            case PublishedPluginExampleValidationOutcome.Accepted accepted:
                foreach (var observation in accepted.Observations)
                {
                    await output.WriteLineAsync(
                        $"validated: {observation.Example}; targets={observation.RuntimeIdentifiers.Length}"
                    );
                }
                return (int)PluginHarnessExitCode.Success;
            case PublishedPluginExampleValidationOutcome.Rejected rejected:
                await ReportFailuresAsync(rejected.Failures, error);
                return (int)ExitCode(rejected.Failures, PluginHarnessExitCode.ValidationFailed);
            default:
                throw new InvalidOperationException("Unknown plugin validation outcome.");
        }
    }

    private static async Task ReportFailuresAsync(
        IReadOnlyList<PublishedPluginExampleFailure> failures,
        TextWriter error
    )
    {
        foreach (var failure in failures)
        {
            await error.WriteLineAsync(
                $"{failure.Code}: example={failure.Example}; subject={failure.Subject}"
            );
        }
    }

    private static PluginHarnessExitCode ExitCode(
        IReadOnlyList<PublishedPluginExampleFailure> failures,
        PluginHarnessExitCode fallback
    ) =>
        failures.Any(failure => failure.Code == PublishedPluginExampleFailureCode.SourceInvalid)
            ? PluginHarnessExitCode.SourceInvalid
        : failures.Any(failure => failure.Code == PublishedPluginExampleFailureCode.PackageRejected)
            ? PluginHarnessExitCode.ValidationFailed
        : failures.Any(failure =>
            failure.Code == PublishedPluginExampleFailureCode.WorkerUnavailable
        )
            ? PluginHarnessExitCode.WorkerUnavailable
        : fallback;

    private static PluginWorkerExecutable WorkerExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("BLOKEBOT_PLUGIN_WORKER");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return new(configured);
        }

        var local = Path.Combine(
            AppContext.BaseDirectory,
            "plugin-worker",
            "BlokeBot.PluginWorker.dll"
        );
        return File.Exists(local) ? new(local)
            : PluginRuntimeIdentifierResolver.TryResolveCurrent(out var runtimeIdentifier)
                ? new(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "plugin-worker",
                        RuntimeIdentifier(runtimeIdentifier),
                        "BlokeBot.PluginWorker.dll"
                    )
                )
            : new(local);
    }

    private static string RuntimeIdentifier(PluginRuntimeIdentifier runtimeIdentifier) =>
        runtimeIdentifier switch
        {
            PluginRuntimeIdentifier.LinuxX64 => "linux-x64",
            PluginRuntimeIdentifier.LinuxArm64 => "linux-arm64",
            PluginRuntimeIdentifier.MacOsArm64 => "osx-arm64",
            PluginRuntimeIdentifier.WindowsX64 => "win-x64",
            PluginRuntimeIdentifier.WindowsArm64 => "win-arm64",
        };

    private static async Task<int> UnknownCommandAsync(string command, TextWriter error)
    {
        await error.WriteLineAsync($"unknown-command: {command}");
        await WriteUsageAsync(error);
        return (int)PluginHarnessExitCode.InvalidUsage;
    }

    private static Task WriteUsageAsync(TextWriter writer) =>
        writer.WriteLineAsync(
            "Usage: blokebot-plugin validate <source> | test <source> | generate-sdk <output>"
        );
}
