using BlokeBot.PluginHarness;
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
        standardOutput.ToString().ShouldContain("validated: arbitrary-author-source; targets=5");
        standardOutput.ToString().ShouldContain("tested: arbitrary-author-source");
        File.Exists(Path.Combine(output.Path, "sdk", "lua", "5.4", "v1", "blokebot.lua"))
            .ShouldBeTrue();
        File.Exists(Path.Combine(output.Path, "docs", "plugin-authoring", "v1.md")).ShouldBeTrue();
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
            Path.Combine(root, "blokebot.example.json"),
            """
            {
              "name": "arbitrary-author-source",
              "scenarios": [{
                "name": "round-trip",
                "workerMode": "admitted",
                "invocationKind": "command",
                "module": "main",
                "operation": "run",
                "expectation": "returned"
              }]
            }
            """
        );
        await File.WriteAllTextAsync(
            Path.Combine(root, "blokebot.plugin.json"),
            $$"""
            {
              "manifestVersion": 1,
              "id": "examples.arbitrary-source",
              "name": "Arbitrary author source",
              "description": "Exercises an author-selected directory.",
              "release": {
                "declaredVersion": "1.0.0",
                "tag": "examples-arbitrary-source-v1"
              },
              "compatibility": {
                "minimumApiVersion": 1,
                "maximumApiVersion": 1,
                "minimumBlokeBotVersion": "0.13.0",
                "maximumBlokeBotVersionExclusive": "0.14.0",
                "luaVersion": "lua54"
              },
              "entryModule": "main",
              "luaModules": [{ "id": "main", "path": "{{declaredPath}}" }],
              "assets": [],
              "payloads": [],
              "settings": [],
              "features": [],
              "hostModules": [],
              "migrations": [],
              "automationDefinitions": [],
              "automationTemplates": [],
              "generatedPages": [],
              "embeddedPages": []
            }
            """
        );
        await File.WriteAllTextAsync(
            Path.Combine(root, declaredPath.Replace('/', Path.DirectorySeparatorChar)),
            "return { run = function(input) return input end }\n"
        );
        if (declaredPath != "lua/main.lua")
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "lua", "main.lua"),
                "return { run = function(input) return input end }\n"
            );
        }
    }

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
