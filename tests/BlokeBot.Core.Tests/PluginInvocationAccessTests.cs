using System.Data.Common;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Plugins;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PluginInvocationAccessTests
{
    [Test]
    public async Task CurrentContext_UsesTheAdmittedIdentityInsteadOfCallerInput()
    {
        var identity = Identity(Host(7));
        var module = new PluginContextHostModule();
        var callerContext = new PluginInvocationContext.Channel(identity.Plugin, Host(99));

        var outcome = await module.InvokeAsync(
            identity,
            Call(module.Descriptor, 0, callerContext),
            CancellationToken.None
        );

        var returned = outcome.ShouldBeOfType<PluginHostCallOutcome.Returned>();
        var context = Properties(returned.Value.ShouldBeOfType<PluginValue.Map>());
        context["hostId"].ShouldBe(new PluginValue.Number(7));
        context["featureId"].ShouldBe(new PluginValue.String("collection"));
        var command = Properties(context["command"].ShouldBeOfType<PluginValue.Map>());
        command["route"].ShouldBe(new PluginValue.String("link"));
    }

    [Test]
    public async Task SettingsReads_UseTheExactInstallationAndHostFeatureOwners()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostsAsync(database);
        var store = new EfPluginFeatureStore(database, new());
        var declaration = Declaration();
        var protector = new DataProtectionPluginSecretProtector(
            new EphemeralDataProtectionProvider()
        );
        var module = new PluginSettingsHostModule(store, declaration.Registry, protector);
        var plugin = declaration.Declaration.Installation.PluginId;
        var serviceToken = Setting("service-token");
        PluginSecretPlaintext
            .TryCreate("only-plugin-can-read-this", 256, out var secret)
            .ShouldBeTrue();
        await WriteAsync(
            store,
            new PluginConfigurationOwner.Installation(plugin),
            Values(
                new PluginSettingValueEntry(
                    Setting("moderation-mode"),
                    new PluginSettingValue.Choice(Choice("manual"))
                )
            ),
            new(
                [
                    new(
                        serviceToken,
                        protector.Protect(
                            new PluginSecretKey.Installation(plugin, serviceToken),
                            secret
                        )
                    ),
                ],
                []
            )
        );
        await WriteFeatureAsync(store, plugin, host: 1, maximumLinks: 11);
        await WriteFeatureAsync(store, plugin, host: 2, maximumLinks: 22);
        var identity = Identity(Host(2), declaration.Declaration.Installation);

        var installation = await module.InvokeAsync(
            identity,
            Call(module.Descriptor, 0, identity.Context),
            CancellationToken.None
        );
        var feature = await module.InvokeAsync(
            identity,
            Call(module.Descriptor, 1, identity.Context),
            CancellationToken.None
        );

        var installationValues = Properties(
            installation
                .ShouldBeOfType<PluginHostCallOutcome.Returned>()
                .Value.ShouldBeOfType<PluginValue.Map>()
        );
        installationValues["moderation-mode"].ShouldBe(new PluginValue.String("manual"));
        installationValues["service-token"]
            .ShouldBe(new PluginValue.String("only-plugin-can-read-this"));
        var featureValues = Properties(
            feature
                .ShouldBeOfType<PluginHostCallOutcome.Returned>()
                .Value.ShouldBeOfType<PluginValue.Map>()
        );
        featureValues["maximum-links"].ShouldBe(new PluginValue.Number(22));
    }

    [Test]
    public void ProtectedFeatureSecret_CannotBeUnprotectedForAnotherHost()
    {
        var protector = new DataProtectionPluginSecretProtector(
            new EphemeralDataProtectionProvider()
        );
        var plugin = PluginContractFixtures.PluginId("community.link-queue");
        var setting = Setting("channel-token");
        PluginSecretPlaintext.TryCreate("host-one-secret", 256, out var plaintext).ShouldBeTrue();
        var first = new PluginSecretKey.Feature(
            new(plugin, Feature("collection"), Host(1)),
            setting
        );
        var second = new PluginSecretKey.Feature(
            new(plugin, Feature("collection"), Host(2)),
            setting
        );

        var protectedSecret = protector.Protect(first, plaintext);

        _ = protector
            .Unprotect(second, protectedSecret)
            .ShouldBeOfType<PluginSecretUnprotectOutcome.Failed>();
    }

    [Test]
    public async Task SettingsRead_OverlappingAtomicWrite_ReturnsOneConsistentSqliteSnapshot()
    {
        var barrier = new ConfigurationSnapshotBarrier();
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync(barrier);
        await SeedHostsAsync(database);
        var store = new EfPluginFeatureStore(database, new());
        var declaration = Declaration();
        var protector = new DataProtectionPluginSecretProtector(
            new EphemeralDataProtectionProvider()
        );
        var module = new PluginSettingsHostModule(store, declaration.Registry, protector);
        var installation = declaration.Declaration.Installation;
        var owner = new PluginConfigurationOwner.Installation(installation.PluginId);
        var setting = Setting("service-token");
        const string OldToken = "old-token-value";
        const string NewToken = "new-token-value";
        var initial = await store.WriteConfigurationAsync(
            new(
                new(owner, PluginSettingValues.Empty, [], PluginConfigurationRevision.Initial),
                InstallationValues("manual"),
                new(
                    [
                        new(
                            setting,
                            protector.Protect(
                                new PluginSecretKey.Installation(installation.PluginId, setting),
                                Plaintext(OldToken)
                            )
                        ),
                    ],
                    []
                )
            ),
            CancellationToken.None
        );
        var expected = initial.ShouldBeOfType<PluginConfigurationStoreWriteOutcome.Written>().State;
        var identity = Identity(Host(1), installation);
        barrier.Arm();

        var read = module
            .InvokeAsync(
                identity,
                Call(module.Descriptor, 0, identity.Context),
                CancellationToken.None
            )
            .AsTask();
        await barrier.WaitUntilReadPausedAsync();
        var write = Task.Run(async () =>
            await store.WriteConfigurationAsync(
                new(
                    expected,
                    InstallationValues("automatic"),
                    new(
                        [
                            new(
                                setting,
                                protector.Protect(
                                    new PluginSecretKey.Installation(
                                        installation.PluginId,
                                        setting
                                    ),
                                    Plaintext(NewToken)
                                )
                            ),
                        ],
                        []
                    )
                ),
                CancellationToken.None
            )
        );
        await barrier.WaitUntilWriteAttemptedAsync();
        var completedBeforeReadReleased = write.IsCompleted;

        barrier.ReleaseRead();
        var returned = (await read).ShouldBeOfType<PluginHostCallOutcome.Returned>();
        _ = (await write).ShouldBeOfType<PluginConfigurationStoreWriteOutcome.Written>();
        completedBeforeReadReleased.ShouldBeFalse();
        var values = Properties(returned.Value.ShouldBeOfType<PluginValue.Map>());
        var mode = values["moderation-mode"].ShouldBeOfType<PluginValue.String>().Value;
        var token = values["service-token"].ShouldBeOfType<PluginValue.String>().Value;
        var completeOld = mode == "manual" && token == OldToken;
        var completeNew = mode == "automatic" && token == NewToken;
        (completeOld || completeNew).ShouldBeTrue();
    }

    private static async Task WriteFeatureAsync(
        IPluginFeatureStore store,
        PluginId plugin,
        int host,
        long maximumLinks
    )
    {
        var owner = new PluginConfigurationOwner.Feature(
            new(plugin, Feature("collection"), Host(host))
        );
        await WriteAsync(
            store,
            owner,
            Values(
                new PluginSettingValueEntry(
                    Setting("collect-messages"),
                    new PluginSettingValue.Boolean(true)
                ),
                new PluginSettingValueEntry(
                    Setting("chat-command"),
                    new PluginSettingValue.Text("link")
                ),
                new PluginSettingValueEntry(
                    Setting("maximum-links"),
                    new PluginSettingValue.Integer(maximumLinks)
                ),
                new PluginSettingValueEntry(
                    Setting("minimum-score"),
                    new PluginSettingValue.Number(1.0m)
                ),
                new PluginSettingValueEntry(
                    Setting("wait-between-links"),
                    new PluginSettingValue.Duration(60)
                )
            ),
            PluginSecretChanges.Empty
        );
    }

    private static async Task SeedHostsAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = database.CreateDbContext();
        for (var host = 1; host <= 2; host++)
        {
            _ = db.Hosts.Add(
                new BotHost
                {
                    TwitchUserId = $"host-{host}",
                    Login = $"host{host}",
                    DisplayName = $"Host {host}",
                    CreatedAtUtc = DateTime.UtcNow,
                }
            );
        }
        _ = await db.SaveChangesAsync();
    }

    private static async Task WriteAsync(
        IPluginFeatureStore store,
        PluginConfigurationOwner owner,
        PluginSettingValues values,
        PluginSecretChanges secrets
    )
    {
        var expected = new PluginConfigurationState(
            owner,
            PluginSettingValues.Empty,
            [],
            PluginConfigurationRevision.Initial
        );
        _ = (
            await store.WriteConfigurationAsync(
                new(expected, values, secrets),
                CancellationToken.None
            )
        ).ShouldBeOfType<PluginConfigurationStoreWriteOutcome.Written>();
    }

    private static (
        PluginFeatureDeclarationRegistry Registry,
        PluginFeatureDeclaration Declaration
    ) Declaration()
    {
        var manifest = (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestToml.Validate(
                    PluginContractFixtures.CompleteManifestToml(),
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;
        var registry = new PluginFeatureDeclarationRegistry();
        registry.Publish(manifest, Fence());
        return (registry, registry.Current.Declarations[manifest.Manifest.Id]);
    }

    private static PluginWorkerInvocationIdentity Identity(
        PluginHostId host,
        PluginInstallationIdentity? installation = null
    )
    {
        installation ??= new(
            PluginContractFixtures.PluginId("community.link-queue"),
            new(PluginContractFixtures.SemanticVersion("1.2.0"), Tag("community-link-queue"))
        );
        var context = new PluginInvocationContext.Channel(
            installation,
            host,
            new("viewer", "Viewer", "viewer-1", false, false, false),
            Command: new("link", ["https://example.invalid"])
        );
        return new(
            installation,
            Feature("collection"),
            host,
            context,
            InvocationId(),
            CoroutineId(),
            WorkerGeneration(),
            PluginWorkerDeadline.From(DateTimeOffset.UtcNow.AddMinutes(1)),
            CancellationId()
        );
    }

    private static PluginHostCall Call(
        PluginHostModuleDescriptor module,
        int operation,
        PluginInvocationContext context
    ) => new(CallId(), CoroutineId(), module.Id, module.Operations[operation].Id, context, []);

    private static IReadOnlyDictionary<string, PluginValue> Properties(PluginValue.Map map) =>
        map.Properties.ToDictionary(
            static property => property.Name,
            static property => property.Value
        );

    private static PluginSettingValues Values(params PluginSettingValueEntry[] entries) =>
        PluginSettingValues.Create(entries) is PluginSettingValuesOutcome.Created created
            ? created.Values
            : throw new InvalidOperationException("Duplicate setting fixture.");

    private static PluginSettingValues InstallationValues(string mode) =>
        Values(
            new PluginSettingValueEntry(
                Setting("moderation-mode"),
                new PluginSettingValue.Choice(Choice(mode))
            )
        );

    private static PluginSecretPlaintext Plaintext(string value) =>
        PluginSecretPlaintext.TryCreate(value, 256, out var plaintext)
            ? plaintext
            : throw new InvalidOperationException("Invalid secret fixture.");

    private static PluginSettingId Setting(string value) =>
        PluginSettingId.TryCreate(value, out var setting)
            ? setting
            : throw new InvalidOperationException("Invalid setting fixture.");

    private static PluginSettingChoiceId Choice(string value) =>
        PluginSettingChoiceId.TryCreate(value, out var choice)
            ? choice
            : throw new InvalidOperationException("Invalid choice fixture.");

    private static PluginFeatureId Feature(string value) =>
        PluginFeatureId.TryCreate(value, out var feature)
            ? feature
            : throw new InvalidOperationException("Invalid feature fixture.");

    private static PluginHostId Host(int value) =>
        PluginHostId.TryCreate(value, out var host)
            ? host
            : throw new InvalidOperationException("Invalid host fixture.");

    private static PluginGitTag Tag(string value) =>
        PluginGitTag.TryCreate(value, out var tag)
            ? tag
            : throw new InvalidOperationException("Invalid tag fixture.");

    private static PluginLifecycleFence Fence() =>
        new(PluginLifecycleOperationId.New(), WorkerGeneration());

    private static PluginWorkerGeneration WorkerGeneration() =>
        PluginWorkerGeneration.TryCreate(1, out var generation)
            ? generation
            : throw new InvalidOperationException("Invalid worker generation fixture.");

    private static PluginHostCallId CallId() =>
        PluginHostCallId.TryCreate(Guid.NewGuid(), out var id)
            ? id
            : throw new InvalidOperationException("Invalid host call fixture.");

    private static PluginWorkerInvocationId InvocationId() =>
        PluginWorkerInvocationId.TryCreate(Guid.NewGuid(), out var id)
            ? id
            : throw new InvalidOperationException("Invalid invocation fixture.");

    private static PluginCoroutineId CoroutineId() =>
        PluginCoroutineId.TryCreate(Guid.NewGuid(), out var id)
            ? id
            : throw new InvalidOperationException("Invalid coroutine fixture.");

    private static PluginWorkerCancellationId CancellationId() =>
        PluginWorkerCancellationId.TryCreate(Guid.NewGuid(), out var id)
            ? id
            : throw new InvalidOperationException("Invalid cancellation fixture.");

    private sealed class ConfigurationSnapshotBarrier
        : DbCommandInterceptor,
            IDbConnectionInterceptor
    {
        private readonly TaskCompletionSource _readPaused = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _readReleased = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _writeAttempted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _armed;
        private int _connectionsOpened;
        private int _intercepted;

        internal void Arm() => _ = Interlocked.Exchange(ref _armed, 1);

        internal async Task WaitUntilReadPausedAsync() =>
            await _readPaused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        internal async Task WaitUntilWriteAttemptedAsync() =>
            await _writeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        internal void ReleaseRead() => _ = _readReleased.TrySetResult();

        public Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            ((SqliteConnection)connection).DefaultTimeout = 10;
            if (
                Volatile.Read(ref _armed) == 1
                && Interlocked.Increment(ref _connectionsOpened) == 2
            )
            {
                _ = _writeAttempted.TrySetResult();
            }
            return Task.CompletedTask;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Volatile.Read(ref _armed) == 1
                && command.CommandText.Contains(
                    "FROM \"plugin_installation_secrets\"",
                    StringComparison.Ordinal
                )
                && Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0
            )
            {
                _ = _readPaused.TrySetResult();
                await _readReleased.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
