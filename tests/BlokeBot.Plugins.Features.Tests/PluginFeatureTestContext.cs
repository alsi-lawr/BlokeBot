using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Plugins;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Runtime;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Plugins.Features.Tests;

internal sealed class PluginFeatureTestContext : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private PluginFeatureTestContext(
        string databasePath,
        TestDbFactory database,
        ServiceProvider services
    )
    {
        DatabasePath = databasePath;
        Database = database;
        _services = services;
        Store = new(database, new());
        Declarations = new();
        Snapshots = new();
    }

    internal string DatabasePath { get; }
    internal TestDbFactory Database { get; }
    internal EfPluginFeatureStore Store { get; }
    internal PluginFeatureDeclarationRegistry Declarations { get; }
    internal PluginFeatureSnapshotRegistry Snapshots { get; }
    internal PluginLifecycleSerialization Serialization { get; } = new();

    internal static async Task<PluginFeatureTestContext> CreateAsync(int hostCount = 2)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-plugin-features-{Guid.NewGuid():N}.db"
        );
        var database = new TestDbFactory(path);
        await using (var db = database.CreateDbContext())
        {
            await db.Database.MigrateAsync();
            for (var index = 1; index <= hostCount; index++)
            {
                _ = db.Hosts.Add(
                    new BotHost
                    {
                        TwitchUserId = $"host-{index}",
                        Login = $"host{index}",
                        DisplayName = $"Host {index}",
                        CreatedAtUtc = DateTime.UtcNow,
                    }
                );
            }
            _ = await db.SaveChangesAsync();
        }

        var services = new ServiceCollection()
            .AddDataProtection()
            .UseEphemeralDataProtectionProvider()
            .Services.BuildServiceProvider();
        return new(path, database, services);
    }

    internal PluginFeatureManager Manager(
        IPluginFeatureLifecycleHealth lifecycle,
        IPluginCoreDependencyChecker dependencies,
        IPluginFeatureReconciler reconciler,
        IPluginFeatureStore? store = null
    ) =>
        new(
            store ?? Store,
            Declarations,
            lifecycle,
            dependencies,
            reconciler,
            new DataProtectionPluginSecretProtector(
                _services.GetRequiredService<IDataProtectionProvider>()
            ),
            new(),
            new(),
            Snapshots,
            Serialization
        );

    internal PluginFeatureDeclaration Publish(
        PluginLifecycleFence? fence = null,
        string? version = null
    )
    {
        var validated = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestToml.Validate(
                    PluginContractFixtures.CompleteManifestToml(),
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;
        if (version is not null)
        {
            SemanticVersion.TryCreate(version, out var releaseVersion).ShouldBeTrue();
            validated = (
                (PluginManifestValidationOutcome.Accepted)
                    PluginManifestValidator.Validate(
                        validated.Manifest with
                        {
                            Release = validated.Manifest.Release with
                            {
                                DeclaredVersion = releaseVersion,
                            },
                        },
                        PluginContractFixtures.CompatibleHost()
                    )
            ).Manifest;
        }
        var currentFence = fence ?? Fence();
        Declarations.Publish(validated, currentFence);
        return Declarations.Current.Declarations[validated.Manifest.Id];
    }

    internal static PluginFeatureKey Key(string featureId, int hostId = 1)
    {
        PluginFeatureId.TryCreate(featureId, out var feature).ShouldBeTrue();
        PluginHostId.TryCreate(hostId, out var host).ShouldBeTrue();
        return new(PluginContractFixtures.PluginId("community.link-queue"), feature, host);
    }

    internal static PluginLifecycleFence Fence(ulong generation = 1)
    {
        PluginWorkerGeneration.TryCreate(generation, out var workerGeneration).ShouldBeTrue();
        return new(PluginLifecycleOperationId.New(), workerGeneration);
    }

    internal static PluginSettingValues InstallationValues() =>
        Values(Entry("moderation-mode", new PluginSettingValue.Choice(Choice("manual"))));

    internal static PluginSettingValues CollectionValues(
        long maximumLinks = 25,
        string command = "link"
    ) =>
        Values(
            Entry("collect-messages", new PluginSettingValue.Boolean(true)),
            Entry("chat-command", new PluginSettingValue.Text(command)),
            Entry("queue-note", new PluginSettingValue.Text("Review links first.")),
            Entry("maximum-links", new PluginSettingValue.Integer(maximumLinks)),
            Entry("minimum-score", new PluginSettingValue.Number(2.5m)),
            Entry("wait-between-links", new PluginSettingValue.Duration(30))
        );

    internal static PluginSettingValues PublishingValues(string value = "18:00") =>
        Values(Entry("publish-time", new PluginSettingValue.Text(value)));

    internal static PluginSecretUpdateEntry SecretReplacement(string value)
    {
        PluginSettingId.TryCreate("service-token", out var id).ShouldBeTrue();
        PluginSecretPlaintext.TryCreate(value, 256, out var secret).ShouldBeTrue();
        return new(id, new PluginSecretUpdate.Replace(secret));
    }

    internal static PluginSettingValueEntry Entry(string id, PluginSettingValue value)
    {
        PluginSettingId.TryCreate(id, out var settingId).ShouldBeTrue();
        return new(settingId, value);
    }

    private static PluginSettingValues Values(params PluginSettingValueEntry[] entries) =>
        ((PluginSettingValuesOutcome.Created)PluginSettingValues.Create(entries)).Values;

    private static PluginSettingChoiceId Choice(string value)
    {
        PluginSettingChoiceId.TryCreate(value, out var choice).ShouldBeTrue();
        return choice;
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        if (File.Exists(DatabasePath))
        {
            File.Delete(DatabasePath);
        }
    }

    internal sealed class TestDbFactory(string path) : IDbContextFactory<BlokeBotDbContext>
    {
        private readonly DbContextOptions<BlokeBotDbContext> _options =
            new DbContextOptionsBuilder<BlokeBotDbContext>()
                .UseSqlite($"Data Source={path};Default Timeout=10")
                .AddInterceptors(new WeeklyAnnouncementMigrationInterceptor())
                .Options;

        public BlokeBotDbContext CreateDbContext() => new(_options);
    }
}

