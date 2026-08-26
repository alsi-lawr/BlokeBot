using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public abstract record PluginConfigurationOwner
{
    private PluginConfigurationOwner() { }

    public abstract TResult Match<TResult>(
        Func<Installation, TResult> installation,
        Func<Feature, TResult> feature
    );

    public sealed record Installation(PluginId PluginId) : PluginConfigurationOwner
    {
        public override TResult Match<TResult>(
            Func<Installation, TResult> installation,
            Func<Feature, TResult> feature
        ) => installation(this);
    }

    public sealed record Feature(PluginFeatureKey Key) : PluginConfigurationOwner
    {
        public override TResult Match<TResult>(
            Func<Installation, TResult> installation,
            Func<Feature, TResult> feature
        ) => feature(this);
    }
}

public sealed record PluginConfigurationState(
    PluginConfigurationOwner Owner,
    PluginSettingValues Values,
    IReadOnlyList<PluginSecretPresence> Secrets,
    PluginConfigurationRevision Revision
);

public sealed record PluginConfigurationStoreWrite(
    PluginConfigurationState Expected,
    PluginSettingValues Values,
    PluginSecretChanges Secrets
);

public abstract record PluginConfigurationStoreWriteOutcome
{
    private PluginConfigurationStoreWriteOutcome() { }

    public sealed record Written(PluginConfigurationState State)
        : PluginConfigurationStoreWriteOutcome;

    public sealed record Conflict(PluginConfigurationState Current)
        : PluginConfigurationStoreWriteOutcome;
}

public sealed record PluginFeatureEnableStoreRequest(
    PluginFeatureState? ExpectedState,
    PluginFeatureState NextState,
    PluginConfigurationRevision ExpectedInstallationRevision,
    PluginConfigurationRevision ExpectedFeatureRevision,
    PluginAutomationEnableStorePlan? Automation = null
);

public enum PluginFeatureEnableConflictCode
{
    FeatureState,
    InstallationConfiguration,
    FeatureConfiguration,
    AutomationProvenance,
    AutomationName,
}

public abstract record PluginFeatureEnableStoreOutcome
{
    private PluginFeatureEnableStoreOutcome() { }

    public sealed record Enabled(PluginFeatureState State) : PluginFeatureEnableStoreOutcome;

    public sealed record Conflict(PluginFeatureEnableConflictCode Code, PluginFeatureState? Current)
        : PluginFeatureEnableStoreOutcome;
}

public abstract record PluginFeatureStateStoreWriteOutcome
{
    private PluginFeatureStateStoreWriteOutcome() { }

    public sealed record Written(PluginFeatureState State) : PluginFeatureStateStoreWriteOutcome;

    public sealed record Conflict(PluginFeatureState? Current)
        : PluginFeatureStateStoreWriteOutcome;
}

public interface IPluginFeatureStore
{
    ValueTask<PluginConfigurationState> LoadConfigurationAsync(
        PluginConfigurationOwner owner,
        CancellationToken cancellationToken
    );

    ValueTask<PluginConfigurationStoreWriteOutcome> WriteConfigurationAsync(
        PluginConfigurationStoreWrite write,
        CancellationToken cancellationToken
    );

    ValueTask<PluginFeatureState?> LoadFeatureStateAsync(
        PluginFeatureKey key,
        CancellationToken cancellationToken
    );

    ValueTask<IReadOnlyList<PluginFeatureState>> LoadFeatureStatesAsync(
        PluginId? pluginId,
        CancellationToken cancellationToken
    );

    ValueTask<PluginFeatureEnableStoreOutcome> EnableAsync(
        PluginFeatureEnableStoreRequest request,
        CancellationToken cancellationToken
    );

    ValueTask<PluginFeatureStateStoreWriteOutcome> WriteFeatureStateAsync(
        PluginFeatureState expected,
        PluginFeatureState next,
        CancellationToken cancellationToken
    );

    ValueTask PurgeAsync(PluginId pluginId, CancellationToken cancellationToken);

    ValueTask<bool> HasFormat1IncompatibleStateAsync(
        PluginHostId hostId,
        CancellationToken cancellationToken
    );
}
