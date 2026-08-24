using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Simulation;

internal sealed class SimulationPluginFeatureScenario(
    PluginFeatureManager manager,
    IPluginFeatureStore store,
    IPluginFeatureDeclarationPublisher declarations,
    PluginFeatureSnapshotRegistry snapshots
) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PluginFeatureKey? _collection;

    public async Task SeedAsync(int hostId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var manifest = SimulationPluginFeatureManifest.Load();
            var fence = Fence();
            declarations.Publish(manifest, fence);
            _ = PluginHostId.TryCreate(hostId, out var pluginHostId);
            _collection = new(manifest.Manifest.Id, Feature("collection"), pluginHostId);

            var installation = await SaveInstallationAsync(manifest, cancellationToken);
            var collection = await SaveFeatureAsync(
                new(_collection),
                CollectionValues(),
                cancellationToken
            );
            var publishingKey = new PluginFeatureKey(
                manifest.Manifest.Id,
                Feature("publishing"),
                pluginHostId
            );
            var publishing = await SaveFeatureAsync(
                new(publishingKey),
                Values(Entry("publish-time", new PluginSettingValue.Text("18:30"))),
                cancellationToken
            );
            await EnsureStateAsync(
                _collection,
                fence,
                installation.Revision,
                collection.Revision,
                cancellationToken
            );
            await EnsureStateAsync(
                publishingKey,
                fence,
                installation.Revision,
                publishing.Revision,
                cancellationToken
            );
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task<bool> SetReadinessAsync(string value, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_collection is null || Readiness(value) is not { } readiness)
            {
                return false;
            }
            var current = await store.LoadFeatureStateAsync(_collection, cancellationToken);
            if (
                current is null
                || !PluginFeatureRevision.TryCreate(current.Revision.Value + 1, out var revision)
            )
            {
                return false;
            }
            var written = await store.WriteFeatureStateAsync(
                current,
                current with
                {
                    Readiness = readiness,
                    Revision = revision,
                },
                cancellationToken
            );
            if (written is not PluginFeatureStateStoreWriteOutcome.Written updated)
            {
                return false;
            }
            snapshots.Publish(updated.State);
            return true;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private async Task<PluginConfigurationState> SaveInstallationAsync(
        ValidatedPluginManifest manifest,
        CancellationToken cancellationToken
    )
    {
        var owner = new PluginConfigurationOwner.Installation(manifest.Manifest.Id);
        var current = await LoadAsync(owner, cancellationToken);
        if (current.Revision != PluginConfigurationRevision.Initial)
        {
            return current;
        }
        _ = PluginSecretPlaintext.TryCreate("simulation-secret", 256, out var secret);
        return await SaveAsync(
            current,
            Values(Entry("moderation-mode", new PluginSettingValue.Choice(Choice("manual")))),
            [new(Setting("service-token"), new PluginSecretUpdate.Replace(secret))],
            cancellationToken
        );
    }

    private async Task<PluginConfigurationState> SaveFeatureAsync(
        PluginConfigurationOwner.Feature owner,
        PluginSettingValues values,
        CancellationToken cancellationToken
    )
    {
        var current = await LoadAsync(owner, cancellationToken);
        return current.Revision == PluginConfigurationRevision.Initial
            ? await SaveAsync(current, values, [], cancellationToken)
            : current;
    }

    private async Task EnsureStateAsync(
        PluginFeatureKey key,
        PluginLifecycleFence fence,
        PluginConfigurationRevision installationRevision,
        PluginConfigurationRevision featureRevision,
        CancellationToken cancellationToken
    )
    {
        var current = await store.LoadFeatureStateAsync(key, cancellationToken);
        if (current is not null)
        {
            snapshots.Publish(current);
            return;
        }
        _ = PluginFeatureGeneration.TryCreate(1, out var generation);
        _ = PluginFeatureRevision.TryCreate(1, out var revision);
        var state = new PluginFeatureState(
            key,
            fence,
            generation,
            new PluginFeatureReadiness.Disabled(),
            revision
        );
        if (
            await store.EnableAsync(
                new(null, state, installationRevision, featureRevision),
                cancellationToken
            )
            is PluginFeatureEnableStoreOutcome.Enabled enabled
        )
        {
            snapshots.Publish(enabled.State);
        }
    }

    private async Task<PluginConfigurationState> LoadAsync(
        PluginConfigurationOwner owner,
        CancellationToken cancellationToken
    ) =>
        await manager.LoadConfigurationAsync(owner, cancellationToken)
            is PluginConfigurationLoadOutcome.Loaded loaded
            ? loaded.Configuration
            : throw new InvalidOperationException("The simulation plugin is not declared.");

    private async Task<PluginConfigurationState> SaveAsync(
        PluginConfigurationState current,
        PluginSettingValues values,
        IReadOnlyList<PluginSecretUpdateEntry> secrets,
        CancellationToken cancellationToken
    ) =>
        await manager.SaveConfigurationAsync(
            new(current.Owner, current.Revision, values, secrets),
            cancellationToken
        )
            is PluginConfigurationSaveOutcome.Saved saved
            ? saved.Configuration
            : throw new InvalidOperationException("The simulation plugin settings are invalid.");

    private static PluginSettingValues CollectionValues() =>
        Values(
            Entry("collect-messages", new PluginSettingValue.Boolean(true)),
            Entry("chat-command", new PluginSettingValue.Text("!link")),
            Entry("queue-note", new PluginSettingValue.Text("Links are reviewed before use.")),
            Entry("maximum-links", new PluginSettingValue.Integer(40)),
            Entry("minimum-score", new PluginSettingValue.Number(4.5m)),
            Entry("wait-between-links", new PluginSettingValue.Duration(30))
        );

    private static PluginFeatureReadiness? Readiness(string value) =>
        value switch
        {
            "disabled" => new PluginFeatureReadiness.Disabled(),
            "degraded" => new PluginFeatureReadiness.EnabledDegraded(DegradedReason()),
            "ready" => new PluginFeatureReadiness.Ready(),
            _ => null,
        };

    private static PluginReadinessReason DegradedReason() =>
        PluginReadinessReason.TryCreate(
            PluginReadinessReasonCode.MissingScopes,
            PluginRecoveryAction.ReconnectTwitch,
            "Reconnect Twitch to grant the missing channel scope.",
            out var reason
        )
            ? reason
            : throw new InvalidOperationException("The simulation recovery reason is invalid.");

    private static PluginLifecycleFence Fence()
    {
        _ = PluginLifecycleOperationId.TryCreate(
            Guid.Parse("24700000-0000-4000-8000-000000000001"),
            out var operation
        );
        _ = PluginWorkerGeneration.TryCreate(1, out var generation);
        return new(operation, generation);
    }

    private static PluginSettingValues Values(params PluginSettingValueEntry[] entries) =>
        PluginSettingValues.Create(entries) is PluginSettingValuesOutcome.Created created
            ? created.Values
            : throw new InvalidOperationException("The simulation plugin values are invalid.");

    private static PluginSettingValueEntry Entry(string id, PluginSettingValue value) =>
        new(Setting(id), value);

    private static PluginSettingId Setting(string value) =>
        PluginSettingId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("The simulation setting ID is invalid.");

    private static PluginSettingChoiceId Choice(string value) =>
        PluginSettingChoiceId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("The simulation choice ID is invalid.");

    private static PluginFeatureId Feature(string value) =>
        PluginFeatureId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("The simulation feature ID is invalid.");

    public void Dispose() => _gate.Dispose();
}
