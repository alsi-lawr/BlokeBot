namespace BlokeBot.Plugins.Features;

public sealed record PluginConfigurationSaveRequest(
    PluginConfigurationOwner Owner,
    PluginConfigurationRevision ExpectedRevision,
    PluginSettingValues Values,
    IReadOnlyList<PluginSecretUpdateEntry> Secrets
);

public abstract record PluginConfigurationLoadOutcome
{
    private PluginConfigurationLoadOutcome() { }

    public sealed record Loaded(
        PluginFeatureDeclaration Declaration,
        PluginConfigurationState Configuration
    ) : PluginConfigurationLoadOutcome;

    public sealed record NotDeclared : PluginConfigurationLoadOutcome;
}

public abstract record PluginConfigurationSaveOutcome
{
    private PluginConfigurationSaveOutcome() { }

    public sealed record Saved(PluginConfigurationState Configuration)
        : PluginConfigurationSaveOutcome;

    public sealed record Invalid(IReadOnlyList<PluginSettingValidationIssue> Issues)
        : PluginConfigurationSaveOutcome;

    public sealed record Conflict(PluginConfigurationState Current)
        : PluginConfigurationSaveOutcome;

    public sealed record NotDeclared : PluginConfigurationSaveOutcome;
}

public enum PluginFeatureEnableRejectionCode
{
    NotDeclared,
    InvalidSettings,
    MissingCoreDependency,
    LifecycleNotHealthy,
    GenerationExhausted,
    Conflict,
}

public abstract record PluginFeatureEnableOutcome
{
    private PluginFeatureEnableOutcome() { }

    public sealed record Enabled(PluginFeatureState State) : PluginFeatureEnableOutcome;

    public sealed record AlreadyEnabled(PluginFeatureState State) : PluginFeatureEnableOutcome;

    public sealed record Superseded(PluginFeatureState? Current) : PluginFeatureEnableOutcome;

    public sealed record Rejected(
        PluginFeatureEnableRejectionCode Code,
        IReadOnlyList<PluginSettingValidationIssue> SettingIssues
    ) : PluginFeatureEnableOutcome;
}

public abstract record PluginFeatureDisableOutcome
{
    private PluginFeatureDisableOutcome() { }

    public sealed record Disabled(PluginFeatureState State) : PluginFeatureDisableOutcome;

    public sealed record AlreadyDisabled(PluginFeatureState? State) : PluginFeatureDisableOutcome;

    public sealed record Conflict(PluginFeatureState? Current) : PluginFeatureDisableOutcome;

    public sealed record GenerationExhausted : PluginFeatureDisableOutcome;
}

public abstract record PluginFeatureReconciliationApplyOutcome
{
    private PluginFeatureReconciliationApplyOutcome() { }

    public sealed record Applied(PluginFeatureState State)
        : PluginFeatureReconciliationApplyOutcome;

    public sealed record Ignored(PluginFeatureState? Current)
        : PluginFeatureReconciliationApplyOutcome;

    public sealed record Conflict(PluginFeatureState? Current)
        : PluginFeatureReconciliationApplyOutcome;
}
