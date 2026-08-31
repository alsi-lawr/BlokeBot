using System.Diagnostics;
using System.Text;
using BlokeBot.Plugins.Contracts.Testing;
using Shouldly;

namespace BlokeBot.Plugins.Contracts.Tests;

public sealed class PluginAuthoringArtifactTests
{
    [Test]
    public async Task GeneratedAuthorArtifacts_MatchCanonicalContract()
    {
        var drift = await PluginAuthoringArtifacts.FindDriftAsync(
            Path.Combine(AppContext.BaseDirectory, "AuthoringArtifacts"),
            CancellationToken.None
        );

        drift.ShouldBeEmpty();
    }

    [Test]
    public void GeneratedReference_CoversEveryExportedCanonicalPublicTypeAndMember()
    {
        var contract = PluginAuthoringContract.Current;
        var reference = PluginAuthoringArtifacts
            .Generate(contract)
            .Single(artifact => artifact.RelativePath.EndsWith(".md", StringComparison.Ordinal))
            .Content;

        PluginAuthoringSemanticCoverage.FindOmissions(reference, contract).ShouldBeEmpty();
    }

    [Test]
    public void GeneratedReference_DetectsAnOmittedCanonicalPublicMember()
    {
        var contract = PluginAuthoringContract.Current;
        var reference = PluginAuthoringArtifacts
            .Generate(contract)
            .Single(artifact => artifact.RelativePath.EndsWith(".md", StringComparison.Ordinal))
            .Content;
        var member = contract
            .PublicContractTypes.SelectMany(type => PluginAuthoringSemanticCoverage.Members(type))
            .Last();
        var omitted = reference.Replace(
            $"`{member.CanonicalName}`",
            "`intentionally-omitted`",
            StringComparison.Ordinal
        );

        PluginAuthoringSemanticCoverage
            .FindOmissions(omitted, contract)
            .ShouldContain(new PluginAuthoringSemanticOmission(member.CanonicalName));
    }

    [Test]
    public void GeneratedSdk_CoversEveryCanonicalNamedHostFunctionAndShape()
    {
        var contract = PluginAuthoringContract.Current;
        var sdk = PluginAuthoringArtifacts
            .Generate(contract)
            .Single(artifact => artifact.RelativePath.EndsWith(".lua", StringComparison.Ordinal))
            .Content;

        foreach (var module in contract.HostModules)
        {
            foreach (var operation in module.Operations)
            {
                sdk.ShouldContain($"function {module.Id.Value}.{operation.LuaFunctionName}(");
                sdk.ShouldContain(PluginLuaType(operation.ResultShape));
                foreach (var parameter in operation.Parameters)
                {
                    sdk.ShouldContain(
                        $"---@param {parameter.Name} {PluginLuaType(parameter.Shape)}"
                    );
                }
            }
        }
    }

    [Test]
    public void GeneratedPluginTypes_DescribeManifestSettingsAutomationsAndHandlers()
    {
        var validation = PluginManifestToml.Validate(
            PluginContractFixtures.CompleteManifestToml(),
            PluginAuthoringContract.Current.Target(PluginRuntimeIdentifier.LinuxX64)
        );
        var manifest = validation
            .ShouldBeOfType<PluginManifestValidationOutcome.Accepted>()
            .Manifest.Manifest;
        var types = PluginProjectArtifacts
            .Generate(manifest)
            .Single(artifact =>
                artifact.RelativePath.EndsWith("plugin.lua", StringComparison.Ordinal)
            )
            .Content;

        types.ShouldContain("CommunityLinkQueueInstallationSettings");
        types.ShouldContain("service-token\"] BlokeBotProtectedValue");
        types.ShouldContain("CommunityLinkQueuePublishLinkInput");
        types.ShouldContain("publish_link\"] fun(input:");
        types.ShouldContain("CommunityLinkQueueEventsHandlers");
        types.ShouldContain("migrate_settings\"] fun(input:");
        types.ShouldContain("render_queue\"] fun(input:");
    }

