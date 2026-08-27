using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginSettingsHostModule(
    IPluginFeatureStore store,
    IPluginFeatureDeclarationProvider declarations,
    IPluginSecretProtector secrets
) : IPluginHostModule
{
    public PluginHostModuleDescriptor Descriptor => PluginStandardHostModules.Settings;

    public ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<PluginHostCallOutcome>(Unavailable());

    public async ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginWorkerInvocationIdentity identity,
        PluginHostCall call,
        CancellationToken cancellationToken
    )
    {
        var installation = call.Operation == Descriptor.Operations[0].Id;
        var owner = installation
            ? (PluginConfigurationOwner)
                new PluginConfigurationOwner.Installation(identity.Plugin.PluginId)
            : new PluginConfigurationOwner.Feature(
                new(identity.Plugin.PluginId, identity.Feature, identity.Host)
            );
        if (!TrySettings(identity, installation, out var declared))
        {
            return Unavailable();
        }

        var snapshot = await store.LoadConfigurationSnapshotAsync(owner, cancellationToken);
        var values = ImmutableArray.CreateBuilder<PluginValueProperty>();
        foreach (
            var entry in snapshot.Configuration.Values.Entries.Where(entry =>
                declared.Contains(entry.SettingId)
            )
        )
        {
            values.Add(new(entry.SettingId.Value, Value(entry.Value)));
        }
        foreach (
            var entry in snapshot.ProtectedSecrets.Where(entry =>
                declared.Contains(entry.SettingId)
            )
        )
        {
            var key = owner.Match<PluginSecretKey>(
                installationOwner => new PluginSecretKey.Installation(
                    installationOwner.PluginId,
                    entry.SettingId
                ),
                featureOwner => new PluginSecretKey.Feature(featureOwner.Key, entry.SettingId)
            );
            if (
                secrets.Unprotect(key, entry.Value)
                is not PluginSecretUnprotectOutcome.Unprotected unprotected
            )
            {
                return Unavailable();
            }
            values.Add(new(entry.SettingId.Value, new PluginValue.String(unprotected.Value.Value)));
        }

        return new PluginHostCallOutcome.Returned(new PluginValue.Map(values.ToImmutable()));
    }

    private bool TrySettings(
        PluginWorkerInvocationIdentity identity,
        bool installation,
        out IReadOnlySet<PluginSettingId> settings
    )
    {
        if (
            !declarations.Current.Declarations.TryGetValue(
                identity.Plugin.PluginId,
                out var declaration
            )
            || declaration.Installation != identity.Plugin
        )
        {
            settings = null!;
            return false;
        }

        settings =
            installation
                ? declaration
                    .Manifest.Settings.Where(static setting =>
                        setting.Scope == PluginSettingScope.Installation
                    )
                    .Select(static setting => setting.Id)
                    .ToHashSet()
            : declaration.FindFeature(identity.Feature) is { } feature
                ? declaration
                    .Manifest.Settings.Where(setting =>
                        setting.Scope == PluginSettingScope.Channel
                        && feature.Settings.Contains(setting.Id)
                    )
                    .Select(static setting => setting.Id)
                    .ToHashSet()
            : ImmutableHashSet<PluginSettingId>.Empty;
        return installation
            || settings.Count > 0
            || declaration.FindFeature(identity.Feature) is not null;
    }

    private static PluginValue Value(PluginSettingValue value) =>
        value.Match<PluginValue>(
            boolean => new PluginValue.Boolean(boolean.Value),
            text => new PluginValue.String(text.Value),
            integer => new PluginValue.Number(integer.Value),
            number => new PluginValue.Number((double)number.Value),
            duration => new PluginValue.Number(duration.Seconds),
            choice => new PluginValue.String(choice.Value.Value)
        );

    private static PluginHostCallOutcome.Failed Unavailable() =>
        new(new(PluginHostFailureCode.Unavailable, "Plugin settings are unavailable."));
}
