using System.Globalization;
using System.Text.Json.Nodes;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferAutomationTests
{
    [Test]
    [Arguments("subflows/0", false)]
    [Arguments("flows/0", false)]
    [Arguments("hostReferences/0", false)]
    [Arguments("scenarios/0", false)]
    [Arguments("flows/0/nodes/0", false)]
    [Arguments("flows/0/edges/0", false)]
    [Arguments("flows/0/nodes/0/inputBindings/0", false)]
    [Arguments("subflows/0/graph/nodes/0", false)]
    [Arguments("subflows/0/interface/inputs/0", false)]
    [Arguments("subflows/0/graph/nodes/0/subflow/interface/outputs/0", false)]
    [Arguments("scenarios/0/inputs/0", false)]
    [Arguments("scenarios/0/effects/0", false)]
    [Arguments("subflows/0/graph", false)]
    [Arguments("subflows/0/interface", false)]
    [Arguments("scenarios/0/inputs", false)]
    [Arguments("subflows/0/interface/inputs", true)]
    [Arguments("subflows/0/graph/nodes/0/subflow/interface/outputs", true)]
    [Arguments("scenarios/0/sourceDefinitionId", true)]
    public void Format2_MalformedNestedWireValuesReturnLocatedInvalid(
        string relativePath,
        bool remove
    )
    {
        var codec = new ConfigurationDocumentCodec();
        var document = NestedDocument();
        document = document with
        {
            Sections = document.Sections with
            {
                Automations = document.Sections.Automations! with
                {
                    Scenarios =
                    [
                        new(
                            "scenario",
                            "flow",
                            "Generated",
                            "source",
                            AutomationDefinitionIds.StreamOnlineSource.Value,
                            1,
                            new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
                            0,
                            [],
                            []
                        ),
                    ],
                },
            },
        };
        _ = codec
            .Parse(codec.Serialize(document))
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>();
        var json = JsonNode.Parse(codec.Serialize(document))!;
        var target = json["sections"]!["automations"]!;
        var segments = relativePath.Split('/');
        var expectedLocation = "sections.automations";
        foreach (var segment in segments[..^1])
        {
            if (target is JsonArray array)
            {
                target = array[int.Parse(segment, CultureInfo.InvariantCulture)]!;
                expectedLocation += $"[{segment}]";
            }
            else
            {
                target = target[segment]!;
                expectedLocation += $".{segment}";
            }
        }
        var last = segments[^1];
        if (target is JsonArray collection)
        {
            var index = int.Parse(last, CultureInfo.InvariantCulture);
            if (index == collection.Count)
            {
                collection.Add(null);
            }
            else
            {
                collection[index] = null;
            }
            expectedLocation += $"[{last}]";
        }
        else if (remove)
        {
            _ = target.AsObject().Remove(last);
        }
        else
        {
            target[last] = null;
            expectedLocation += $".{last}";
        }
        var invalid = codec
            .Parse(json.ToJsonString())
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();
        invalid.Issue.Location.ShouldContain(expectedLocation);
    }

    [Test]
    public async Task Format2_NullRevisionRejectsDirectApplyIncludingSelectedActivation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "null-revision");
        var document = NestedDocument();
        document = document with
        {
            Sections = document.Sections with
            {
                Automations = document.Sections.Automations! with { Subflows = [null!] },
                ChannelToolEnablement = ChannelToolEnablementMapper.FromFlags(
                    HostFeatureFlags.Automations
                ),
            },
        };
        var selection = AutomationSelection(host) with
        {
            Sections =
            [
                .. AutomationSelection(host).Sections,
                new(ConfigurationSectionId.ChannelToolEnablement, ImportConflictStrategy.Merge, []),
            ],
            EnablementChanges = new HashSet<HostFeatureFlags> { HostFeatureFlags.Automations },
        };
        var outcome = await Coordinator(
                database,
                new RecordingLogger<ConfigurationTransferCoordinator>()
            )
            .ApplyAsync(
                Session(host),
                document,
                selection,
                new("destination-id", "destination"),
                CancellationToken.None
            );
        var invalid = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();
        invalid.Issues.ShouldHaveSingleItem().Location.ShouldBe("sections.automations.subflows[0]");
        await using var db = await database.CreateDbContextAsync();
        (await db.AutomationFlows.CountAsync()).ShouldBe(0);
        (await db.AutomationSubflowRevisions.CountAsync()).ShouldBe(0);
        (await db.AutomationScenarios.CountAsync()).ShouldBe(0);
        (await db.ConfigurationActivations.CountAsync()).ShouldBe(0);
        (await db.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
        (await db.Hosts.SingleAsync()).EnabledFeatures.ShouldBe(HostFeatureFlags.None);
    }
}
