using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public sealed partial class PluginFeatureManager
{
    private IReadOnlyList<PluginSettingValidationIssue> EnableSettingIssues(
        PluginFeatureDeclaration declaration,
        PluginConfigurationState installation,
        PluginConfigurationState feature
    )
    {
        var issues = new List<PluginSettingValidationIssue>();
        AddIssues(
            validator.Validate(
                declaration,
                installation.Owner,
                RelevantValues(declaration, PluginSettingScope.Installation, installation.Values),
                RelevantSecrets(declaration, PluginSettingScope.Installation, installation.Secrets),
                []
            ),
            issues
        );
        AddIssues(
            validator.Validate(
                declaration,
                feature.Owner,
                RelevantValues(declaration, PluginSettingScope.Channel, feature.Values),
                RelevantSecrets(declaration, PluginSettingScope.Channel, feature.Secrets),
                []
            ),
            issues
        );
        return issues.AsReadOnly();
    }

    private static PluginSettingValues RelevantValues(
        PluginFeatureDeclaration declaration,
        PluginSettingScope scope,
        PluginSettingValues values
    )
    {
        var settingIds = declaration
            .Manifest.Settings.Where(setting => setting.Scope == scope)
            .Select(static setting => setting.Id)
            .ToHashSet();
        return (
            (PluginSettingValuesOutcome.Created)
                PluginSettingValues.Create(
                    values.Entries.Where(entry => settingIds.Contains(entry.SettingId))
                )
        ).Values;
    }

    private static IReadOnlyList<PluginSecretPresence> RelevantSecrets(
        PluginFeatureDeclaration declaration,
        PluginSettingScope scope,
        IReadOnlyList<PluginSecretPresence> secrets
    )
    {
        var settingIds = declaration
            .Manifest.Settings.Where(setting => setting.Scope == scope)
            .Select(static setting => setting.Id)
            .ToHashSet();
        return secrets.Where(secret => settingIds.Contains(secret.SettingId)).ToArray();
    }

    private static void AddIssues(
        PluginSettingsValidationOutcome outcome,
        ICollection<PluginSettingValidationIssue> issues
    )
    {
        if (outcome is not PluginSettingsValidationOutcome.Invalid invalid)
        {
            return;
        }
        foreach (var issue in invalid.Issues)
        {
            issues.Add(issue);
        }
    }
}