internal sealed class HealthyLifecycle : IPluginFeatureLifecycleHealth
{
    public bool IsCurrent(PluginFeatureDeclaration declaration) => true;

    public bool IsHealthy(PluginFeatureDeclaration declaration) => true;
}

internal sealed class FaultedLifecycle : IPluginFeatureLifecycleHealth
{
    public bool IsCurrent(PluginFeatureDeclaration declaration) => true;

    public bool IsHealthy(PluginFeatureDeclaration declaration) => false;
}

internal sealed class RemovedLifecycle : IPluginFeatureLifecycleHealth
{
    public bool IsCurrent(PluginFeatureDeclaration declaration) => false;

    public bool IsHealthy(PluginFeatureDeclaration declaration) => false;
}

internal sealed class MutableLifecycleHealth : IPluginFeatureLifecycleHealth
{
    internal bool Healthy { get; set; } = true;

    public bool IsCurrent(PluginFeatureDeclaration declaration) => true;

    public bool IsHealthy(PluginFeatureDeclaration declaration) => Healthy;
}

internal sealed class AvailableDependencies : IPluginCoreDependencyChecker
{
    public PluginCoreDependencyStatus Check(
        IReadOnlyList<PluginHostModuleRequirement> requirements
    ) => new PluginCoreDependencyStatus.Available();
}

internal sealed class MissingDependencies : IPluginCoreDependencyChecker
{
    public PluginCoreDependencyStatus Check(
        IReadOnlyList<PluginHostModuleRequirement> requirements
    ) => new PluginCoreDependencyStatus.Missing(requirements.Select(item => item.Id).ToArray());
}

internal sealed class QueueReconciler(params PluginFeatureReconciliationResult[] results)
    : IPluginFeatureReconciler
{
    private readonly Queue<PluginFeatureReconciliationResult> _results = new(results);

    internal List<CancellationToken> CancellationTokens { get; } = [];

    public ValueTask<PluginFeatureReconciliationResult> ReconcileAsync(
        PluginFeatureReconciliationRequest request,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(_results.Dequeue());

    public ValueTask CancelAsync(
        PluginFeatureKey key,
        PluginLifecycleFence fence,
        PluginFeatureGeneration generation,
        CancellationToken cancellationToken
    )
    {
        CancellationTokens.Add(cancellationToken);
        return ValueTask.CompletedTask;
    }
}
