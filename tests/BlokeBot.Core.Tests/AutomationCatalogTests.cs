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
    public async Task RandomNumberConfiguration_DefaultsAndSignedDomainAreExactWithStableFailures()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "random", HostFeatureFlags.Automations);
        var service = new AutomationCatalogService(Catalog(), FeatureService(dbFactory));

        var defaults = (
            await service.ValidatePersistedForSaveAsync(
                new(hostId),
                Persisted("random-number", 1, "{}"),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationConfigurationCheck.Valid>();
        defaults.Configuration.ShouldBe(new AutomationRandomNumberConfiguration(0, 100));

        var domain = (
            await service.ValidatePersistedForSaveAsync(
                new(hostId),
                Persisted(
                    "random-number",
                    1,
                    """{"minimum":-9223372036854775808,"maximum":9223372036854775807}"""
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationConfigurationCheck.Valid>();
        domain.Configuration.ShouldBe(
            new AutomationRandomNumberConfiguration(long.MinValue, long.MaxValue)
        );

        var fractional = (
            await service.ValidatePersistedForSaveAsync(
                new(hostId),
                Persisted("random-number", 1, """{"minimum":0.5,"maximum":1}"""),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationConfigurationCheck.Invalid>();
        fractional
            .Errors.ShouldHaveSingleItem()
            .ShouldBe(
                new(
                    new AutomationValidationTarget.Field(new("minimum")),
                    "Enter an exact whole number without a fractional part."
                )
            );

        var outOfDomain = (
            await service.ValidatePersistedForSaveAsync(
                new(hostId),
                Persisted("random-number", 1, """{"minimum":0,"maximum":9223372036854775808}"""),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationConfigurationCheck.Invalid>();
        outOfDomain
            .Errors.ShouldHaveSingleItem()
            .ShouldBe(
                new(
                    new AutomationValidationTarget.Field(new("maximum")),
                    "Enter a whole number from -9223372036854775808 through 9223372036854775807."
                )
            );

        var reversed = (
            await service.ValidatePersistedForSaveAsync(
                new(hostId),
                Persisted("random-number", 1, """{"minimum":2,"maximum":1}"""),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationConfigurationCheck.Invalid>();
        reversed
            .Errors.ShouldHaveSingleItem()
            .ShouldBe(
                new(
                    new AutomationValidationTarget.Field(new("maximum")),
                    "Maximum must be greater than or equal to minimum."
                )
            );
    }

    [Test]
    public void RandomNumberInclusiveMapping_CoversEndpointsSingletonFullDomainAndRejection()
    {
        var endpoints = new SequenceUInt64Source(1, ulong.MaxValue);
        AutomationInclusiveIntegerMapping.NextInt64Inclusive(endpoints, 0, 100).ShouldBe(0);
        AutomationInclusiveIntegerMapping.NextInt64Inclusive(endpoints, 0, 100).ShouldBe(100);

        var singleton = new SequenceUInt64Source(ulong.MaxValue);
        AutomationInclusiveIntegerMapping.NextInt64Inclusive(singleton, -42, -42).ShouldBe(-42);

        var fullDomain = new SequenceUInt64Source(0, ulong.MaxValue);
        AutomationInclusiveIntegerMapping
            .NextInt64Inclusive(fullDomain, long.MinValue, long.MaxValue)
            .ShouldBe(long.MinValue);
        AutomationInclusiveIntegerMapping
            .NextInt64Inclusive(fullDomain, long.MinValue, long.MaxValue)
            .ShouldBe(long.MaxValue);

        var rejectedLowProduct = new SequenceUInt64Source(0, 1);
        AutomationInclusiveIntegerMapping
            .NextInt64Inclusive(rejectedLowProduct, 0, 100)
            .ShouldBe(0);
        rejectedLowProduct.Calls.ShouldBe(2);
    }

    [Test]
    public void SeededRandomNumber_UsesTheSameDeterministicInclusiveMapping()
    {
        var first = new AutomationSeededIntegerEntropy(0x227UL);
        var second = new AutomationSeededIntegerEntropy(0x227UL);

        var firstSequence = Enumerable
            .Range(0, 32)
            .Select(_ => first.NextInt64Inclusive(long.MinValue, long.MaxValue))
            .ToArray();
        var secondSequence = Enumerable
            .Range(0, 32)
            .Select(_ => second.NextInt64Inclusive(long.MinValue, long.MaxValue))
            .ToArray();

        secondSequence.ShouldBe(firstSequence);
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
        enabled.Definitions.Length.ShouldBe(7);

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
            (AutomationDefinitionIds.ConditionControl, new ConditionControlConfiguration(true)),
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
            Persisted("condition", 1, """{"predicate":true}"""),
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
        extended.Descriptors.Length.ShouldBe(9);
        extended.Descriptors.ShouldContain(static definition =>
            definition.Id == new AutomationDefinitionId("sample-source")
        );
        extended.Descriptors.ShouldContain(static definition =>
            definition.Id == new AutomationDefinitionId("sample-action")
            && definition.Kind == AutomationNodeKind.Action
        );

        _ = Should.Throw<AutomationCatalogRegistrationException>(() =>
            _ = new AutomationDefinitionCatalog([
                new CoreAutomationCatalogModule(),
                new DuplicateAutomationModule(),
            ])
        );
        _ = Should.Throw<AutomationCatalogRegistrationException>(() =>
            _ = new AutomationDefinitionCatalog([new UnsupportedSchemaAutomationModule()])
        );

        var services = new ServiceCollection();
        _ = services
            .AddBlokeBotAutomations()
            .AddAutomationCatalogModule<DuplicateAutomationModule>();
        await using var provider = services.BuildServiceProvider();
        _ = Should.Throw<AutomationCatalogRegistrationException>(() =>
            _ = provider.GetRequiredService<AutomationDefinitionCatalog>()
        );
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

    private sealed class SequenceUInt64Source(params ulong[] values) : IAutomationUInt64Source
    {
        private readonly Queue<ulong> _values = new(values);

        internal int Calls { get; private set; }

        public ulong NextUInt64()
        {
            Calls++;
            return _values.Dequeue();
        }
    }
}
