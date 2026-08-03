using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomationCatalogTests
{
    [Test]
    public void CoreCatalog_DescribingInitialNodes_ExposesStableInspectorMetadata()
    {
        var catalog = Catalog();

        HostFeatureFlags.All.Contains(HostFeatureFlags.Automations).ShouldBeTrue();
        var featureCard = HostFeatureCatalog
            .Cards(HostFeatureFlags.None)
            .Single(static card => card.Feature == HostFeatureFlags.Automations);
        featureCard.Enabled.ShouldBeFalse();
        featureCard.Name.ShouldBe("Automations");

        catalog
            .Descriptors.Select(static definition => definition.Id.Value)
            .ShouldBe(["condition", "custom-command", "delay", "play-overlay-cue", "send-chat"]);
        catalog.Descriptors.ShouldAllBe(static definition =>
            definition.Scope == AutomationDefinitionScope.Host
            && definition.Schema.Current == new AutomationSchemaVersion(1)
            && definition.Schema.OldestReadable == new AutomationSchemaVersion(1)
            && !string.IsNullOrWhiteSpace(definition.Display.Name)
            && !string.IsNullOrWhiteSpace(definition.Display.Description)
            && !string.IsNullOrWhiteSpace(definition.Display.Category)
        );
        catalog
            .Descriptors.SelectMany(static definition => definition.Configuration)
            .ShouldAllBe(static field =>
                !string.IsNullOrWhiteSpace(field.Id.Value)
                && !string.IsNullOrWhiteSpace(field.Name)
                && !string.IsNullOrWhiteSpace(field.Description)
            );

        var source = Descriptor(catalog, AutomationDefinitionIds.CustomCommandSource);
        source.Kind.ShouldBe(AutomationNodeKind.Source);
        source
            .Outputs.Single(static port => port.Id == new AutomationPortId("arguments"))
            .Sensitivity.ShouldBe(AutomationDataSensitivity.Sensitive);
        source
            .Configuration.Single()
            .FieldType.ShouldBeOfType<AutomationConfigurationFieldType.Reference>()
            .ReferenceKind.ShouldBe(AutomationReferenceKind.CustomCommand);

        var sendChat = Descriptor(catalog, AutomationDefinitionIds.SendChatAction);
        sendChat.Capabilities.ShouldBe(AutomationActionCapabilities.SendsChat);
        sendChat.RetrySafety.ShouldBe(AutomationActionRetrySafety.Unsafe);
        sendChat.Inputs.ShouldContain(static port =>
            port.ValueType == AutomationPortValueType.Flow
        );
        sendChat.Outputs.ShouldContain(static port =>
            port.ValueType == AutomationPortValueType.Flow
        );

        var playCue = Descriptor(catalog, AutomationDefinitionIds.PlayOverlayCueAction);
        playCue.Capabilities.ShouldBe(AutomationActionCapabilities.PlaysOverlays);
        playCue.RetrySafety.ShouldBe(AutomationActionRetrySafety.Unsafe);

        var delay = Descriptor(catalog, AutomationDefinitionIds.DelayControl)
            .Configuration.Single()
            .FieldType.ShouldBeOfType<AutomationConfigurationFieldType.Duration>();
        delay.Minimum.ShouldBe(TimeSpan.FromMilliseconds(1));
        delay.Maximum.ShouldBeNull();

        catalog
            .Descriptors.Where(static definition => definition.Kind != AutomationNodeKind.Action)
            .ShouldAllBe(static definition =>
                definition.Capabilities == AutomationActionCapabilities.None
                && definition.RetrySafety == AutomationActionRetrySafety.NotApplicable
            );
    }

    [Test]
    public async Task HostGate_DiscoveryAndValidation_AreOptInIsolatedAndPauseWithoutReplay()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var disabledHost = await SeedHostAsync(dbFactory, "disabled", HostFeatureFlags.None);
        var enabledHost = await SeedHostAsync(dbFactory, "enabled", HostFeatureFlags.Automations);
        var features = FeatureService(dbFactory);
        var service = new AutomationCatalogService(Catalog(), features);
        var persisted = Persisted("send-chat", 1, """{"message":"Hello {actor}"}""");

        var disabled = await service.DiscoverAsync(new(disabledHost), CancellationToken.None);
        disabled.Availability.ShouldBe(AutomationCatalogAvailability.Disabled);
        disabled.Definitions.ShouldBeEmpty();
        _ = (
            await service.ValidatePersistedForSaveAsync(
                new(disabledHost),
                persisted,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationConfigurationCheck.FeatureDisabled>();

        var enabled = await service.DiscoverAsync(new(enabledHost), CancellationToken.None);
        enabled.Availability.ShouldBe(AutomationCatalogAvailability.Enabled);
        enabled.Definitions.Length.ShouldBe(5);

        await features.EnableAsync(
            disabledHost,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        var firstEnable = await service.DiscoverAsync(new(disabledHost), CancellationToken.None);
        firstEnable.Definitions.ShouldBe(enabled.Definitions);

        await features.DisableAsync(
            disabledHost,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        (
            await service.DiscoverAsync(new(disabledHost), CancellationToken.None)
        ).Definitions.ShouldBeEmpty();
        await features.EnableAsync(
            disabledHost,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );

        var restored = await service.DiscoverAsync(new(disabledHost), CancellationToken.None);
        restored.Definitions.ShouldBe(firstEnable.Definitions);
        _ = (
            await service.ValidatePersistedBeforeExecutionAsync(
                new(disabledHost),
                Context(disabledHost),
                persisted,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationConfigurationCheck.Valid>();
        (await service.DiscoverAsync(new(99_999), CancellationToken.None)).Availability.ShouldBe(
            AutomationCatalogAvailability.HostNotFound
        );
    }

    [Test]
    public async Task InitialDefinitions_SaveAndExecutionValidation_AcceptAndRejectTypedConfigurations()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer", HostFeatureFlags.Automations);
        var service = new AutomationCatalogService(Catalog(), FeatureService(dbFactory));
        var valid = new (AutomationDefinitionId Id, AutomationConfiguration Configuration)[]
        {
            (
                AutomationDefinitionIds.CustomCommandSource,
                new CustomCommandSourceConfiguration(new(1))
            ),
            (AutomationDefinitionIds.SendChatAction, new SendChatActionConfiguration("Hello")),
            (
                AutomationDefinitionIds.PlayOverlayCueAction,
                new PlayOverlayCueActionConfiguration(new(Guid.NewGuid()), new(Guid.NewGuid()))
            ),
            (
                AutomationDefinitionIds.ConditionControl,
                new ConditionControlConfiguration("actor.login == 'viewer'")
            ),
            (
                AutomationDefinitionIds.DelayControl,
                new DelayControlConfiguration(TimeSpan.FromDays(30))
            ),
        };
        var invalid = new (AutomationDefinitionId Id, AutomationConfiguration Configuration)[]
        {
            (
                AutomationDefinitionIds.CustomCommandSource,
                new CustomCommandSourceConfiguration(new(0))
            ),
            (AutomationDefinitionIds.SendChatAction, new SendChatActionConfiguration(" ")),
            (
                AutomationDefinitionIds.PlayOverlayCueAction,
                new PlayOverlayCueActionConfiguration(new(Guid.Empty), new(Guid.Empty))
            ),
            (AutomationDefinitionIds.ConditionControl, new ConditionControlConfiguration(" ")),
            (AutomationDefinitionIds.DelayControl, new DelayControlConfiguration(TimeSpan.Zero)),
        };

        foreach (var candidate in valid)
        {
            _ = (
                await service.ValidateForSaveAsync(
                    new(hostId),
                    candidate.Id,
                    new(1),
                    candidate.Configuration,
                    CancellationToken.None
                )
            ).ShouldBeOfType<AutomationConfigurationCheck.Valid>();
            _ = (
                await service.ValidateBeforeExecutionAsync(
                    new(hostId),
                    Context(hostId),
                    candidate.Id,
                    new(1),
                    candidate.Configuration,
                    CancellationToken.None
                )
            ).ShouldBeOfType<AutomationConfigurationCheck.Valid>();
        }

        foreach (var candidate in invalid)
        {
            _ = (
                await service.ValidateForSaveAsync(
                    new(hostId),
                    candidate.Id,
                    new(1),
                    candidate.Configuration,
                    CancellationToken.None
                )
            ).ShouldBeOfType<AutomationConfigurationCheck.Invalid>();
        }

        _ = (
            await service.ValidateForSaveAsync(
                new(hostId),
                AutomationDefinitionIds.SendChatAction,
                new(1),
                new DelayControlConfiguration(TimeSpan.FromSeconds(1)),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationConfigurationCheck.Invalid>();
        _ = (
            await service.ValidateBeforeExecutionAsync(
                new(hostId),
                Context(hostId + 1),
                AutomationDefinitionIds.SendChatAction,
                new(1),
                new SendChatActionConfiguration("Hello"),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationConfigurationCheck.HostMismatch>();
    }

    [Test]
    public async Task PersistedDefinitions_DecodeThroughStableIdsBeforeBothValidationPhases()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer", HostFeatureFlags.Automations);
        var service = new AutomationCatalogService(Catalog(), FeatureService(dbFactory));
        var persisted = new[]
        {
            Persisted("custom-command", 1, """{"custom-command-id":4}"""),
            Persisted("send-chat", 1, """{"message":"Hello"}"""),
            Persisted(
                "play-overlay-cue",
                1,
                $$"""{"target-id":"{{Guid.NewGuid()}}","cue-id":"{{Guid.NewGuid()}}"}"""
            ),
            Persisted("condition", 1, """{"expression":"actor.login == 'viewer'"}"""),
            Persisted("delay", 1, """{"duration-milliseconds":2592000000}"""),
        };

        foreach (var candidate in persisted)
        {
            _ = (
                await service.ValidatePersistedForSaveAsync(
                    new(hostId),
                    candidate,
                    CancellationToken.None
                )
            ).ShouldBeOfType<AutomationConfigurationCheck.Valid>();
            _ = (
                await service.ValidatePersistedBeforeExecutionAsync(
                    new(hostId),
                    Context(hostId),
                    candidate,
                    CancellationToken.None
                )
            ).ShouldBeOfType<AutomationConfigurationCheck.Valid>();
        }

        _ = (
            await service.ValidatePersistedForSaveAsync(
                new(hostId),
                Persisted("send-chat", 1, """{"message":""}"""),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationConfigurationCheck.Invalid>();
        var unsupported = (
            await service.ValidatePersistedForSaveAsync(
                new(hostId),
                Persisted("send-chat", 2, "42"),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationConfigurationCheck.SchemaUnsupported>();
        unsupported.Status.ShouldBe(AutomationSchemaCompatibilityStatus.NewerThanSupported);

        var serialized = JsonSerializer.Serialize(persisted[1]);
        serialized.ShouldContain("send-chat");
        serialized.ShouldNotContain(nameof(SendChatActionConfiguration));
        serialized.ShouldNotContain("BlokeBot.Core");
        serialized.ShouldNotContain("System.Private.CoreLib");
    }

    [Test]
    public async Task Registration_MultipleModulesWorkAndDuplicateOrUnsupportedSchemasFailStartupClearly()
    {
        var extended = new AutomationDefinitionCatalog([
            new CoreAutomationCatalogModule(),
            new AdditionalAutomationModule(),
        ]);
        extended.Descriptors.Length.ShouldBe(7);
        extended.Descriptors.ShouldContain(static definition =>
            definition.Id == new AutomationDefinitionId("sample-source")
        );
        extended.Descriptors.ShouldContain(static definition =>
            definition.Id == new AutomationDefinitionId("sample-action")
            && definition.Kind == AutomationNodeKind.Action
        );

        var duplicate = Should.Throw<AutomationCatalogRegistrationException>(() =>
            _ = new AutomationDefinitionCatalog([
                new CoreAutomationCatalogModule(),
                new DuplicateAutomationModule(),
            ])
        );
        duplicate.Message.ShouldContain("send-chat");
        duplicate.Message.ShouldContain("more than once");

        var unsupported = Should.Throw<AutomationCatalogRegistrationException>(() =>
            _ = new AutomationDefinitionCatalog([new UnsupportedSchemaAutomationModule()])
        );
        unsupported.Message.ShouldContain("Schema versions");
        unsupported.Message.ShouldContain("1..1");

        var services = new ServiceCollection();
        _ = services
            .AddBlokeBotAutomations()
            .AddAutomationCatalogModule<DuplicateAutomationModule>();
        await using var provider = services.BuildServiceProvider();
        var startupFailure = Should.Throw<AutomationCatalogRegistrationException>(() =>
            _ = provider.GetRequiredService<AutomationDefinitionCatalog>()
        );
        startupFailure.Message.ShouldContain("send-chat");
    }

    [Test]
    public void SchemaAndSensitiveValues_CompatibilityAndExternalProjectionFailClosed()
    {
        var compatibility = new AutomationSchemaCompatibility(new(3), new(2));
        compatibility.Classify(new(3)).ShouldBe(AutomationSchemaCompatibilityStatus.Current);
        compatibility
            .Classify(new(2))
            .ShouldBe(AutomationSchemaCompatibilityStatus.UpgradeRequired);
        compatibility
            .Classify(new(1))
            .ShouldBe(AutomationSchemaCompatibilityStatus.OlderThanSupported);
        compatibility
            .Classify(new(4))
            .ShouldBe(AutomationSchemaCompatibilityStatus.NewerThanSupported);

        var safeName = new AutomationVariableName("viewer");
        var sensitiveName = new AutomationVariableName("token");
        var variables = new AutomationVariableSet(
            new Dictionary<AutomationVariableName, AutomationVariable>
            {
                [safeName] = new(
                    new AutomationValue.Text("viewer"),
                    AutomationDataSensitivity.Safe
                ),
                [sensitiveName] = new(
                    new AutomationValue.Text("secret"),
                    AutomationDataSensitivity.Sensitive
                ),
            }
        );

        variables.SafeForExternalUse().Keys.ShouldBe([safeName]);
        variables.ForExecution().Keys.ShouldBe([safeName, sensitiveName], ignoreOrder: true);
    }

    private static AutomationDefinitionCatalog Catalog() =>
        new([new CoreAutomationCatalogModule()]);

    private static AutomationDefinitionDescriptor Descriptor(
        AutomationDefinitionCatalog catalog,
        AutomationDefinitionId id
    ) => catalog.Descriptors.Single(definition => definition.Id == id);

    private static HostFeatureService FeatureService(SqliteBlokeBotDbFactory dbFactory) =>
        new(dbFactory, new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()), []);

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login,
        HostFeatureFlags enabledFeatures
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            EnabledFeatures = enabledFeatures,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static PersistedAutomationNodeDefinition Persisted(
        string typeId,
        int schemaVersion,
        string json
    )
    {
        using var document = JsonDocument.Parse(json);
        return new(typeId, schemaVersion, document.RootElement.Clone());
    }

    private static AutomationContext Context(int hostId) =>
        new(
            new(Guid.NewGuid(), AutomationDefinitionIds.CustomCommandSource),
            new("viewer-id", "viewer", "Viewer"),
            new(new(hostId), $"channel-{hostId}", "streamer", "Streamer"),
            new("stream-id", "A stream", "A game", DateTimeOffset.UtcNow),
            new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            [new(1, "hello")],
            new AutomationVariableSet([])
        );

    private static IAutomationDefinition TestDefinition(
        AutomationDefinitionId id,
        AutomationSchemaCompatibility? schema = null,
        AutomationNodeKind kind = AutomationNodeKind.Source
    ) =>
        new AutomationDefinition<SendChatActionConfiguration>(
            new(
                id,
                kind,
                AutomationDefinitionScope.Host,
                schema ?? new(new(1), new(1)),
                new("Sample", "A sample automation source.", "Test"),
                [],
                [],
                [],
                kind == AutomationNodeKind.Action
                    ? AutomationActionCapabilities.SendsChat
                    : AutomationActionCapabilities.None,
                kind == AutomationNodeKind.Action
                    ? AutomationActionRetrySafety.Unsafe
                    : AutomationActionRetrySafety.NotApplicable
            ),
            static json => new AutomationConfigurationParseResult.Parsed(
                new SendChatActionConfiguration(json.ToString())
            ),
            static _ => AutomationValidationResult.Valid
        );

    private sealed class AdditionalAutomationModule : IAutomationCatalogModule
    {
        public AutomationModuleId Id => new("tests.additional");

        public IEnumerable<IAutomationDefinition> Definitions =>
            [
                TestDefinition(new("sample-source")),
                TestDefinition(new("sample-action"), kind: AutomationNodeKind.Action),
            ];
    }

    public sealed class DuplicateAutomationModule : IAutomationCatalogModule
    {
        public AutomationModuleId Id => new("tests.duplicate");

        public IEnumerable<IAutomationDefinition> Definitions =>
            [TestDefinition(AutomationDefinitionIds.SendChatAction)];
    }

    private sealed class UnsupportedSchemaAutomationModule : IAutomationCatalogModule
    {
        public AutomationModuleId Id => new("tests.unsupported");

        public IEnumerable<IAutomationDefinition> Definitions =>
            [TestDefinition(new("future-source"), new(new(2), new(1)))];
    }
}