    [Test]
    public async Task GeneratedSdk_ProvidesEveryCanonicalStructuredInputFieldToLuaLs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"blokebot-luals-test-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(root);
        try
        {
            var contract = PluginAuthoringContract.Current;
            var sdk = PluginProjectArtifacts
                .Scaffold(PluginContractFixtures.PluginId("examples.luals-inputs"))
                .Single(artifact =>
                    artifact.RelativePath.EndsWith("blokebot.lua", StringComparison.Ordinal)
                );
            await File.WriteAllTextAsync(Path.Combine(root, "blokebot.lua"), sdk.Content);
            await File.WriteAllTextAsync(Path.Combine(root, "probe.lua"), LuaLsProbe(contract));
            await File.WriteAllTextAsync(
                Path.Combine(root, ".luarc.json"),
                """
                {
                  "runtime": { "version": "Lua 5.4" },
                  "diagnostics": {
                    "severity": {
                      "assign-type-mismatch": "Error!",
                      "undefined-field": "Error!"
                    }
                  }
                }
                """
            );

            using var process = new Process
            {
                StartInfo =
                {
                    FileName =
                        Environment.GetEnvironmentVariable("BLOKEBOT_LUA_LANGUAGE_SERVER")
                        ?? "lua-language-server",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
            process.StartInfo.ArgumentList.Add($"--check={root}");
            process.StartInfo.ArgumentList.Add("--checklevel=Warning");
            process.StartInfo.ArgumentList.Add("--check_format=pretty");
            process.StartInfo.ArgumentList.Add($"--configpath={Path.Combine(root, ".luarc.json")}");
            _ = process.Start();
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var diagnostics = $"{await standardOutput}\n{await standardError}";

            process.ExitCode.ShouldBe(0, diagnostics);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string LuaLsProbe(PluginAuthoringContract contract)
    {
        var output = new StringBuilder();
        for (
            var schemaIndex = 0;
            schemaIndex < contract.InvocationInputSchemas.Length;
            schemaIndex++
        )
        {
            var schema = contract.InvocationInputSchemas[schemaIndex];
            _ = output.Append("---@param input ").AppendLine(schema.LuaTypeName);
            _ = output.Append("local function probe_").Append(schemaIndex).AppendLine("(input)");
            for (var fieldIndex = 0; fieldIndex < schema.Fields.Length; fieldIndex++)
            {
                var field = schema.Fields[fieldIndex];
                _ = output.Append("  ---@type ").AppendLine(field.Shape.LuaTypeName);
                _ = output
                    .Append("  local field_")
                    .Append(fieldIndex)
                    .Append(" = input[\"")
                    .Append(field.Name)
                    .AppendLine("\"]");
            }
            _ = output.AppendLine("  return input");
            _ = output.AppendLine("end");
            _ = output.AppendLine();
        }
        return output.ToString();
    }

    private static string PluginLuaType(PluginLuaValueShape shape) =>
        shape switch
        {
            PluginLuaValueShape.Nil => "nil",
            PluginLuaValueShape.Boolean => "boolean",
            PluginLuaValueShape.Number => "number",
            PluginLuaValueShape.String => "string",
            PluginLuaValueShape.ValueArray => "BlokeBotValue[]",
            PluginLuaValueShape.ValueMap => "table<string, BlokeBotValue>",
            PluginLuaValueShape.Context => "BlokeBotContext",
            PluginLuaValueShape.InstallationSettings => "BlokeBotInstallationSettings",
            PluginLuaValueShape.FeatureSettings => "BlokeBotFeatureSettings",
            PluginLuaValueShape.DiagnosticLevel => "BlokeBotDiagnosticLevel",
            PluginLuaValueShape.OverlayTargetId => "BlokeBotOverlayTargetId",
            PluginLuaValueShape.OverlayCueId => "BlokeBotOverlayCueId",
            PluginLuaValueShape.PointAmount => "BlokeBotPointAmount",
            PluginLuaValueShape.PointBalance => "BlokeBotPointBalance",
            PluginLuaValueShape.ScheduleInput => "BlokeBotScheduleInput",
            PluginLuaValueShape.ScheduleIntervalSeconds => "integer",
            PluginLuaValueShape.ScheduleId => "BlokeBotScheduleId",
            PluginLuaValueShape.SqlParameters => "BlokeBotSqlParameters",
            PluginLuaValueShape.SqlRows => "BlokeBotSqlRow[]",
            PluginLuaValueShape.HttpRequest => "BlokeBotHttpRequest",
            PluginLuaValueShape.HttpOutcome => "BlokeBotHttpOutcome",
        };
}
