using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Plugins;

public sealed partial class EfPluginFeatureStore
{
    private static async Task ApplyInstallationSecretsAsync(
        BlokeBotDbContext db,
        PluginConfigurationOwner.Installation owner,
        PluginSecretChanges changes,
        CancellationToken cancellationToken
    )
    {
        foreach (var replacement in changes.Replacements)
        {
            var record = await db.PluginInstallationSecrets.SingleOrDefaultAsync(
                value =>
                    value.PluginId == owner.PluginId.Value
                    && value.SettingId == replacement.SettingId.Value,
                cancellationToken
            );
            if (record is null)
            {
                record = new()
                {
                    PluginId = owner.PluginId.Value,
                    SettingId = replacement.SettingId.Value,
                };
                _ = db.PluginInstallationSecrets.Add(record);
            }
            record.ProtectedValue = replacement.Value.Bytes.ToArray();
        }

        foreach (var settingId in changes.Clears)
        {
            var record = await db.PluginInstallationSecrets.SingleOrDefaultAsync(
                value =>
                    value.PluginId == owner.PluginId.Value && value.SettingId == settingId.Value,
                cancellationToken
            );
            if (record is not null)
            {
                _ = db.PluginInstallationSecrets.Remove(record);
            }
        }
    }

    private static async Task ApplyFeatureSecretsAsync(
        BlokeBotDbContext db,
        PluginConfigurationOwner.Feature owner,
        PluginSecretChanges changes,
        CancellationToken cancellationToken
    )
    {
        var key = owner.Key;
        foreach (var replacement in changes.Replacements)
        {
            var record = await db.PluginFeatureSecrets.SingleOrDefaultAsync(
                value =>
                    value.PluginId == key.PluginId.Value
                    && value.FeatureId == key.FeatureId.Value
                    && value.HostId == key.HostId.Value
                    && value.SettingId == replacement.SettingId.Value,
                cancellationToken
            );
            if (record is null)
            {
                record = new()
                {
                    PluginId = key.PluginId.Value,
                    FeatureId = key.FeatureId.Value,
                    HostId = key.HostId.Value,
                    SettingId = replacement.SettingId.Value,
                };
                _ = db.PluginFeatureSecrets.Add(record);
            }
            record.ProtectedValue = replacement.Value.Bytes.ToArray();
        }

        foreach (var settingId in changes.Clears)
        {
            var record = await db.PluginFeatureSecrets.SingleOrDefaultAsync(
                value =>
                    value.PluginId == key.PluginId.Value
                    && value.FeatureId == key.FeatureId.Value
                    && value.HostId == key.HostId.Value
                    && value.SettingId == settingId.Value,
                cancellationToken
            );
            if (record is not null)
            {
                _ = db.PluginFeatureSecrets.Remove(record);
            }
        }
    }
}
