using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Plugins;

public sealed partial class EfPluginFeatureStore(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    PluginSettingValuesCodec codec
) : IPluginFeatureStore
{
    public ValueTask<PluginConfigurationState> LoadConfigurationAsync(
        PluginConfigurationOwner owner,
        CancellationToken cancellationToken
    ) =>
        owner.Match(
            installation => LoadInstallationAsync(installation, cancellationToken),
            feature => LoadFeatureConfigurationAsync(feature, cancellationToken)
        );

    public async ValueTask<PluginFeatureState?> LoadFeatureStateAsync(
        PluginFeatureKey key,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db
            .PluginFeatureStates.AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.PluginId == key.PluginId.Value
                    && value.FeatureId == key.FeatureId.Value
                    && value.HostId == key.HostId.Value,
                cancellationToken
            );
        return record is null ? null : PluginFeatureRecordMapper.ToDomain(record);
    }

    public async ValueTask<IReadOnlyList<PluginFeatureState>> LoadFeatureStatesAsync(
        PluginId? pluginId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.PluginFeatureStates.AsNoTracking();
        if (pluginId is not null)
        {
            query = query.Where(value => value.PluginId == pluginId.Value);
        }
        return (await query.ToListAsync(cancellationToken))
            .Select(PluginFeatureRecordMapper.ToDomain)
            .ToArray();
    }

    public async ValueTask PurgeAsync(PluginId pluginId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        _ = await db
            .PluginFeatureStates.Where(value => value.PluginId == pluginId.Value)
            .ExecuteDeleteAsync(cancellationToken);
        _ = await db
            .PluginAutomationInstantiations.Where(value => value.PluginId == pluginId.Value)
            .ExecuteDeleteAsync(cancellationToken);
        _ = await db
            .PluginFeatureConfigurations.Where(value => value.PluginId == pluginId.Value)
            .ExecuteDeleteAsync(cancellationToken);
        _ = await db
            .PluginInstallationConfigurations.Where(value => value.PluginId == pluginId.Value)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<bool> HasFormat1IncompatibleStateAsync(
        PluginHostId hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.PluginInstallationConfigurations.AnyAsync(cancellationToken)
            || await db.PluginInstallationSecrets.AnyAsync(cancellationToken)
            || await db.PluginFeatureConfigurations.AnyAsync(
                value => value.HostId == hostId.Value,
                cancellationToken
            )
            || await db.PluginFeatureSecrets.AnyAsync(
                value => value.HostId == hostId.Value,
                cancellationToken
            )
            || await db.PluginFeatureStates.AnyAsync(
                value => value.HostId == hostId.Value,
                cancellationToken
            )
            || await db.PluginAutomationInstantiations.AnyAsync(
                value => value.HostId == hostId.Value,
                cancellationToken
            );
    }

    private PluginSettingValues Decode(string json) =>
        codec.Decode(json) is PluginSettingValuesDecodingOutcome.Decoded decoded
            ? decoded.Values
            : throw new InvalidOperationException("Persisted plugin settings JSON is invalid.");
}
