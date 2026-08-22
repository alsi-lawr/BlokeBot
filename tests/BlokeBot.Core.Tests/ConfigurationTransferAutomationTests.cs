using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationTransferAutomationTests
{
    private const string _malformedCommandReference = "raw-host-reference-secret";

    [Test]
    public async Task KnownCoreInvalidConfiguration_PreviewsAsRepairableAndRoundTripsWithoutBlocking()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "closed-shape");
        var transfer = AutomationTransfer(database);
        var previewService = new ConfigurationImportPreviewService(
            database,
            UnavailableOverlayConfigurationTransferAdapter.Instance,
            transfer.Adapter
        );
        var cases = new[]
        {
            (
                Name: "Unknown member",
                Configuration: JsonSerializer.SerializeToElement(
                    new Dictionary<string, object> { ["message"] = "Hello", ["unknown"] = "extra" }
                ),
                Message: "Configuration member 'unknown' is not supported by automation definition 'send-chat'."
            ),
            (
                Name: "Wrong known type",
                Configuration: JsonSerializer.SerializeToElement(
                    new Dictionary<string, int> { ["message"] = 42 }
                ),
                Message: "Enter a chat message."
            ),
        };

        foreach (var item in cases)
        {
            var document = OrdinaryAutomationDocument(item.Name, item.Configuration);
            var selection = AutomationSelection(hostId);
            var preview = await previewService.PreviewAsync(
                document,
                selection,
                CancellationToken.None
            );
            preview
                .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
                .Preview.Sections.Single()
                .Issues.ShouldContain(issue => issue.Message == item.Message && !issue.BlocksApply);

            var applied = await Coordinator(
                    database,
                    new RecordingLogger<ConfigurationTransferCoordinator>()
                )
                .ApplyAsync(
                    Session(hostId),
                    document,
                    selection,
                    new("destination-id", "destination"),
                    CancellationToken.None
                );
            _ = applied.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        }

        _ = (
            await ExportAutomationsAsync(database, transfer, hostId)
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.AutomationFlows.CountAsync()).ShouldBe(2);
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(2);
    }

    [Test]
    public async Task KnownCoreInvalidGraphAndRepairableBinding_TransferWithoutBlocking()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "invalid-graph");
        var transfer = AutomationTransfer(database);
        var document = AutomationDocument(
            "Cyclic flow",
            [
                Node("source", AutomationDefinitionIds.StreamOnlineSource.Value, EmptyObject()),
                Node(
                    "action",
                    AutomationDefinitionIds.SendChatAction.Value,
                    JsonSerializer.SerializeToElement(
                        new Dictionary<string, string> { ["message"] = "Hello" }
                    ),
                    [new("repair-me", AutomationInputBindingMode.Fixed)]
                ),
            ],
            [
                new("edge-1", AutomationEdgeKind.Flow, "source", "flow", "action", "flow"),
                new("edge-2", AutomationEdgeKind.Flow, "action", "flow", "source", "flow"),
            ]
        );
        var preview = await new ConfigurationImportPreviewService(
            database,
            UnavailableOverlayConfigurationTransferAdapter.Instance,
            transfer.Adapter
        ).PreviewAsync(document, AutomationSelection(hostId), CancellationToken.None);
        var section = preview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single();
        section.Issues.ShouldNotBeEmpty();
        section.Issues.ShouldAllBe(issue => !issue.BlocksApply);

        _ = (
            await Coordinator(database, new RecordingLogger<ConfigurationTransferCoordinator>())
                .ApplyAsync(
                    Session(hostId),
                    document,
                    AutomationSelection(hostId),
                    new("destination-id", "destination"),
                    CancellationToken.None
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        _ = (
            await ExportAutomationsAsync(database, transfer, hostId)
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();

        await using var verify = await database.CreateDbContextAsync();
        var flow = await verify
            .AutomationFlows.Include(value => value.Nodes)
            .Include(value => value.Edges)
            .SingleAsync();
        flow.Edges.Count.ShouldBe(2);
        flow.Nodes.Single(value =>
                value.DefinitionId == AutomationDefinitionIds.SendChatAction.Value
            )
            .InputBindingsJson.ShouldContain("repair-me");
    }

    [Test]
    public async Task DynamicTransform_WithOptionalStreamFieldsImportsAndExports()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "dynamic-shape");
        var transfer = AutomationTransfer(database);
        var document = DynamicTransformDocument();
        var selection = AutomationSelection(hostId);
        var preview = await new ConfigurationImportPreviewService(
            database,
            UnavailableOverlayConfigurationTransferAdapter.Instance,
            transfer.Adapter
        ).PreviewAsync(document, selection, CancellationToken.None);
        preview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single()
            .Issues.ShouldAllBe(issue => !issue.BlocksApply);

        _ = (
            await Coordinator(database, new RecordingLogger<ConfigurationTransferCoordinator>())
                .ApplyAsync(
                    Session(hostId),
                    document,
                    selection,
                    new("destination-id", "destination"),
                    CancellationToken.None
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var exported = await new ConfigurationDocumentExporter(
            database,
            new(),
            transfer.Catalog,
            transfer.FlowService,
            NullLogger<ConfigurationDocumentExporter>.Instance,
            TimeProvider.System
        ).ExportAsync(
            hostId,
            new(
                new HashSet<ConfigurationSectionId> { ConfigurationSectionId.Automations },
                new(false, false, false)
            ),
            CancellationToken.None
        );
        _ = exported.ShouldBeOfType<ConfigurationExportOutcome.Success>();

        await using var verify = await database.CreateDbContextAsync();
        var flow = await verify
            .AutomationFlows.Include(value => value.Nodes)
            .SingleAsync(value => value.Name == "Dynamic transform");
        flow.Nodes.ShouldContain(value =>
            value.DefinitionId == AutomationDefinitionIds.CelTransform.Value
        );
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task ConnectedIdentityFallback_CelTransformRoundTripsAsValidConfiguration()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "connected-identity");
        var transfer = AutomationTransfer(database);
        const string LocalLogin = "local-raid-actor";
        const string LocalDisplayName = "Local Raid Actor";
        var configuration = TransformConfiguration(
            [
                TransformInput(
                    "actor",
                    "actor",
                    "Actor",
                    "actor-binding",
                    AutomationPortValueType.Actor,
                    new Dictionary<string, object>
                    {
                        ["login"] = LocalLogin,
                        ["display-name"] = LocalDisplayName,
                    }
                ),
                TransformInput(
                    "number",
                    "number",
                    "Number",
                    "number-binding",
                    AutomationPortValueType.Number,
                    0
                ),
                TransformInput(
                    "threshold",
                    "threshold",
                    "Threshold",
                    "threshold-binding",
                    AutomationPortValueType.Number,
                    0
                ),
                TransformInput(
                    "arguments",
                    "arguments_input",
                    "Arguments",
                    "arguments-binding",
                    AutomationPortValueType.Arguments,
                    Array.Empty<string>()
                ),
            ],
            [
                TransformOutput(
                    "message",
                    "Message",
                    "${actor.display_name} rolled ${format_number(number)}"
                ),
                TransformOutput(
                    "is-high",
                    "Is high",
                    "number >= 75",
                    AutomationPortValueType.Boolean
                ),
                TransformOutput(
                    "rolled",
                    "Rolled",
                    "number",
                    AutomationPortValueType.Number,
                    AutomationPortNullability.Nullable
                ),
            ]
        );
        var bindings = ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>
            .Empty.Add(new("actor-binding"), new(AutomationInputBindingMode.Connected, null))
            .Add(new("number-binding"), new(AutomationInputBindingMode.Connected, null))
            .Add(new("threshold-binding"), new(AutomationInputBindingMode.Connected, null))
            .Add(
                new("arguments-binding"),
                new(
                    AutomationInputBindingMode.Expression,
                    new(AutomationExpressionLanguage.CurrentVersion, "arguments")
                )
            );
        _ = await SeedPersistedFlowAsync(
            database,
            hostId,
            "Welcome a qualifying raid",
            AutomationDefinitionIds.CelTransform.Value,
            configuration,
            AutomationRuntimeSerialization.SerializeInputBindings(bindings)
        );

        var exported = (
            await ExportAutomationsAsync(database, transfer, hostId)
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        var exportedNode = exported.Document.Sections.Automations!.Flows.Single().Nodes.Single();
        exportedNode.Configuration.GetRawText().ShouldNotContain(LocalLogin);
        exportedNode.Configuration.GetRawText().ShouldNotContain(LocalDisplayName);
        _ = transfer
            .Catalog.ValidatePersistedDefinition(
                new(
                    exportedNode.DefinitionId,
                    exportedNode.DefinitionSchemaVersion,
                    exportedNode.Configuration
                )
            )
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>();

        _ = (
            await Coordinator(database, new RecordingLogger<ConfigurationTransferCoordinator>())
                .ApplyAsync(
                    Session(hostId),
                    exported.Document,
                    AutomationSelection(hostId),
                    new("destination-id", "destination"),
                    CancellationToken.None
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();

        await using var verify = await database.CreateDbContextAsync();
        var importedNode = await verify.AutomationFlowNodes.SingleAsync();
        using var importedConfiguration = JsonDocument.Parse(importedNode.ConfigurationJson);
        _ = transfer
            .Catalog.ValidatePersistedDefinition(
                new(
                    importedNode.DefinitionId,
                    importedNode.DefinitionSchemaVersion,
                    importedConfiguration.RootElement.Clone()
                )
            )
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>();
        AutomationCelTransformDocumentSerializer
            .TryDeserialize<AutomationCelTransformDocument>(
                importedConfiguration.RootElement,
                out var importedTransform
            )
            .ShouldBeTrue();
        importedTransform!
            .Inputs.Single(input => input.PortId == "actor")
            .FixedValue.GetProperty("login")
            .GetString()
            .ShouldBeEmpty();
        importedTransform
            .Outputs.Select(static output => (output.PortId, output.Source))
            .ShouldBe([
                ("message", "${actor.display_name} rolled ${format_number(number)}"),
                ("is-high", "number >= 75"),
                ("rolled", "number"),
            ]);
        var importedBindings = AutomationRuntimeSerialization
            .RestoreInputBindings(importedNode.InputBindingsJson)
            .ShouldBeOfType<AutomationInputBindingsRestoreOutcome.Available>();
        importedBindings
            .Bindings[new("actor-binding")]
            .Mode.ShouldBe(AutomationInputBindingMode.Connected);
        importedBindings
            .Bindings[new("arguments-binding")]
            .ShouldBe(bindings[new("arguments-binding")]);
    }

    [Test]
    public async Task ConnectedIdentityPlaceholder_ImportsAsValidNeutralFallback()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "connected-placeholder");
        var transfer = AutomationTransfer(database);
        var configuration = TransformConfiguration(
            [
                TransformInput(
                    "actor",
                    "actor",
                    "Actor",
                    "actor-binding",
                    AutomationPortValueType.Actor,
                    AutomationTransferPlaceholder.Create(AutomationTransferPlaceholder.Identity)
                ),
            ],
            [TransformOutput("message", "Message", "'ready'")]
        );
        var document = AutomationDocument(
            "Affected connected identity",
            [
                Node(
                    "transform",
                    AutomationDefinitionIds.CelTransform.Value,
                    configuration,
                    [new("actor-binding", AutomationInputBindingMode.Connected)]
                ),
            ],
            []
        );

        _ = (
            await Coordinator(database, new RecordingLogger<ConfigurationTransferCoordinator>())
                .ApplyAsync(
                    Session(hostId),
                    document,
                    AutomationSelection(hostId),
                    new("destination-id", "destination"),
                    CancellationToken.None
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();

        await using var verify = await database.CreateDbContextAsync();
        var imported = await verify.AutomationFlowNodes.SingleAsync();
        imported.ConfigurationJson.ShouldNotContain(AutomationTransferPlaceholder.Identity);
        using var importedConfiguration = JsonDocument.Parse(imported.ConfigurationJson);
        _ = transfer
            .Catalog.ValidatePersistedDefinition(
                new(
                    imported.DefinitionId,
                    imported.DefinitionSchemaVersion,
                    importedConfiguration.RootElement.Clone()
                )
            )
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>();
    }

    [Test]
    [Arguments(AutomationPortValueType.Actor)]
    [Arguments(AutomationPortValueType.Channel)]
    public async Task NullableFixedIdentityNull_CelTransformRoundTripsAsValidConfiguration(
        AutomationPortValueType valueType
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, $"nullable-{valueType}");
        var transfer = AutomationTransfer(database);
        var configuration = TransformConfiguration(
            [
                TransformInput(
                    "identity",
                    "identityValue",
                    "Identity",
                    "identity-binding",
                    valueType,
                    JsonSerializer.SerializeToElement<object?>(null),
                    AutomationPortNullability.Nullable
                ),
            ],
            [TransformOutput("text-output", "Text", "'ready'")]
        );
        _ = await SeedPersistedFlowAsync(
            database,
            hostId,
            "Nullable identity",
            AutomationDefinitionIds.CelTransform.Value,
            configuration,
            AutomationRuntimeSerialization.SerializeInputBindings(
                ImmutableDictionary<
                    AutomationConfigurationFieldId,
                    AutomationInputBinding
                >.Empty.Add(new("identity-binding"), new(AutomationInputBindingMode.Fixed, null))
            )
        );

        var exported = (
            await ExportAutomationsAsync(database, transfer, hostId)
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        var exportedNode = exported.Document.Sections.Automations!.Flows.Single().Nodes.Single();
        exportedNode.Configuration.GetRawText().ShouldNotContain("identity-redacted");
        _ = transfer
            .Catalog.ValidatePersistedDefinition(
                new(
                    exportedNode.DefinitionId,
                    exportedNode.DefinitionSchemaVersion,
                    exportedNode.Configuration
                )
            )
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>();

        _ = (
            await Coordinator(database, new RecordingLogger<ConfigurationTransferCoordinator>())
                .ApplyAsync(
                    Session(hostId),
                    exported.Document,
                    AutomationSelection(hostId),
                    new("destination-id", "destination"),
                    CancellationToken.None
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();

        await using var verify = await database.CreateDbContextAsync();
        var importedNode = await verify.AutomationFlowNodes.SingleAsync();
        using var importedConfiguration = JsonDocument.Parse(importedNode.ConfigurationJson);
        _ = transfer
            .Catalog.ValidatePersistedDefinition(
                new(
                    importedNode.DefinitionId,
                    importedNode.DefinitionSchemaVersion,
                    importedConfiguration.RootElement.Clone()
                )
            )
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>();
        AutomationCelTransformDocumentSerializer
            .TryDeserialize<AutomationCelTransformDocument>(
                importedConfiguration.RootElement,
                out var importedTransform
            )
            .ShouldBeTrue();
        importedTransform!.Inputs.Single().FixedValue.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    [Arguments(AutomationInputBindingMode.Connected)]
    [Arguments(AutomationInputBindingMode.Expression)]
    public async Task NonFixedNonNullableIdentityNull_ExportsWithValidNeutralFallback(
        AutomationInputBindingMode mode
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, $"non-fixed-null-{mode}");
        var transfer = AutomationTransfer(database);
        var configuration = TransformConfiguration(
            [
                TransformInput(
                    "actor",
                    "actorValue",
                    "Actor",
                    "actor-binding",
                    AutomationPortValueType.Actor,
                    JsonSerializer.SerializeToElement<object?>(null)
                ),
            ],
            [TransformOutput("text-output", "Text", "'ready'")]
        );
        var expression =
            mode == AutomationInputBindingMode.Expression
                ? new AutomationExpressionSource(
                    AutomationExpressionLanguage.CurrentVersion,
                    "actor"
                )
                : null;
        _ = await SeedPersistedFlowAsync(
            database,
            hostId,
            "Non-fixed null identity",
            AutomationDefinitionIds.CelTransform.Value,
            configuration,
            AutomationRuntimeSerialization.SerializeInputBindings(
                ImmutableDictionary<
                    AutomationConfigurationFieldId,
                    AutomationInputBinding
                >.Empty.Add(new("actor-binding"), new(mode, expression))
            )
        );

        var exported = (
            await ExportAutomationsAsync(database, transfer, hostId)
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        var exportedNode = exported.Document.Sections.Automations!.Flows.Single().Nodes.Single();
        _ = transfer
            .Catalog.ValidatePersistedDefinition(
                new(
                    exportedNode.DefinitionId,
                    exportedNode.DefinitionSchemaVersion,
                    exportedNode.Configuration
                )
            )
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>();
        AutomationCelTransformDocumentSerializer
            .TryDeserialize<AutomationCelTransformDocument>(
                exportedNode.Configuration,
                out var exportedTransform
            )
            .ShouldBeTrue();
        var fallback = exportedTransform!.Inputs.Single().FixedValue;
        fallback.GetProperty("login").GetString().ShouldBeEmpty();
        fallback.GetProperty("display-name").GetString().ShouldBeEmpty();
    }

    [Test]
    public async Task Format1FixedIdentityCel_RedactsRoundTripsLogsAndPreservesInvalidHistory()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "identity-shape");
        var transfer = AutomationTransfer(database);
        const string Login = "local-identity";
        const string DisplayName = "Local Identity";
        var configuration = TransformConfiguration(
            [
                IdentityTransformInput("actor", AutomationPortValueType.Actor, Login, DisplayName),
                IdentityTransformInput(
                    "channel",
                    AutomationPortValueType.Channel,
                    Login,
                    DisplayName
                ),
            ],
            [TransformOutput("text-output", "Text", "'ready'")]
        );
        var (flowId, nodeId) = await SeedPersistedFlowAsync(
            database,
            hostId,
            "CEL transform",
            AutomationDefinitionIds.CelTransform.Value,
            configuration,
            AutomationRuntimeSerialization.SerializeInputBindings(
                ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>
                    .Empty.Add(new("actor-binding"), new(AutomationInputBindingMode.Fixed, null))
                    .Add(new("channel-binding"), new(AutomationInputBindingMode.Fixed, null))
            ),
            enabled: true
        );
        const string FrozenDefinition = "{\"frozen\":true}";
        await SeedRunAsync(database, hostId, flowId, nodeId, FrozenDefinition);

        var exportLogger = new RecordingLogger<ConfigurationDocumentExporter>();
        var exported = (
            await new ConfigurationDocumentExporter(
                database,
                new(),
                transfer.Catalog,
                transfer.FlowService,
                exportLogger,
                TimeProvider.System
            ).ExportAsync(
                hostId,
                new(
                    new HashSet<ConfigurationSectionId> { ConfigurationSectionId.Automations },
                    new(false, false, false)
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        var json = System.Text.Encoding.UTF8.GetString(exported.Json);
        json.ShouldContain("identity-redacted");
        json.ShouldNotContain(Login);
        json.ShouldNotContain(DisplayName);
        json.ShouldNotContain(flowId.ToString("D"));
        json.ShouldNotContain(nodeId.ToString("D"));
        AssertSafeDiagnosticLog(
            exportLogger.Entries,
            AutomationFormat1ConfigurationProjector.IdentityRedactedReason,
            "flow-0001",
            "node-0001",
            Login,
            DisplayName,
            flowId.ToString("D"),
            nodeId.ToString("D")
        );
        exportLogger.Entries.ShouldContain(entry =>
            Equals(entry.Properties.GetValueOrDefault("Reason"), "source-count")
        );

        var importLogger = new RecordingLogger<ConfigurationTransferCoordinator>();
        _ = (
            await Coordinator(database, importLogger)
                .ApplyAsync(
                    Session(hostId),
                    exported.Document,
                    AutomationSelection(hostId),
                    new("destination-id", "destination"),
                    CancellationToken.None
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        AssertSafeDiagnosticLog(
            importLogger.Entries,
            AutomationFormat1ConfigurationProjector.IdentityPlaceholderReason,
            "flow-0001",
            "node-0001",
            Login,
            DisplayName,
            flowId.ToString("D"),
            nodeId.ToString("D")
        );
        importLogger.Entries.ShouldContain(entry =>
            Equals(entry.Properties.GetValueOrDefault("Reason"), "configuration-invalid")
        );

        await using var verify = await database.CreateDbContextAsync();
        var imported = await verify
            .AutomationFlows.Include(value => value.Nodes)
            .SingleAsync(value => value.Id == flowId);
        var importedNode = imported.Nodes.ShouldHaveSingleItem();
        importedNode.ConfigurationJson.ShouldContain("identity-redacted");
        importedNode.ConfigurationJson.ShouldNotContain(Login);
        (await verify.AutomationFlowRuns.SingleAsync()).DefinitionJson.ShouldBe(FrozenDefinition);
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(1);
        using var persisted = JsonDocument.Parse(importedNode.ConfigurationJson);
        _ = transfer
            .Catalog.ValidatePersistedDefinition(
                new(
                    importedNode.DefinitionId,
                    importedNode.DefinitionSchemaVersion,
                    persisted.RootElement.Clone()
                )
            )
            .ShouldBeOfType<AutomationConfigurationCheck.Invalid>();
        var restored = AutomationFlowService
            .RestoreDraft(imported)
            .ShouldBeOfType<AutomationFlowDraftRestoreOutcome.Available>();
        var validation = await transfer.FlowService.ValidateConfigurationTransferAsync(
            restored.Draft,
            CancellationToken.None
        );
        validation.Errors.ShouldContain(error => error.Code == "configuration-invalid");
    }

    [Test]
    public async Task NestedIdentityInInvalidFixedValue_RedactsAcrossExportAndImport()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "nested-invalid-identity");
        var transfer = AutomationTransfer(database);
        const string Login = "nested-viewer";
        const string DisplayName = "Nested Viewer";
        const string Retained = "unrelated retained value";
        var configuration = DynamicFixedValueConfiguration(
            AutomationPortValueType.Text,
            new Dictionary<string, object>
            {
                ["items"] = new object[]
                {
                    new Dictionary<string, string>
                    {
                        ["LoGiN"] = Login,
                        ["DiSpLaY-NaMe"] = DisplayName,
                    },
                    new Dictionary<string, string> { ["keep"] = Retained },
                },
            }
        );
        _ = await SeedPersistedFlowAsync(
            database,
            hostId,
            "Nested invalid identity",
            AutomationDefinitionIds.CelTransform.Value,
            configuration,
            AutomationRuntimeSerialization.SerializeInputBindings(
                ImmutableDictionary<
                    AutomationConfigurationFieldId,
                    AutomationInputBinding
                >.Empty.Add(new("value-binding"), new(AutomationInputBindingMode.Fixed, null))
            )
        );

        var exportLogger = new RecordingLogger<ConfigurationDocumentExporter>();
        var exported = (
            await new ConfigurationDocumentExporter(
                database,
                new(),
                transfer.Catalog,
                transfer.FlowService,
                exportLogger,
                TimeProvider.System
            ).ExportAsync(
                hostId,
                new(
                    new HashSet<ConfigurationSectionId> { ConfigurationSectionId.Automations },
                    new(false, false, false)
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        var json = System.Text.Encoding.UTF8.GetString(exported.Json);
        json.ShouldNotContain(Login);
        json.ShouldNotContain(DisplayName);
        json.ShouldContain(Retained);
        AssertSafeDiagnosticLog(
            exportLogger.Entries,
            AutomationFormat1ConfigurationProjector.IdentityRedactedReason,
            "flow-0001",
            "node-0001",
            Login,
            DisplayName
        );

        var importLogger = new RecordingLogger<ConfigurationTransferCoordinator>();
        _ = (
            await Coordinator(database, importLogger)
                .ApplyAsync(
                    Session(hostId),
                    exported.Document,
                    AutomationSelection(hostId),
                    new("destination-id", "destination"),
                    CancellationToken.None
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        AssertSafeDiagnosticLog(
            importLogger.Entries,
            AutomationFormat1ConfigurationProjector.IdentityPlaceholderReason,
            "flow-0001",
            "node-0001",
            Login,
            DisplayName
        );

        await using var verify = await database.CreateDbContextAsync();
        var persisted = await verify.AutomationFlowNodes.SingleAsync();
        persisted.ConfigurationJson.ShouldNotContain(Login);
        persisted.ConfigurationJson.ShouldNotContain(DisplayName);
        persisted.ConfigurationJson.ShouldContain(Retained);
    }

    [Test]
    [Arguments(AutomationPortValueType.Actor)]
    [Arguments(AutomationPortValueType.Text)]
    public async Task Format1IdentityShapedImport_RedactsRawDocumentBeforePersistence(
        AutomationPortValueType valueType
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "identity-import");
        const string Login = "foreign-viewer";
        const string DisplayName = "Foreign Viewer";
        object fixedValue =
            valueType == AutomationPortValueType.Actor
                ? new Dictionary<string, object>
                {
                    ["login"] = Login,
                    ["display-name"] = DisplayName,
                }
                : new Dictionary<string, object>
                {
                    ["nested"] = new Dictionary<string, object>
                    {
                        ["login"] = Login,
                        ["display-name"] = DisplayName,
                    },
                };
        var document = DynamicFixedValueDocument("Imported identity", valueType, fixedValue);
        var importedFlowId = Guid.NewGuid().ToString("D");
        var importedSourceNodeId = Guid.NewGuid().ToString("D");
        var importedTransformNodeId = Guid.NewGuid().ToString("D");
        var importedAutomation = document.Sections.Automations!;
        var importedFlow = importedAutomation.Flows.Single();
        document = document with
        {
            Sections = document.Sections with
            {
                Automations = importedAutomation with
                {
                    Flows =
                    [
                        importedFlow with
                        {
                            Id = importedFlowId,
                            Nodes =
                            [
                                importedFlow.Nodes[0] with
                                {
                                    Id = importedSourceNodeId,
                                },
                                importedFlow.Nodes[1] with
                                {
                                    Id = importedTransformNodeId,
                                },
                            ],
                        },
                    ],
                },
            },
        };
        var logger = new RecordingLogger<ConfigurationTransferCoordinator>();

        _ = (
            await Coordinator(database, logger)
                .ApplyAsync(
                    Session(hostId),
                    document,
                    AutomationSelection(hostId),
                    new("destination-id", "destination"),
                    CancellationToken.None
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        AssertSafeDiagnosticLog(
            logger.Entries,
            AutomationFormat1ConfigurationProjector.IdentityRedactedReason,
            "flow-0001",
            "node-0002",
            Login,
            DisplayName,
            importedFlowId,
            importedSourceNodeId,
            importedTransformNodeId
        );

        await using var verify = await database.CreateDbContextAsync();
        var persisted = await verify.AutomationFlowNodes.SingleAsync(value =>
            value.DefinitionId == AutomationDefinitionIds.CelTransform.Value
        );
        persisted.ConfigurationJson.ShouldContain("identity-redacted");
        persisted.ConfigurationJson.ShouldNotContain(Login);
        persisted.ConfigurationJson.ShouldNotContain(DisplayName);
    }

    [Test]
    public async Task MalformedNestedCelIdentities_RejectWithoutLeakingValues()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "nested-redaction");
        var transfer = AutomationTransfer(database);
        const string Message = "CEL Transform configuration does not match the Format 1 schema.";
        const string Login = "deep-viewer-login";
        const string DisplayName = "Deep Viewer Name";
        const string OrphanLogin = "orphan-viewer-login";
        var configuration = MalformedNestedIdentityConfiguration(Login, DisplayName, OrphanLogin);
        _ = await SeedPersistedFlowAsync(
            database,
            hostId,
            "Nested export",
            AutomationDefinitionIds.CelTransform.Value,
            configuration,
            "{}"
        );

        var exportLogger = new RecordingLogger<ConfigurationDocumentExporter>();
        var exported = await new ConfigurationDocumentExporter(
            database,
            new(),
            transfer.Catalog,
            transfer.FlowService,
            exportLogger,
            TimeProvider.System
        ).ExportAsync(
            hostId,
            new(
                new HashSet<ConfigurationSectionId> { ConfigurationSectionId.Automations },
                new(false, false, false)
            ),
            CancellationToken.None
        );
        exported
            .ShouldBeOfType<ConfigurationExportOutcome.Unsupported>()
            .Message.ShouldContain(Message);
        AssertLogsExclude(exportLogger.Entries, Login, DisplayName, OrphanLogin);

        var importDocument = AutomationDocument(
            "Nested import",
            [Node("transform", AutomationDefinitionIds.CelTransform.Value, configuration)],
            []
        );
        var preview = await new ConfigurationImportPreviewService(
            database,
            UnavailableOverlayConfigurationTransferAdapter.Instance,
            transfer.Adapter
        ).PreviewAsync(importDocument, AutomationSelection(hostId), CancellationToken.None);
        preview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single()
            .Issues.ShouldContain(issue => issue.BlocksApply && issue.Message == Message);

        var importLogger = new RecordingLogger<ConfigurationTransferCoordinator>();
        var applied = await Coordinator(database, importLogger)
            .ApplyAsync(
                Session(hostId),
                importDocument,
                AutomationSelection(hostId),
                new("destination-id", "destination"),
                CancellationToken.None
            );
        applied
            .ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>()
            .Issues.ShouldContain(issue => issue.Message == Message);
        AssertLogsExclude(importLogger.Entries, Login, DisplayName, OrphanLogin);

        await using var verify = await database.CreateDbContextAsync();
        (await verify.AutomationFlows.CountAsync()).ShouldBe(1);
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task NonObjectParameterlessConfiguration_RejectsPreviewApplyAndExport()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "non-object");
        var transfer = AutomationTransfer(database);
        var document = AutomationDocument(
            "Non-object",
            [
                Node(
                    "source",
                    AutomationDefinitionIds.StreamOnlineSource.Value,
                    JsonSerializer.SerializeToElement(new[] { "not", "an", "object" })
                ),
            ],
            []
        );
        var selection = AutomationSelection(hostId);
        var preview = await new ConfigurationImportPreviewService(
            database,
            UnavailableOverlayConfigurationTransferAdapter.Instance,
            transfer.Adapter
        ).PreviewAsync(document, selection, CancellationToken.None);
        preview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single()
            .Issues.ShouldHaveSingleItem()
            .Message.ShouldBe("Automation configuration must be a JSON object.");
        _ = (
            await Coordinator(database, new RecordingLogger<ConfigurationTransferCoordinator>())
                .ApplyAsync(
                    Session(hostId),
                    document,
                    selection,
                    new("destination-id", "destination"),
                    CancellationToken.None
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();

        _ = await SeedPersistedFlowAsync(
            database,
            hostId,
            "Malformed source",
            AutomationDefinitionIds.StreamOnlineSource.Value,
            JsonSerializer.SerializeToElement(new[] { 1 }),
            "{}"
        );
        var exported = await ExportAutomationsAsync(database, transfer, hostId);
        exported
            .ShouldBeOfType<ConfigurationExportOutcome.Unsupported>()
            .Message.ShouldContain("Automation configuration must be a JSON object.");

        await using var verify = await database.CreateDbContextAsync();
        (await verify.AutomationFlows.CountAsync()).ShouldBe(1);
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task DynamicCelCollections_Accept1000AndReject1001BeforePersistence()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "dynamic-bounds");
        var transfer = AutomationTransfer(database);
        var previewService = new ConfigurationImportPreviewService(
            database,
            UnavailableOverlayConfigurationTransferAdapter.Instance,
            transfer.Adapter
        );
        foreach (var kind in Enum.GetValues<DynamicCollectionKind>())
        {
            var accepted = await previewService.PreviewAsync(
                DynamicCollectionDocument(kind, 1000),
                AutomationSelection(hostId),
                CancellationToken.None
            );
            accepted
                .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
                .Preview.Sections.Single()
                .Issues.ShouldAllBe(issue => !issue.BlocksApply);

            var rejectedDocument = DynamicCollectionDocument(kind, 1001);
            var expected = DynamicCollectionLimitMessage(kind);
            var rejected = await previewService.PreviewAsync(
                rejectedDocument,
                AutomationSelection(hostId),
                CancellationToken.None
            );
            rejected
                .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
                .Preview.Sections.Single()
                .Issues.ShouldContain(issue => issue.Message == expected && issue.BlocksApply);
            var applied = await Coordinator(
                    database,
                    new RecordingLogger<ConfigurationTransferCoordinator>()
                )
                .ApplyAsync(
                    Session(hostId),
                    rejectedDocument,
                    AutomationSelection(hostId),
                    new("destination-id", "destination"),
                    CancellationToken.None
                );
            applied
                .ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>()
                .Issues.ShouldContain(issue => issue.Message == expected);

            var rejectedTransform = rejectedDocument
                .Sections.Automations!.Flows.Single()
                .Nodes.Single(node =>
                    node.DefinitionId == AutomationDefinitionIds.CelTransform.Value
                );
            _ = await SeedPersistedFlowAsync(
                database,
                hostId,
                $"Persisted {kind}",
                AutomationDefinitionIds.CelTransform.Value,
                rejectedTransform.Configuration,
                "{}"
            );
            var exported = await ExportAutomationsAsync(database, transfer, hostId);
            exported
                .ShouldBeOfType<ConfigurationExportOutcome.Unsupported>()
                .Message.ShouldContain(expected);
            await using var cleanup = await database.CreateDbContextAsync();
            _ = await cleanup.AutomationFlows.ExecuteDeleteAsync();
        }

        await using var verify = await database.CreateDbContextAsync();
        (await verify.AutomationFlows.CountAsync()).ShouldBe(0);
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task MalformedAutomationReferencePayload_ImportsAsInvalidPlaceholderWithoutRawIds()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var (hostId, _, _, documentId) = await SeedAsync(database);
        var document = Document(documentId);
        var automations = document.Sections.Automations!;
        var flow = automations.Flows.Single();
        var source = flow.Nodes.Single(value =>
            value.DefinitionId == AutomationDefinitionIds.CustomCommandSource.Value
        );
        source = source with
        {
            Configuration = JsonSerializer.SerializeToElement(
                new Dictionary<string, int> { ["custom-command-id"] = 42 }
            ),
        };
        document = document with
        {
            Sections = document.Sections with
            {
                Automations = automations with
                {
                    Flows =
                    [
                        flow with
                        {
                            Nodes = flow
                                .Nodes.Select(value => value.Id == source.Id ? source : value)
                                .ToArray(),
                        },
                    ],
                },
            },
        };

        var outcome = await Coordinator(
                database,
                new RecordingLogger<ConfigurationTransferCoordinator>()
            )
            .ApplyAsync(
                Session(hostId),
                document,
                new(
                    hostId,
                    [new(ConfigurationSectionId.Automations, ImportConflictStrategy.Merge, [])],
                    new HashSet<HostFeatureFlags>()
                ),
                new("destination-id", "destination"),
                CancellationToken.None
            );

        _ = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(1);
        var persisted = (
            await verify.AutomationFlows.Include(value => value.Nodes).SingleAsync()
        ).Nodes.Single(value =>
            value.DefinitionId == AutomationDefinitionIds.CustomCommandSource.Value
        );
        persisted.ConfigurationJson.ShouldContain("custom-command-reference-unmapped");
        persisted.ConfigurationJson.ShouldNotContain("42");
    }

    [Test]
    public async Task UnmappedPersistedHostReferences_ExportAsPlaceholdersWithoutRawIds()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "unmapped-references");
        var transfer = AutomationTransfer(database);
        const int CommandId = 7654321;
        var targetId = Guid.NewGuid();
        var cueId = Guid.NewGuid();
        const string RewardId = "provider-reward-secret";
        await using (var db = await database.CreateDbContextAsync())
        {
            var flowId = Guid.NewGuid();
            _ = db.AutomationFlows.Add(
                new()
                {
                    Id = flowId,
                    HostId = hostId,
                    Name = "Unmapped references",
                    SchemaVersion = AutomationFlowSchema.CurrentVersion,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Nodes =
                    [
                        PersistedNode(
                            flowId,
                            AutomationDefinitionIds.CustomCommandSource.Value,
                            JsonSerializer.SerializeToElement(
                                new Dictionary<string, int> { ["custom-command-id"] = CommandId }
                            )
                        ),
                        PersistedNode(
                            flowId,
                            AutomationDefinitionIds.PlayOverlayCueAction.Value,
                            JsonSerializer.SerializeToElement(
                                new Dictionary<string, Guid>
                                {
                                    ["target-id"] = targetId,
                                    ["cue-id"] = cueId,
                                }
                            )
                        ),
                        PersistedNode(
                            flowId,
                            AutomationDefinitionIds.RewardRedemptionSource.Value,
                            JsonSerializer.SerializeToElement(
                                new Dictionary<string, object?>
                                {
                                    ["reward-id"] = RewardId,
                                    ["completion-policy"] = "manual",
                                }
                            )
                        ),
                    ],
                }
            );
            _ = await db.SaveChangesAsync();
        }

        var exported = (
            await ExportAutomationsAsync(database, transfer, hostId)
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        var json = System.Text.Encoding.UTF8.GetString(exported.Json);
        json.ShouldContain("custom-command-reference-unmapped");
        json.ShouldContain("overlay-reference-unmapped");
        json.ShouldContain("custom-reward-reference-unmapped");
        json.ShouldNotContain(
            CommandId.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        json.ShouldNotContain(targetId.ToString("D"));
        json.ShouldNotContain(cueId.ToString("D"));
        json.ShouldNotContain(RewardId);
        exported.Document.Sections.Automations!.HostReferences.ShouldBeEmpty();
    }

    [Test]
    public async Task UnknownPluginDefinition_RemainsOutsideFormat1OnImportAndExport()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "plugin-boundary");
        var transfer = AutomationTransfer(database);
        var document = AutomationDocument(
            "Plugin flow",
            [Node("plugin", "plugin.example.action", EmptyObject())],
            []
        );
        var preview = await new ConfigurationImportPreviewService(
            database,
            UnavailableOverlayConfigurationTransferAdapter.Instance,
            transfer.Adapter
        ).PreviewAsync(document, AutomationSelection(hostId), CancellationToken.None);
        preview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single()
            .Issues.ShouldContain(issue =>
                issue.BlocksApply && issue.Message.Contains("not a core")
            );
        _ = (
            await Coordinator(database, new RecordingLogger<ConfigurationTransferCoordinator>())
                .ApplyAsync(
                    Session(hostId),
                    document,
                    AutomationSelection(hostId),
                    new("destination-id", "destination"),
                    CancellationToken.None
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();

        _ = await SeedPersistedFlowAsync(
            database,
            hostId,
            "Persisted plugin",
            "plugin.example.action",
            EmptyObject(),
            "{}"
        );
        _ = (
            await ExportAutomationsAsync(database, transfer, hostId)
        ).ShouldBeOfType<ConfigurationExportOutcome.Unsupported>();
    }

    [Test]
    public async Task CombinedStaging_LaterAuditFailureRollsBackGraphsActivationAndConcreteObservers()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(
            new FailImportAuditSaveInterceptor()
        );
        var (hostId, flowId, runId, documentId) = await SeedAsync(database);
        var events = TestEventBus.Create<AppEventKind>();
        var overlayNotifications = 0;
        _ = events.Subscribe(
            AppEventKind.OverlaysChanged,
            ObserverIdentity.Named("Test.ConfigurationTransfer.Rollback"),
            (_, _) =>
            {
                overlayNotifications++;
                return ValueTask.CompletedTask;
            }
        );
        var trigger = new RecordingReconciliationTrigger();
        var (dispatcher, gate) = Observers(database, events, trigger);
        var logger = new RecordingLogger<ConfigurationTransferCoordinator>();
        var imported = WithMalformedAutomationCommandReference(Document(documentId));
        imported = imported with
        {
            Sections = imported.Sections with
            {
                ChannelToolEnablement = ChannelToolEnablementMapper.FromFlags(
                    HostFeatureFlags.Overlays | HostFeatureFlags.Automations
                ),
            },
        };

        var outcome = await Coordinator(database, logger, dispatcher, gate)
            .ApplyAsync(
                Session(hostId),
                imported,
                new(
                    hostId,
                    [
                        new(
                            ConfigurationSectionId.CustomCommands,
                            ImportConflictStrategy.Merge,
                            []
                        ),
                        new(ConfigurationSectionId.Overlays, ImportConflictStrategy.Merge, []),
                        new(ConfigurationSectionId.Automations, ImportConflictStrategy.Merge, []),
                        new(
                            ConfigurationSectionId.ChannelToolEnablement,
                            ImportConflictStrategy.Merge,
                            []
                        ),
                    ],
                    new HashSet<HostFeatureFlags>
                    {
                        HostFeatureFlags.Overlays,
                        HostFeatureFlags.Automations,
                    }
                ),
                new("destination-id", "destination"),
                CancellationToken.None
            );

        _ = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Failed>();
        logger.Entries.ShouldNotContain(entry =>
            Equals(
                entry.Properties.GetValueOrDefault("Reason"),
                AutomationTransferPlaceholder.CustomCommand
            )
        );
        overlayNotifications.ShouldBe(0);
        trigger.Calls.ShouldBe(0);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.CustomCommands.CountAsync()).ShouldBe(0);
        (await verify.OverlayInstances.CountAsync()).ShouldBe(0);
        (await verify.OverlayCues.CountAsync()).ShouldBe(0);
        (await verify.OverlayMediaAssets.CountAsync()).ShouldBe(0);
        var flow = await verify.AutomationFlows.Include(value => value.Nodes).SingleAsync();
        flow.Id.ShouldBe(flowId);
        flow.Nodes.ShouldHaveSingleItem().ConfigurationJson.ShouldContain("999");
        (await verify.AutomationFlowRuns.SingleAsync()).Id.ShouldBe(runId);
        (await verify.ConfigurationActivations.CountAsync()).ShouldBe(0);
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
        (await verify.Hosts.SingleAsync()).EnabledFeatures.ShouldBe(HostFeatureFlags.None);
    }

    [Test]
    public async Task ConcreteObservers_RunAfterCommitAndReportReconciliationFailureWithoutReplay()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var (hostId, _, runId, documentId) = await SeedAsync(database);
        var events = TestEventBus.Create<AppEventKind>();
        var overlayNotifications = 0;
        _ = events.Subscribe(
            AppEventKind.OverlaysChanged,
            ObserverIdentity.Named("Test.ConfigurationTransfer.Commit"),
            (_, _) =>
            {
                overlayNotifications++;
                return ValueTask.CompletedTask;
            }
        );
        var trigger = new RecordingReconciliationTrigger { Throw = true };
        var (dispatcher, gate) = Observers(database, events, trigger);
        var logger = new RecordingLogger<ConfigurationTransferCoordinator>();

        var outcome = await Coordinator(database, logger, dispatcher, gate)
            .ApplyAsync(
                Session(hostId),
                WithMalformedAutomationCommandReference(Document(documentId)),
                new(
                    hostId,
                    [
                        new(
                            ConfigurationSectionId.CustomCommands,
                            ImportConflictStrategy.Merge,
                            []
                        ),
                        new(ConfigurationSectionId.Overlays, ImportConflictStrategy.Merge, []),
                        new(ConfigurationSectionId.Automations, ImportConflictStrategy.Merge, []),
                    ],
                    new HashSet<HostFeatureFlags>()
                ),
                new("destination-id", "destination"),
                CancellationToken.None
            );

        var applied = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>().Result;
        applied.PostCommitFailures.ShouldBe([
            new(ConfigurationSectionId.Automations, "reconciliation-failed"),
        ]);
        AssertSafeDiagnosticLog(
            logger.Entries,
            AutomationTransferPlaceholder.CustomCommand,
            "flow-0001",
            "node-0001",
            _malformedCommandReference,
            "source-node",
            "flow-ref"
        );
        overlayNotifications.ShouldBe(1);
        trigger.Calls.ShouldBe(1);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(1);
        (await verify.AutomationFlowRuns.SingleAsync()).Id.ShouldBe(runId);
        (await verify.OverlayInstanceEvents.CountAsync()).ShouldBe(0);
        (await verify.OverlayEventFeedItems.CountAsync()).ShouldBe(0);
        (await verify.Hosts.SingleAsync()).EnabledFeatures.ShouldBe(HostFeatureFlags.None);
    }

    [Test]
    public async Task AddMissing_NoChangeOverlayWithMissingDocumentBlocksBeforeAuditOrMutation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var (hostId, _, _, documentId) = await SeedAsync(database);
        Guid referenceId;
        await using (var seed = await database.CreateDbContextAsync())
        {
            referenceId = Guid.NewGuid();
            _ = seed.OverlayMediaAssets.Add(
                new()
                {
                    PublicId = referenceId,
                    HostId = hostId,
                    Name = "Imported clip",
                    ContentRevision = 1,
                    DocumentId = documentId,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        var document = new ConfigurationDocumentV1(
            ConfigurationDocumentCodec.Format,
            1,
            DateTimeOffset.UtcNow,
            new("source", "0.12.0"),
            new(
                Overlays: new(
                    false,
                    true,
                    [],
                    [new("media-ref", "Imported clip", Guid.NewGuid(), "video/mp4", 3)],
                    [],
                    [],
                    []
                )
            )
        );
        var logger = new RecordingLogger<ConfigurationTransferCoordinator>();

        var outcome = await Coordinator(database, logger)
            .ApplyAsync(
                Session(hostId),
                document,
                new(
                    hostId,
                    [new(ConfigurationSectionId.Overlays, ImportConflictStrategy.AddMissing, [])],
                    new HashSet<HostFeatureFlags>()
                ),
                new("destination-id", "destination"),
                CancellationToken.None
            );

        outcome
            .ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>()
            .Issues.ShouldContain(value => value.Message.Contains("not available"));
        await using var verify = await database.CreateDbContextAsync();
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
        var reference = await verify.OverlayMediaAssets.SingleAsync();
        reference.PublicId.ShouldBe(referenceId);
        reference.DocumentId.ShouldBe(documentId);
    }

    [Test]
    public async Task UnselectedOverlaySection_DoesNotAuthorizeFreshCommandDependencies()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var (hostId, _, _, documentId) = await SeedAsync(database);

        var outcome = await new ConfigurationImportPreviewService(database).PreviewAsync(
            Document(documentId),
            new(
                hostId,
                [new(ConfigurationSectionId.CustomCommands, ImportConflictStrategy.Merge, [])],
                new HashSet<HostFeatureFlags>()
            ),
            CancellationToken.None
        );

        var conflict = outcome
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single()
            .Conflicts.ShouldHaveSingleItem();
        conflict.ImportedId.ShouldBe("command-ref");
        conflict.AllowedResolutions.ShouldBe([
            ImportConflictResolution.Skip,
            ImportConflictResolution.Abort,
        ]);
    }

    [Test]
    public async Task Replace_AbsentFlowWithHistoryRequiresRetainAndNeverDeletesRun()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var (hostId, flowId, runId, _) = await SeedAsync(database);
        var logger = new RecordingLogger<ConfigurationTransferCoordinator>();
        var coordinator = Coordinator(database, logger);
        var document = new ConfigurationDocumentV1(
            ConfigurationDocumentCodec.Format,
            1,
            DateTimeOffset.UtcNow,
            new("source", "0.12.0"),
            new(Automations: new([], []))
        );
        var unresolved = new ConfigurationImportSelection(
            hostId,
            [new(ConfigurationSectionId.Automations, ImportConflictStrategy.ReplaceSection, [])],
            new HashSet<HostFeatureFlags>()
        );

        _ = (
            await coordinator.ApplyAsync(
                Session(hostId),
                document,
                unresolved,
                new("destination-id", "destination"),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();
        var retained = unresolved with
        {
            Sections =
            [
                new(
                    ConfigurationSectionId.Automations,
                    ImportConflictStrategy.ReplaceSection,
                    [new($"automation-flow-{flowId:D}", ImportConflictResolution.Retain)]
                ),
            ],
        };

        _ = (
            await coordinator.ApplyAsync(
                Session(hostId),
                document,
                retained,
                new("destination-id", "destination"),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();

        await using var verify = await database.CreateDbContextAsync();
        (await verify.AutomationFlows.SingleAsync()).Id.ShouldBe(flowId);
        (await verify.AutomationFlowRuns.SingleAsync()).Id.ShouldBe(runId);
    }

    [Test]
    public async Task Merge_RemapsImportedCommandReferenceAndPreservesFrozenRunHistory()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var (hostId, flowId, runId, documentId) = await SeedAsync(database);
        var logger = new RecordingLogger<ConfigurationTransferCoordinator>();
        var coordinator = Coordinator(database, logger);
        var document = Document(documentId);
        var selection = new ConfigurationImportSelection(
            hostId,
            [
                new(ConfigurationSectionId.CustomCommands, ImportConflictStrategy.Merge, []),
                new(ConfigurationSectionId.Overlays, ImportConflictStrategy.Merge, []),
                new(ConfigurationSectionId.Automations, ImportConflictStrategy.Merge, []),
            ],
            new HashSet<HostFeatureFlags>()
        );

        var outcome = await coordinator.ApplyAsync(
            Session(hostId),
            document,
            selection,
            new("destination-id", "destination"),
            CancellationToken.None
        );

        if (outcome is ConfigurationImportApplyOutcome.Invalid invalid)
        {
            throw new InvalidOperationException(
                string.Join(" | ", invalid.Issues.Select(value => value.Message))
            );
        }
        if (outcome is ConfigurationImportApplyOutcome.Failed)
        {
            throw new InvalidOperationException(
                "The combined import failed.",
                logger.Entries.LastOrDefault()?.Exception
            );
        }
        _ = outcome.ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        await using var verify = await database.CreateDbContextAsync();
        var command = await verify.CustomCommands.Include(value => value.Action).SingleAsync();
        var target = await verify.OverlayInstances.SingleAsync();
        var cue = await verify.OverlayCues.SingleAsync();
        var commandAction = command.Action.ShouldBeOfType<OverlayCueCustomCommandAction>();
        commandAction.TargetOverlayPublicId.ShouldBe(target.PublicId);
        commandAction.CuePublicId.ShouldBe(cue.PublicId);
        var flow = await verify.AutomationFlows.Include(value => value.Nodes).SingleAsync();
        flow.Id.ShouldBe(flowId);
        var commandNode = flow.Nodes.Single(value =>
            value.DefinitionId == AutomationDefinitionIds.CustomCommandSource.Value
        );
        using var commandConfiguration = JsonDocument.Parse(commandNode.ConfigurationJson);
        commandConfiguration
            .RootElement.GetProperty("custom-command-id")
            .GetInt32()
            .ShouldBe(command.Id);
        var overlayNode = flow.Nodes.Single(value =>
            value.DefinitionId == AutomationDefinitionIds.PlayOverlayCueAction.Value
        );
        using var overlayConfiguration = JsonDocument.Parse(overlayNode.ConfigurationJson);
        overlayConfiguration
            .RootElement.GetProperty("target-id")
            .GetGuid()
            .ShouldBe(target.PublicId);
        overlayConfiguration.RootElement.GetProperty("cue-id").GetGuid().ShouldBe(cue.PublicId);
        var run = await verify.AutomationFlowRuns.SingleAsync();
        run.Id.ShouldBe(runId);
        run.FlowId.ShouldBe(flowId);
        run.DefinitionJson.ShouldBe("{\"frozen\":true}");
    }

    private static async Task<(int HostId, Guid FlowId, Guid RunId, Guid DocumentId)> SeedAsync(
        SqliteBlokeBotDbFactory database
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "destination-id",
            Login = "destination",
            DisplayName = "Destination",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var flowId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var flow = new AutomationFlow
        {
            Id = flowId,
            HostId = host.Id,
            Name = "Command flow",
            SchemaVersion = AutomationFlowSchema.CurrentVersion,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Nodes =
            [
                new()
                {
                    Id = nodeId,
                    FlowId = flowId,
                    DefinitionId = AutomationDefinitionIds.CustomCommandSource.Value,
                    DefinitionSchemaVersion = 1,
                    ConfigurationJson = "{\"custom-command-id\":999}",
                    InputBindingsJson = "{}",
                    ExpressionLanguageVersion = 1,
                },
            ],
        };
        var runId = Guid.NewGuid();
        _ = db.AutomationFlows.Add(flow);
        var documentId = Guid.NewGuid();
        _ = db.OverlayMediaDocuments.Add(
            new()
            {
                Id = documentId,
                ContentType = "video/mp4",
                ByteLength = 3,
                StorageKey = new string('a', 32),
                State = OverlayMediaDocumentState.Available,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        _ = db.AutomationFlowRuns.Add(
            new()
            {
                Id = runId,
                FlowId = flowId,
                HostId = host.Id,
                ContextSchemaVersion = 1,
                SourceDefinitionId = AutomationDefinitionIds.CustomCommandSource.Value,
                SourceNodeId = nodeId,
                SourceOccurrenceId = Guid.NewGuid(),
                ContextJson = "{}",
                DefinitionJson = "{\"frozen\":true}",
                Status = AutomationFlowRunStatus.Completed,
                StartedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();
        return (host.Id, flowId, runId, documentId);
    }

    private static ConfigurationDocumentV1 Document(Guid documentId) =>
        new(
            ConfigurationDocumentCodec.Format,
            1,
            DateTimeOffset.UtcNow,
            new("source", "0.12.0"),
            new(
                CustomCommands: new(
                    "UTC",
                    [],
                    [],
                    [
                        new(
                            "command-ref",
                            "Imported command",
                            true,
                            ["imported"],
                            true,
                            true,
                            0,
                            CustomCommandCooldownScope.User,
                            CustomCommandInvocationLimit.Unlimited,
                            new(
                                CustomCommandActionTypeV1.OverlayCue,
                                OverlayTargetId: "overlay-ref",
                                OverlayTargetName: "Imported cue player",
                                OverlayCueId: "cue-ref",
                                OverlayCueName: "Imported cue",
                                OverlayQueuePolicy: OverlayCueQueuePolicy.Enqueue,
                                OverlayReplyOrder: OverlayCueReplyOrder.Before
                            )
                        ),
                    ]
                ),
                Overlays: new(
                    false,
                    true,
                    [
                        new(
                            "overlay-ref",
                            "Imported cue player",
                            OverlayType.CuePlayer,
                            true,
                            new(1)
                        ),
                    ],
                    [new("media-ref", "Imported clip", documentId, "video/mp4", 3)],
                    [
                        new(
                            "cue-ref",
                            "Imported cue",
                            true,
                            1000,
                            OverlayCueQueuePolicy.Enqueue,
                            [
                                new(
                                    OverlayCueLayerTypeV1.UploadedMedia,
                                    0,
                                    1000,
                                    0,
                                    "media-ref",
                                    MediaKind: OverlayCueMediaKindV1.Video,
                                    Volume: 1,
                                    Fit: OverlayCueFitModeV1.Contain,
                                    Rectangle: new(0, 0, 100, 100)
                                ),
                            ]
                        ),
                    ],
                    [],
                    []
                ),
                Automations: new(
                    [
                        new(
                            "flow-ref",
                            "Command flow",
                            false,
                            AutomationFlowSchema.CurrentVersion,
                            AutomationFlowOrientation.Horizontal,
                            AutomationEdgeStyle.Angular,
                            [
                                new(
                                    "source-node",
                                    AutomationDefinitionIds.CustomCommandSource.Value,
                                    1,
                                    JsonSerializer.SerializeToElement(
                                        new Dictionary<string, string>
                                        {
                                            ["custom-command-id"] = "command-ref",
                                        }
                                    ),
                                    1,
                                    AutomationNodeFailurePolicy.Stop,
                                    [],
                                    0,
                                    0
                                ),
                                new(
                                    "overlay-node",
                                    AutomationDefinitionIds.PlayOverlayCueAction.Value,
                                    1,
                                    JsonSerializer.SerializeToElement(
                                        new Dictionary<string, string>
                                        {
                                            ["target-id"] = "overlay-ref",
                                            ["cue-id"] = "cue-ref",
                                        }
                                    ),
                                    1,
                                    AutomationNodeFailurePolicy.Stop,
                                    [],
                                    200,
                                    0
                                ),
                            ],
                            [
                                new(
                                    "edge-ref",
                                    AutomationEdgeKind.Flow,
                                    "source-node",
                                    "flow",
                                    "overlay-node",
                                    "flow"
                                ),
                            ]
                        ),
                    ],
                    [
                        new(
                            "command-ref",
                            AutomationHostReferenceKindV1.CustomCommand,
                            "Imported command"
                        ),
                        new(
                            "overlay-ref",
                            AutomationHostReferenceKindV1.OverlayTarget,
                            "Imported cue player"
                        ),
                        new("cue-ref", AutomationHostReferenceKindV1.OverlayCue, "Imported cue"),
                    ]
                )
            )
        );

    private static ConfigurationDocumentV1 WithMalformedAutomationCommandReference(
        ConfigurationDocumentV1 document
    )
    {
        var automations = document.Sections.Automations!;
        var flow = automations.Flows.Single();
        var source = flow.Nodes.Single(value =>
            value.DefinitionId == AutomationDefinitionIds.CustomCommandSource.Value
        );
        source = source with
        {
            Configuration = JsonSerializer.SerializeToElement(
                new Dictionary<string, object>
                {
                    ["custom-command-id"] = new { value = _malformedCommandReference },
                }
            ),
        };
        return document with
        {
            Sections = document.Sections with
            {
                Automations = automations with
                {
                    Flows =
                    [
                        flow with
                        {
                            Nodes = flow
                                .Nodes.Select(value => value.Id == source.Id ? source : value)
                                .ToArray(),
                        },
                    ],
                },
            },
        };
    }

    private static ConfigurationDocumentV1 OrdinaryAutomationDocument(
        string flowName,
        JsonElement sendChatConfiguration
    ) =>
        AutomationDocument(
            flowName,
            [
                Node("source", AutomationDefinitionIds.StreamOnlineSource.Value, EmptyObject()),
                Node(
                    "action",
                    AutomationDefinitionIds.SendChatAction.Value,
                    sendChatConfiguration,
                    [new("message", AutomationInputBindingMode.Fixed)]
                ),
            ],
            [new("edge", AutomationEdgeKind.Flow, "source", "flow", "action", "flow")]
        );

    private static ConfigurationDocumentV1 DynamicTransformDocument()
    {
        var configuration = JsonSerializer.SerializeToElement(
            new Dictionary<string, object>
            {
                ["inputs"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["port-id"] = "stream-input",
                        ["cel-identifier"] = "stream",
                        ["display-name"] = "Stream",
                        ["binding-field-id"] = "stream-binding",
                        ["type"] = AutomationPortValueType.Stream.ToString(),
                        ["nullability"] = AutomationPortNullability.NonNullable.ToString(),
                        ["fixed"] = new Dictionary<string, object>(),
                    },
                },
                ["outputs"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["port-id"] = "text-output",
                        ["display-name"] = "Text",
                        ["type"] = AutomationPortValueType.Text.ToString(),
                        ["nullability"] = AutomationPortNullability.NonNullable.ToString(),
                        ["cel"] = "'ready'",
                    },
                },
            }
        );
        return AutomationDocument(
            "Dynamic transform",
            [
                Node("source", AutomationDefinitionIds.StreamOnlineSource.Value, EmptyObject()),
                Node(
                    "transform",
                    AutomationDefinitionIds.CelTransform.Value,
                    configuration,
                    [new("stream-binding", AutomationInputBindingMode.Fixed)]
                ),
            ],
            []
        );
    }

    private static ConfigurationDocumentV1 DynamicFixedValueDocument(
        string flowName,
        AutomationPortValueType valueType,
        object fixedValue
    ) =>
        AutomationDocument(
            flowName,
            [
                Node("source", AutomationDefinitionIds.StreamOnlineSource.Value, EmptyObject()),
                Node(
                    "transform",
                    AutomationDefinitionIds.CelTransform.Value,
                    DynamicFixedValueConfiguration(valueType, fixedValue),
                    [new("value-binding", AutomationInputBindingMode.Fixed)]
                ),
            ],
            []
        );

    private static JsonElement DynamicFixedValueConfiguration(
        AutomationPortValueType valueType,
        object fixedValue
    ) =>
        TransformConfiguration(
            [
                TransformInput(
                    "value-input",
                    "value",
                    "Value",
                    "value-binding",
                    valueType,
                    fixedValue
                ),
            ],
            [TransformOutput("text-output", "Text", "'ready'")]
        );

    private static ConfigurationDocumentV1 DynamicCollectionDocument(
        DynamicCollectionKind kind,
        int count
    )
    {
        var inputs = kind switch
        {
            DynamicCollectionKind.Inputs => Enumerable
                .Range(0, count)
                .Select(index =>
                    TransformInput(
                        $"input-{index}",
                        $"value{index}",
                        $"Value {index}",
                        $"binding-{index}",
                        AutomationPortValueType.Number,
                        index
                    )
                )
                .ToArray(),
            DynamicCollectionKind.Arguments =>
            [
                TransformInput(
                    "arguments-input",
                    "argumentValues",
                    "Arguments",
                    "arguments-binding",
                    AutomationPortValueType.Arguments,
                    Enumerable.Range(0, count).Select(index => $"argument-{index}").ToArray()
                ),
            ],
            _ => [],
        };
        var outputs =
            kind == DynamicCollectionKind.Outputs
                ? Enumerable
                    .Range(0, count)
                    .Select(index =>
                        TransformOutput($"output-{index}", $"Output {index}", "'ready'")
                    )
                    .ToArray()
                : [TransformOutput("text-output", "Text", "'ready'")];
        IReadOnlyList<AutomationInputBindingV1> bindings = kind switch
        {
            DynamicCollectionKind.Inputs when count <= 1000 => Enumerable
                .Range(0, count)
                .Select(index => new AutomationInputBindingV1(
                    $"binding-{index}",
                    AutomationInputBindingMode.Fixed
                ))
                .ToArray(),
            DynamicCollectionKind.Arguments =>
            [
                new("arguments-binding", AutomationInputBindingMode.Fixed),
            ],
            _ => [],
        };
        return AutomationDocument(
            $"{kind} {count}",
            [
                Node("source", AutomationDefinitionIds.StreamOnlineSource.Value, EmptyObject()),
                Node(
                    "transform",
                    AutomationDefinitionIds.CelTransform.Value,
                    TransformConfiguration(inputs, outputs),
                    bindings
                ),
            ],
            []
        );
    }

    private static Dictionary<string, object> TransformInput(
        string portId,
        string identifier,
        string displayName,
        string bindingFieldId,
        AutomationPortValueType valueType,
        object fixedValue,
        AutomationPortNullability nullability = AutomationPortNullability.NonNullable
    ) =>
        new()
        {
            ["port-id"] = portId,
            ["cel-identifier"] = identifier,
            ["display-name"] = displayName,
            ["binding-field-id"] = bindingFieldId,
            ["type"] = valueType.ToString(),
            ["nullability"] = nullability.ToString(),
            ["fixed"] = fixedValue,
        };

    private static Dictionary<string, object> IdentityTransformInput(
        string prefix,
        AutomationPortValueType valueType,
        string login,
        string displayName
    ) =>
        TransformInput(
            $"{prefix}-input",
            $"{prefix}Value",
            valueType.ToString(),
            $"{prefix}-binding",
            valueType,
            new Dictionary<string, object> { ["login"] = login, ["display-name"] = displayName }
        );

    private static Dictionary<string, object> TransformOutput(
        string portId,
        string displayName,
        string source,
        AutomationPortValueType valueType = AutomationPortValueType.Text,
        AutomationPortNullability nullability = AutomationPortNullability.NonNullable
    ) =>
        new()
        {
            ["port-id"] = portId,
            ["display-name"] = displayName,
            ["type"] = valueType.ToString(),
            ["nullability"] = nullability.ToString(),
            ["cel"] = source,
        };

    private static JsonElement TransformConfiguration(
        IReadOnlyList<Dictionary<string, object>> inputs,
        IReadOnlyList<Dictionary<string, object>> outputs
    ) =>
        JsonSerializer.SerializeToElement(
            new Dictionary<string, object> { ["inputs"] = inputs, ["outputs"] = outputs }
        );

    private static JsonElement MalformedNestedIdentityConfiguration(
        string login,
        string displayName,
        string orphanLogin
    ) =>
        JsonSerializer.SerializeToElement(
            new Dictionary<string, object>
            {
                ["InPuTs"] = new Dictionary<string, object>
                {
                    ["nested"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["TyPe"] = AutomationPortValueType.Actor.ToString(),
                            ["FiXeD"] = new Dictionary<string, string>
                            {
                                ["LoGiN"] = login,
                                ["DiSpLaY-NaMe"] = displayName,
                            },
                        },
                        new Dictionary<string, object>
                        {
                            ["wrapper"] = new Dictionary<string, object>
                            {
                                ["fixed"] = new Dictionary<string, object>
                                {
                                    ["payload"] = new Dictionary<string, string>
                                    {
                                        ["display-name"] = displayName,
                                    },
                                    ["keep"] = "nested retained",
                                },
                            },
                        },
                        new Dictionary<string, object>
                        {
                            ["type"] = AutomationPortValueType.Channel.ToString(),
                            ["fixed"] = AutomationTransferPlaceholder.Create(
                                AutomationTransferPlaceholder.Identity
                            ),
                        },
                    },
                    ["orphan"] = new Dictionary<string, string>
                    {
                        ["login"] = orphanLogin,
                        ["display-name"] = displayName,
                    },
                },
                ["outputs"] = new Dictionary<string, bool> { ["malformed"] = true },
                ["unrelated"] = new Dictionary<string, string>
                {
                    ["keep"] = "retained",
                    ["display-name"] = "Unrelated label",
                },
            }
        );

    private static string DynamicCollectionLimitMessage(DynamicCollectionKind kind) =>
        kind switch
        {
            DynamicCollectionKind.Inputs =>
                "The CEL Transform input collection exceeds the 1000 record limit.",
            DynamicCollectionKind.Outputs =>
                "The CEL Transform output collection exceeds the 1000 record limit.",
            DynamicCollectionKind.Arguments =>
                "A fixed CEL Arguments value exceeds the 1000 record limit.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static void AssertSafeDiagnosticLog(
        IReadOnlyList<UiFaultLogEntry> entries,
        string reason,
        string flow,
        string node,
        params string[] forbiddenValues
    )
    {
        var diagnostic = entries.Single(entry =>
            Equals(entry.Properties.GetValueOrDefault("Reason"), reason)
        );
        diagnostic.Level.ShouldBe(LogLevel.Warning);
        diagnostic.Properties.ShouldContainKey("HostId");
        diagnostic.Properties.GetValueOrDefault("Flow").ShouldBe(flow);
        diagnostic.Properties.GetValueOrDefault("Node").ShouldBe(node);
        diagnostic.Properties.ShouldNotContainKey("FlowId");
        diagnostic.Properties.ShouldNotContainKey("NodeId");
        AssertLogsExclude(entries, forbiddenValues);
    }

    private static void AssertLogsExclude(
        IReadOnlyList<UiFaultLogEntry> entries,
        params string[] forbiddenValues
    )
    {
        foreach (var entry in entries)
        {
            foreach (var forbidden in forbiddenValues)
            {
                entry.Message.ShouldNotContain(forbidden);
            }
        }
        foreach (var value in entries.SelectMany(entry => entry.Properties.Values))
        {
            var text = value?.ToString();
            foreach (var forbidden in forbiddenValues)
            {
                text?.ShouldNotContain(forbidden);
            }
        }
    }

    private static ConfigurationDocumentV1 AutomationDocument(
        string flowName,
        IReadOnlyList<AutomationNodeV1> nodes,
        IReadOnlyList<AutomationEdgeV1> edges
    ) =>
        new(
            ConfigurationDocumentCodec.Format,
            1,
            DateTimeOffset.UtcNow,
            new("source", "0.12.0"),
            new(
                Automations: new(
                    [
                        new(
                            "flow",
                            flowName,
                            false,
                            AutomationFlowSchema.CurrentVersion,
                            AutomationFlowOrientation.Horizontal,
                            AutomationEdgeStyle.Angular,
                            nodes,
                            edges
                        ),
                    ],
                    []
                )
            )
        );

    private static AutomationNodeV1 Node(
        string id,
        string definitionId,
        JsonElement configuration,
        IReadOnlyList<AutomationInputBindingV1>? bindings = null
    ) =>
        new(
            id,
            definitionId,
            1,
            configuration,
            1,
            AutomationNodeFailurePolicy.Stop,
            bindings ?? [],
            0,
            0
        );

    private static JsonElement EmptyObject() =>
        JsonSerializer.SerializeToElement(new Dictionary<string, object>());

    private static ConfigurationImportSelection AutomationSelection(int hostId) =>
        new(
            hostId,
            [new(ConfigurationSectionId.Automations, ImportConflictStrategy.Merge, [])],
            new HashSet<HostFeatureFlags>()
        );

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database, string login)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "destination-id",
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<(Guid FlowId, Guid NodeId)> SeedPersistedFlowAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        string name,
        string definitionId,
        JsonElement configuration,
        string inputBindingsJson,
        bool enabled = false
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var flowId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        _ = db.AutomationFlows.Add(
            new()
            {
                Id = flowId,
                HostId = hostId,
                Name = name,
                SchemaVersion = AutomationFlowSchema.CurrentVersion,
                IsEnabled = enabled,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Nodes =
                [
                    new()
                    {
                        Id = nodeId,
                        FlowId = flowId,
                        DefinitionId = definitionId,
                        DefinitionSchemaVersion = 1,
                        ConfigurationJson = configuration.GetRawText(),
                        InputBindingsJson = inputBindingsJson,
                        ExpressionLanguageVersion = 1,
                    },
                ],
            }
        );
        _ = await db.SaveChangesAsync();
        return (flowId, nodeId);
    }

    private static AutomationFlowNode PersistedNode(
        Guid flowId,
        string definitionId,
        JsonElement configuration
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            FlowId = flowId,
            DefinitionId = definitionId,
            DefinitionSchemaVersion = 1,
            ConfigurationJson = configuration.GetRawText(),
            InputBindingsJson = "{}",
            ExpressionLanguageVersion = 1,
        };

    private static async Task SeedRunAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        Guid flowId,
        Guid nodeId,
        string frozenDefinition
    )
    {
        await using var db = await database.CreateDbContextAsync();
        _ = db.AutomationFlowRuns.Add(
            new()
            {
                Id = Guid.NewGuid(),
                FlowId = flowId,
                HostId = hostId,
                ContextSchemaVersion = 1,
                SourceDefinitionId = AutomationDefinitionIds.CelTransform.Value,
                SourceNodeId = nodeId,
                SourceOccurrenceId = Guid.NewGuid(),
                ContextJson = "{}",
                DefinitionJson = frozenDefinition,
                Status = AutomationFlowRunStatus.Completed,
                StartedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static Task<ConfigurationExportOutcome> ExportAutomationsAsync(
        SqliteBlokeBotDbFactory database,
        AutomationTransferComponents transfer,
        int hostId
    ) =>
        new ConfigurationDocumentExporter(
            database,
            new(),
            transfer.Catalog,
            transfer.FlowService,
            NullLogger<ConfigurationDocumentExporter>.Instance,
            TimeProvider.System
        ).ExportAsync(
            hostId,
            new(
                new HashSet<ConfigurationSectionId> { ConfigurationSectionId.Automations },
                new(false, false, false)
            ),
            CancellationToken.None
        );

    private static ConfigurationTransferCoordinator Coordinator(
        SqliteBlokeBotDbFactory database,
        ILogger<ConfigurationTransferCoordinator> logger,
        IConfigurationImportObserverDispatcher? importObservers = null,
        SemaphoreSlim? mediaGate = null
    )
    {
        var transfer = AutomationTransfer(database);
        var automations = transfer.Adapter;
        var overlayOptions = Options.Create(new BlokeBotOptions());
        var overlays = new OverlayConfigurationTransferAdapter(
            new CryptographicOverlayAccessKeyGenerator(),
            null!,
            overlayOptions,
            TimeProvider.System
        );
        var writer = new CustomCommandConfigurationGraphWriter(
            database,
            null!,
            TimeProvider.System
        );
        return new(
            database,
            new(writer, new(), TimeProvider.System),
            new GrantedAuthority(),
            new(),
            TimeProvider.System,
            logger,
            new(database, overlays, automations),
            overlays,
            automations,
            importObservers ?? UnavailableConfigurationImportObserverDispatcher.Instance,
            mediaGate ?? new(1, 1)
        );
    }

    private static AutomationTransferComponents AutomationTransfer(SqliteBlokeBotDbFactory database)
    {
        var services = ConfigurationTransferAutomationTestServices.Create(database);
        return new(
            services.Catalog,
            services.Flows,
            new AutomationConfigurationTransferAdapter(
                services.Flows,
                services.Catalog,
                TimeProvider.System
            )
        );
    }

    private sealed record AutomationTransferComponents(
        AutomationCatalogService Catalog,
        AutomationFlowService FlowService,
        AutomationConfigurationTransferAdapter Adapter
    );

    private enum DynamicCollectionKind
    {
        Inputs,
        Outputs,
        Arguments,
    }

    private static (
        IConfigurationImportObserverDispatcher Dispatcher,
        SemaphoreSlim Gate
    ) Observers(
        SqliteBlokeBotDbFactory database,
        EventBus<AppEventKind> events,
        RecordingReconciliationTrigger trigger
    )
    {
        var options = Options.Create(
            new BlokeBotOptions
            {
                DatabasePath = Path.Combine(
                    Path.GetTempPath(),
                    $"blokebot-transfer-observers-{Guid.NewGuid():N}",
                    "state.db"
                ),
            }
        );
        var maintenance = new OverlayMediaMaintenanceService(
            database,
            options,
            new SystemOverlayMediaFileDeletion(),
            TimeProvider.System,
            NullLogger<OverlayMediaMaintenanceService>.Instance
        );
        return (
            new ConfigurationImportObserverDispatcher(
                [
                    new OverlayConfigurationImportObserver(events, maintenance),
                    new AutomationConfigurationImportObserver(trigger),
                ],
                NullLogger<ConfigurationImportObserverDispatcher>.Instance
            ),
            maintenance.Gate
        );
    }

    private static AuthenticatedSession Session(int hostId)
    {
        var host = new BotHostChoice(hostId, "destination", "Destination", AuthRole.Streamer);
        return new()
        {
            IsAuthenticated = true,
            UserId = "destination-id",
            Login = "destination",
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private sealed class GrantedAuthority : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        ) => Task.FromResult<ModeratorAuthorityOutcome>(new ModeratorAuthorityOutcome.Granted());
    }

    private sealed class RecordingReconciliationTrigger : IEventSubChannelReconciliationTrigger
    {
        internal int Calls { get; private set; }

        internal bool Throw { get; init; }

        public Task ReconcileAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Throw
                ? Task.FromException(
                    new InvalidOperationException("Planned reconciliation failure.")
                )
                : Task.CompletedTask;
        }

        public Task ReconcileRevocationAsync(
            string subscriptionId,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    private sealed class FailImportAuditSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) =>
            eventData.Context?.ChangeTracker.Entries<ConfigurationImportAudit>().Any() == true
                ? ValueTask.FromException<InterceptionResult<int>>(
                    new DbUpdateException("Planned import commit failure.")
                )
                : ValueTask.FromResult(result);
    }
}
