using BlokeBot.PluginHarness;
using BlokeBot.Plugins.Contracts.Testing;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginHarnessCliTests
{
    [Test]
    public async Task AuthorCommands_UseArbitraryLocalSourceAndOutputDirectories()
    {
        using var source = new TemporaryDirectory("source");
        using var output = new TemporaryDirectory("output");
        await WritePluginAsync(source.Path);
        var standardOutput = new StringWriter();
        var standardError = new StringWriter();

        var validationExit = await PluginHarnessCli.RunAsync(
            ["validate", source.Path],
            standardOutput,
            standardError,
            CancellationToken.None
        );
        await WriteTestsAsync(source.Path);
        var testExit = await PluginHarnessCli.RunAsync(
            ["test", source.Path],
            standardOutput,
            standardError,
            CancellationToken.None
        );
        var generationExit = await PluginHarnessCli.RunAsync(
            ["generate-sdk", output.Path],
            standardOutput,
            standardError,
            CancellationToken.None
        );

        validationExit.ShouldBe((int)PluginHarnessExitCode.Success);
        testExit.ShouldBe((int)PluginHarnessExitCode.Success);
        generationExit.ShouldBe((int)PluginHarnessExitCode.Success);
        standardError.ToString().ShouldBeEmpty();
        standardOutput.ToString().ShouldContain("validated:");
        standardOutput.ToString().ShouldContain("targets=5");
        standardOutput.ToString().ShouldContain("tested: arbitrary-author-source");
        File.Exists(Path.Combine(output.Path, "sdk", "lua", "5.4", "v1", "blokebot.lua"))
            .ShouldBeTrue();
        File.Exists(Path.Combine(output.Path, "docs", "plugin-authoring", "v1.md")).ShouldBeTrue();
    }

    [Test]
    [Arguments(null, PublishedPluginExampleFailureCode.TestMetadataMissing)]
    [Arguments("name = [", PublishedPluginExampleFailureCode.TestMetadataMalformed)]
    [Arguments(
        "name = \"invalid\"\n[[scenarios]]\nname = \"bad\"\nworkerMode = \"unknown\"\ninvocationKind = \"command\"\nmodule = \"main\"\noperation = \"run\"\nexpectation = \"returned\"",
        PublishedPluginExampleFailureCode.TestMetadataInvalid
    )]
    public async Task Test_InvalidAuthorMetadataUsesTypedTestFailure(
        string? metadata,
        PublishedPluginExampleFailureCode expectedFailure
    )
    {
        using var source = new TemporaryDirectory("invalid-test-metadata");
        await WritePluginAsync(source.Path);
        if (metadata is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(source.Path, "tests.toml"), metadata);
        }
        var standardError = new StringWriter();

        var exit = await PluginHarnessCli.RunAsync(
            ["test", source.Path],
            TextWriter.Null,
            standardError,
            CancellationToken.None
        );

        exit.ShouldBe((int)PluginHarnessExitCode.TestFailed);
        standardError.ToString().ShouldContain(expectedFailure.ToString());
    }

    [Test]
    public async Task Test_InvalidSourceAndPackageUseTypedExitsBeforeWorkerExecution()
    {
        using var source = new TemporaryDirectory("invalid-package");
        await WritePluginAsync(source.Path);
        await WriteTestsAsync(source.Path);
        await File.WriteAllTextAsync(Path.Combine(source.Path, "plugin.toml"), "not = [toml");
        var packageError = new StringWriter();
        var missingError = new StringWriter();
        var invalidPathError = new StringWriter();

        var packageExit = await PluginHarnessCli.RunAsync(
            ["test", source.Path],
            TextWriter.Null,
            packageError,
            CancellationToken.None
        );
        var sourceExit = await PluginHarnessCli.RunAsync(
            ["test", Path.Combine(source.Path, "missing")],
            TextWriter.Null,
            missingError,
            CancellationToken.None
        );
        var invalidPathExit = await PluginHarnessCli.RunAsync(
            ["test", "\0"],
            TextWriter.Null,
            invalidPathError,
            CancellationToken.None
        );

        packageExit.ShouldBe((int)PluginHarnessExitCode.ValidationFailed);
        packageError
            .ToString()
            .ShouldContain(PublishedPluginExampleFailureCode.PackageRejected.ToString());
        sourceExit.ShouldBe((int)PluginHarnessExitCode.SourceInvalid);
        missingError
            .ToString()
            .ShouldContain(PublishedPluginExampleFailureCode.SourceInvalid.ToString());
        invalidPathExit.ShouldBe((int)PluginHarnessExitCode.SourceInvalid);
        invalidPathError
            .ToString()
            .ShouldContain(PublishedPluginExampleFailureCode.SourceInvalid.ToString());
    }

    [Test]
    public async Task Validate_PreservesFilesystemLinkAsCanonicalPackageFailure()
    {
        using var source = new TemporaryDirectory("linked-source");
        await WritePluginAsync(source.Path, declaredPath: "lua/linked.lua");
        var lua = Path.Combine(source.Path, "lua");
        File.Delete(Path.Combine(lua, "linked.lua"));
        _ = File.CreateSymbolicLink(Path.Combine(lua, "linked.lua"), Path.Combine(lua, "main.lua"));
        var standardError = new StringWriter();

        var exit = await PluginHarnessCli.RunAsync(
            ["validate", source.Path],
            TextWriter.Null,
            standardError,
            CancellationToken.None
        );

        exit.ShouldBe((int)PluginHarnessExitCode.ValidationFailed);
        standardError
            .ToString()
            .ShouldContain(nameof(PluginPackageEntryErrorCode.LinkNotPermitted));
    }

    private static async Task WritePluginAsync(string root, string declaredPath = "lua/main.lua")
    {
        _ = Directory.CreateDirectory(Path.Combine(root, "lua"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "plugin.toml"),
            $$"""
            manifestVersion = 1
            id = "examples.arbitrary-source"
            name = "Arbitrary author source"
            description = "Exercises an author-selected directory."
            entryModule = "main"
            assets = []
            payloads = []
            settings = []
            features = []
            hostModules = [{ id = "diagnostics", minimumVersion = 1, maximumVersion = 1 }]
            migrations = []
            automationDefinitions = []
            automationTemplates = []
            generatedPages = []
            embeddedPages = []

            [release]
            declaredVersion = "1.0.0"
            tag = "examples-arbitrary-source-v1"

            [compatibility]
            minimumApiVersion = 1
            maximumApiVersion = 1
            minimumBlokeBotVersion = "0.13.0"
            maximumBlokeBotVersionExclusive = "0.14.0"
            luaVersion = "lua54"

            [[luaModules]]
            id = "main"
            path = "{{declaredPath}}"
            """
        );
        await File.WriteAllTextAsync(
            Path.Combine(root, declaredPath.Replace('/', Path.DirectorySeparatorChar)),
            "return { run = function(input) blokebot.host.call('diagnostics', 'log', 'information', input.message); return input end }\n"
        );
        if (declaredPath != "lua/main.lua")
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "lua", "main.lua"),
                "return { run = function(input) blokebot.host.call('diagnostics', 'log', 'information', input.message); return input end }\n"
            );
        }
    }

    private static Task WriteTestsAsync(string root) =>
        File.WriteAllTextAsync(
            Path.Combine(root, "tests.toml"),
            """
            name = "arbitrary-author-source"

            [[scenarios]]
            name = "round-trip"
            workerMode = "admitted"
            invocationKind = "command"
            module = "main"
            operation = "run"
            expectation = "returned"
            input = { message = "typed scenario input", nested = { enabled = true }, values = ["one", 2] }
            expectedHostCalls = ["diagnostics.log"]
            """
        );

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory(string purpose)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"blokebot-plugin-harness-{purpose}-{Guid.NewGuid():N}"
            );
            _ = Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
