using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public sealed partial class PluginFeatureManager
{
    public async ValueTask<PluginConfigurationLoadOutcome> LoadConfigurationAsync(
        PluginConfigurationOwner owner,
        CancellationToken cancellationToken
    )
    {
        var declaration = Declaration(owner);
        return declaration is null || !OwnerDeclared(declaration, owner)
            ? new PluginConfigurationLoadOutcome.NotDeclared()
            : new PluginConfigurationLoadOutcome.Loaded(
                declaration,
                await store.LoadConfigurationAsync(owner, cancellationToken)
            );
    }

    public async ValueTask<PluginConfigurationSaveOutcome> SaveConfigurationAsync(
        PluginConfigurationSaveRequest request,
        CancellationToken cancellationToken
    )
    {
        await using var lease = await lifecycleSerialization.AcquireAsync(
            PluginIdFor(request.Owner),
            cancellationToken
        );
        return await SaveConfigurationCoreAsync(request, cancellationToken);
    }

    private async ValueTask<PluginConfigurationSaveOutcome> SaveConfigurationCoreAsync(
        PluginConfigurationSaveRequest request,
        CancellationToken cancellationToken
    )
    {
        var declaration = Declaration(request.Owner);
        if (declaration is null || !OwnerDeclared(declaration, request.Owner))
        {
            return new PluginConfigurationSaveOutcome.NotDeclared();
        }
        if (!lifecycleHealth.IsCurrent(declaration))
        {
            return new PluginConfigurationSaveOutcome.NotDeclared();
        }

        var current = await store.LoadConfigurationAsync(request.Owner, cancellationToken);
        if (current.Revision != request.ExpectedRevision)
        {
            return new PluginConfigurationSaveOutcome.Conflict(current);
        }

        var validation = validator.Validate(
            declaration,
            request.Owner,
            request.Values,
            current.Secrets,
            request.Secrets
        );
        if (validation is PluginSettingsValidationOutcome.Invalid invalid)
        {
            return new PluginConfigurationSaveOutcome.Invalid(invalid.Issues);
        }
        if (codec.Encode(request.Values) is PluginSettingValuesEncodingOutcome.TooLarge)
        {
            return new PluginConfigurationSaveOutcome.Invalid([
                new(
                    null,
                    PluginSettingValidationCode.TooManyValues,
                    "The settings are too large to save."
                ),
            ]);
        }
        if (Declaration(request.Owner) != declaration || !lifecycleHealth.IsCurrent(declaration))
        {
            return new PluginConfigurationSaveOutcome.NotDeclared();
        }

        var write = new PluginConfigurationStoreWrite(
            current,
            request.Values,
            Protect(request.Owner, request.Secrets)
        );
        return await store.WriteConfigurationAsync(write, cancellationToken) switch
        {
            PluginConfigurationStoreWriteOutcome.Written written =>
                new PluginConfigurationSaveOutcome.Saved(written.State),
            PluginConfigurationStoreWriteOutcome.Conflict conflict =>
                new PluginConfigurationSaveOutcome.Conflict(conflict.Current),
            _ => throw new InvalidOperationException("Unknown configuration store outcome."),
        };
    }

    private PluginSecretChanges Protect(
        PluginConfigurationOwner owner,
        IReadOnlyList<PluginSecretUpdateEntry> updates
    )
    {
        var replacements = ImmutableArray.CreateBuilder<PluginProtectedSecretEntry>();
        var clears = ImmutableArray.CreateBuilder<PluginSettingId>();
        foreach (var entry in updates)
        {
            _ = entry.Update.Match(
                _ => false,
                replacement =>
                {
                    replacements.Add(
                        new(
                            entry.SettingId,
                            secrets.Protect(SecretKey(owner, entry.SettingId), replacement.Value)
                        )
                    );
                    return true;
                },
                _ =>
                {
                    clears.Add(entry.SettingId);
                    return true;
                }
            );
        }
        return new(replacements.ToImmutable(), clears.ToImmutable());
    }

    private static PluginSecretKey SecretKey(
        PluginConfigurationOwner owner,
        PluginSettingId settingId
    ) =>
        owner.Match<PluginSecretKey>(
            installation => new PluginSecretKey.Installation(installation.PluginId, settingId),
            feature => new PluginSecretKey.Feature(feature.Key, settingId)
        );

    private static bool OwnerDeclared(
        PluginFeatureDeclaration declaration,
        PluginConfigurationOwner owner
    ) =>
        owner.Match(
            installation => declaration.Installation.PluginId == installation.PluginId,
            feature =>
                declaration.Installation.PluginId == feature.Key.PluginId
                && declaration.FindFeature(feature.Key.FeatureId) is not null
        );
}
