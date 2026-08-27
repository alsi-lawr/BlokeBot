using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Plugins;

public sealed partial class EfPluginFeatureStore
{
    private async ValueTask<IReadOnlyList<PluginProtectedSecretEntry>> LoadInstallationSecretsAsync(
        BlokeBotDbContext db,
        PluginConfigurationOwner.Installation owner,
        CancellationToken cancellationToken
    )
    {
        var records = await db
            .PluginInstallationSecrets.AsNoTracking()
            .Where(value => value.PluginId == owner.PluginId.Value)
            .Select(value => new { value.SettingId, value.ProtectedValue })
            .ToArrayAsync(cancellationToken);
        return records.Select(record => Entry(record.SettingId, record.ProtectedValue)).ToArray();
    }

    private async ValueTask<IReadOnlyList<PluginProtectedSecretEntry>> LoadFeatureSecretsAsync(
        BlokeBotDbContext db,
        PluginConfigurationOwner.Feature owner,
        CancellationToken cancellationToken
    )
    {
        var key = owner.Key;
        var records = await db
            .PluginFeatureSecrets.AsNoTracking()
            .Where(value =>
                value.PluginId == key.PluginId.Value
                && value.FeatureId == key.FeatureId.Value
                && value.HostId == key.HostId.Value
            )
            .Select(value => new { value.SettingId, value.ProtectedValue })
            .ToArrayAsync(cancellationToken);
        return records.Select(record => Entry(record.SettingId, record.ProtectedValue)).ToArray();
    }

    private static PluginProtectedSecretEntry Entry(string settingId, byte[] protectedValue) =>
        PluginSettingId.TryCreate(settingId, out var parsed)
            ? new(parsed, new(protectedValue))
            : throw new InvalidOperationException("Persisted plugin secret identity is invalid.");
}
