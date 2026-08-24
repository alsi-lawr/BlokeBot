using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginSettingsEditor
{
    private PluginSettingsEditor(IReadOnlyList<PluginSettingEditor> settings) =>
        Settings = settings;

    internal IReadOnlyList<PluginSettingEditor> Settings { get; }

    internal static PluginSettingsEditor Installation(
        PluginFeatureDeclaration declaration,
        PluginConfigurationState configuration
    ) =>
        new(
            declaration
                .Manifest.Settings.Where(static setting =>
                    setting.Scope == PluginSettingScope.Installation
                )
                .Select(setting => PluginSettingEditor.Create(setting, configuration))
                .ToArray()
        );

    internal static PluginSettingsEditor Feature(
        PluginFeatureDeclaration declaration,
        PluginFeatureDescriptor feature,
        PluginConfigurationState configuration
    )
    {
        var settingIds = feature.Settings.ToHashSet();
        return new(
            declaration
                .Manifest.Settings.Where(setting =>
                    setting.Scope == PluginSettingScope.Channel && settingIds.Contains(setting.Id)
                )
                .Select(setting => PluginSettingEditor.Create(setting, configuration))
                .ToArray()
        );
    }

    internal PluginSettingsEditorBuildOutcome Build()
    {
        var values = new List<PluginSettingValueEntry>();
        var secrets = new List<PluginSecretUpdateEntry>();
        var invalid = false;
        foreach (var setting in Settings)
        {
            switch (setting.Build())
            {
                case PluginSettingEditorOutcome.Setting value:
                    values.Add(value.Entry);
                    break;
                case PluginSettingEditorOutcome.Secret secret:
                    secrets.Add(secret.Entry);
                    break;
                case PluginSettingEditorOutcome.Omitted:
                    break;
                case PluginSettingEditorOutcome.Invalid:
                    if (!invalid)
                    {
                        setting.RequestFocus();
                    }
                    invalid = true;
                    break;
            }
        }
        return
            !invalid
            && PluginSettingValues.Create(values) is PluginSettingValuesOutcome.Created created
            ? new PluginSettingsEditorBuildOutcome.Built(created.Values, secrets.AsReadOnly())
            : new PluginSettingsEditorBuildOutcome.Invalid();
    }

    internal int ApplyServerErrors(IReadOnlyList<PluginSettingValidationIssue> issues)
    {
        var focus = true;
        var applied = 0;
        foreach (var issue in issues)
        {
            var editor = Settings.FirstOrDefault(setting =>
                setting.Descriptor.Id == issue.SettingId
            );
            if (editor is null)
            {
                continue;
            }
            editor.ApplyServerError(issue.Message, focus);
            focus = false;
            applied++;
        }
        return applied;
    }
}

internal abstract record PluginSettingsEditorBuildOutcome
{
    private PluginSettingsEditorBuildOutcome() { }

    internal sealed record Built(
        PluginSettingValues Values,
        IReadOnlyList<PluginSecretUpdateEntry> Secrets
    ) : PluginSettingsEditorBuildOutcome;

    internal sealed record Invalid : PluginSettingsEditorBuildOutcome;
}
