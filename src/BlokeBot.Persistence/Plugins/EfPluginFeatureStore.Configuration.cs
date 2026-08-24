using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Plugins;

public sealed partial class EfPluginFeatureStore
{
    public async ValueTask<PluginConfigurationStoreWriteOutcome> WriteConfigurationAsync(
        PluginConfigurationStoreWrite write,
        CancellationToken cancellationToken
    )
    {
        var encoded = codec.Encode(write.Values);
        if (encoded is not PluginSettingValuesEncodingOutcome.Encoded values)
        {
            throw new ArgumentException("Plugin settings exceed the storage bound.", nameof(write));
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            await write.Expected.Owner.Match(
                installation =>
                    WriteInstallationAsync(db, installation, write, values.Json, cancellationToken),
                feature =>
                    WriteFeatureConfigurationAsync(
                        db,
                        feature,
                        write,
                        values.Json,
                        cancellationToken
                    )
            );
            _ = await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new PluginConfigurationStoreWriteOutcome.Conflict(
                await LoadConfigurationAsync(write.Expected.Owner, cancellationToken)
            );
        }
        catch (DbUpdateException exception) when (UniqueConstraint(exception))
        {
            return new PluginConfigurationStoreWriteOutcome.Conflict(
                await LoadConfigurationAsync(write.Expected.Owner, cancellationToken)
            );
        }

        return new PluginConfigurationStoreWriteOutcome.Written(
            await LoadConfigurationAsync(write.Expected.Owner, cancellationToken)
        );
    }

    private async ValueTask<PluginConfigurationState> LoadInstallationAsync(
        PluginConfigurationOwner.Installation owner,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db
            .PluginInstallationConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.PluginId == owner.PluginId.Value,
                cancellationToken
            );
        var secrets = await db
            .PluginInstallationSecrets.AsNoTracking()
            .Where(value => value.PluginId == owner.PluginId.Value)
            .Select(value => value.SettingId)
            .ToListAsync(cancellationToken);
        return Configuration(owner, record?.ValuesJson, record?.Revision, secrets);
    }

    private async ValueTask<PluginConfigurationState> LoadFeatureConfigurationAsync(
        PluginConfigurationOwner.Feature owner,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var key = owner.Key;
        var record = await db
            .PluginFeatureConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.PluginId == key.PluginId.Value
                    && value.FeatureId == key.FeatureId.Value
                    && value.HostId == key.HostId.Value,
                cancellationToken
            );
        var secrets = await db
            .PluginFeatureSecrets.AsNoTracking()
            .Where(value =>
                value.PluginId == key.PluginId.Value
                && value.FeatureId == key.FeatureId.Value
                && value.HostId == key.HostId.Value
            )
            .Select(value => value.SettingId)
            .ToListAsync(cancellationToken);
        return Configuration(owner, record?.ValuesJson, record?.Revision, secrets);
    }

    private PluginConfigurationState Configuration(
        PluginConfigurationOwner owner,
        string? json,
        long? revisionValue,
        IEnumerable<string> secretIds
    )
    {
        var revision =
            revisionValue is { } value
            && PluginConfigurationRevision.TryCreate(value, out var parsed)
                ? parsed
                : PluginConfigurationRevision.Initial;
        var secrets = secretIds.Select(SecretPresence).ToArray();
        return new(
            owner,
            json is null ? PluginSettingValues.Empty : Decode(json),
            secrets,
            revision
        );
    }

    private static PluginSecretPresence SecretPresence(string value) =>
        PluginSettingId.TryCreate(value, out var settingId)
            ? new(settingId, true)
            : throw new InvalidOperationException("Persisted plugin secret identity is invalid.");

    private static async Task WriteInstallationAsync(
        BlokeBotDbContext db,
        PluginConfigurationOwner.Installation owner,
        PluginConfigurationStoreWrite write,
        string json,
        CancellationToken cancellationToken
    )
    {
        var record = await db.PluginInstallationConfigurations.SingleOrDefaultAsync(
            value => value.PluginId == owner.PluginId.Value,
            cancellationToken
        );
        RequireRevision(record?.Revision, write.Expected.Revision);
        if (record is null)
        {
            record = new() { PluginId = owner.PluginId.Value };
            _ = db.PluginInstallationConfigurations.Add(record);
        }
        record.ValuesJson = json;
        record.Revision = checked(write.Expected.Revision.Value + 1);
        await ApplyInstallationSecretsAsync(db, owner, write.Secrets, cancellationToken);
    }

    private static async Task WriteFeatureConfigurationAsync(
        BlokeBotDbContext db,
        PluginConfigurationOwner.Feature owner,
        PluginConfigurationStoreWrite write,
        string json,
        CancellationToken cancellationToken
    )
    {
        var key = owner.Key;
        var record = await db.PluginFeatureConfigurations.SingleOrDefaultAsync(
            value =>
                value.PluginId == key.PluginId.Value
                && value.FeatureId == key.FeatureId.Value
                && value.HostId == key.HostId.Value,
            cancellationToken
        );
        RequireRevision(record?.Revision, write.Expected.Revision);
        if (record is null)
        {
            record = new()
            {
                PluginId = key.PluginId.Value,
                FeatureId = key.FeatureId.Value,
                HostId = key.HostId.Value,
            };
            _ = db.PluginFeatureConfigurations.Add(record);
        }
        record.ValuesJson = json;
        record.Revision = checked(write.Expected.Revision.Value + 1);
        await ApplyFeatureSecretsAsync(db, owner, write.Secrets, cancellationToken);
    }

    private static void RequireRevision(long? actual, PluginConfigurationRevision expected)
    {
        if ((actual ?? 0) != expected.Value)
        {
            throw new DbUpdateConcurrencyException();
        }
    }

    private static bool UniqueConstraint(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: 19 };
}
