using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public sealed class PluginSettingsValidator
{
    public PluginSettingsValidationOutcome Validate(
        PluginFeatureDeclaration declaration,
        PluginConfigurationOwner owner,
        PluginSettingValues values,
        IReadOnlyList<PluginSecretPresence> secrets,
        IReadOnlyList<PluginSecretUpdateEntry> secretUpdates
    )
    {
        var issues = new List<PluginSettingValidationIssue>();
        var descriptors = Descriptors(declaration, owner);
        if (values.Entries.Count > PluginContractLimits.MaximumDeclarationsPerSurface)
        {
            issues.Add(
                new(
                    null,
                    PluginSettingValidationCode.TooManyValues,
                    "The settings contain too many values."
                )
            );
        }

        foreach (var entry in values.Entries)
        {
            if (!descriptors.TryGetValue(entry.SettingId, out var descriptor))
            {
                issues.Add(
                    new(
                        entry.SettingId,
                        PluginSettingValidationCode.UnknownSetting,
                        "This setting is not declared for this page."
                    )
                );
                continue;
            }

            ValidateValue(descriptor, entry.Value, issues);
        }

        ValidateSecrets(descriptors, secrets, secretUpdates, issues);
        ValidateRequired(descriptors.Values, values, secrets, secretUpdates, issues);
        return issues.Count == 0
            ? new PluginSettingsValidationOutcome.Valid()
            : new PluginSettingsValidationOutcome.Invalid(issues.AsReadOnly());
    }

    private static Dictionary<PluginSettingId, PluginSettingDescriptor> Descriptors(
        PluginFeatureDeclaration declaration,
        PluginConfigurationOwner owner
    ) =>
        owner.Match(
            installation =>
                declaration.Installation.PluginId == installation.PluginId
                    ? declaration
                        .Manifest.Settings.Where(static setting =>
                            setting.Scope == PluginSettingScope.Installation
                        )
                        .ToDictionary(static setting => setting.Id)
                    : new(),
            feature => FeatureDescriptors(declaration, feature.Key)
        );

    private static Dictionary<PluginSettingId, PluginSettingDescriptor> FeatureDescriptors(
        PluginFeatureDeclaration declaration,
        PluginFeatureKey key
    )
    {
        var feature = declaration.FindFeature(key.FeatureId);
        if (declaration.Installation.PluginId != key.PluginId || feature is null)
        {
            return [];
        }

        var settings = declaration.Manifest.Settings.ToDictionary(static setting => setting.Id);
        var result = new Dictionary<PluginSettingId, PluginSettingDescriptor>();
        foreach (var settingId in feature.Settings)
        {
            var descriptor = settings[settingId];
            if (descriptor.Scope == PluginSettingScope.Channel)
            {
                result.Add(descriptor.Id, descriptor);
            }
        }
        return result;
    }

    private static void ValidateValue(
        PluginSettingDescriptor descriptor,
        PluginSettingValue value,
        ICollection<PluginSettingValidationIssue> issues
    )
    {
        var valid = descriptor.Schema.Match(
            _ => value is PluginSettingValue.Boolean,
            text =>
                value is PluginSettingValue.Text typed
                && TextValid(typed.Value, text.MaximumLength),
            multiline =>
                value is PluginSettingValue.Text typed
                && TextValid(typed.Value, multiline.MaximumLength),
            integer =>
                value is PluginSettingValue.Integer typed
                && typed.Value >= integer.Minimum
                && typed.Value <= integer.Maximum,
            number =>
                value is PluginSettingValue.Number typed
                && typed.Value >= number.Minimum
                && typed.Value <= number.Maximum
                && decimal.Round(typed.Value, number.DecimalPlaces) == typed.Value,
            duration =>
                value is PluginSettingValue.Duration typed
                && typed.Seconds >= duration.MinimumSeconds
                && typed.Seconds <= duration.MaximumSeconds,
            choice =>
                value is PluginSettingValue.Choice typed
                && choice.Choices.Any(option => option.Id == typed.Value),
            _ => false
        );
        if (valid)
        {
            return;
        }

        issues.Add(
            new(
                descriptor.Id,
                FailureCode(descriptor.Schema, value),
                FailureMessage(descriptor.Schema, value)
            )
        );
    }

    private static bool TextValid(string value, int maximumLength) =>
        value.Length <= maximumLength && !string.IsNullOrWhiteSpace(value);

    private static PluginSettingValidationCode FailureCode(
        PluginSettingSchema schema,
        PluginSettingValue value
    ) =>
        MatchingKind(schema, value)
            ? schema.Match(
                _ => PluginSettingValidationCode.WrongValueKind,
                _ => PluginSettingValidationCode.TooLong,
                _ => PluginSettingValidationCode.TooLong,
                _ => PluginSettingValidationCode.OutOfRange,
                _ => PluginSettingValidationCode.OutOfRange,
                _ => PluginSettingValidationCode.OutOfRange,
                _ => PluginSettingValidationCode.InvalidChoice,
                _ => PluginSettingValidationCode.WrongValueKind
            )
            : PluginSettingValidationCode.WrongValueKind;

    private static string FailureMessage(PluginSettingSchema schema, PluginSettingValue value) =>
        MatchingKind(schema, value)
            ? schema.Match(
                _ => "Enter a Boolean value.",
                text => $"Enter text with at most {text.MaximumLength} characters.",
                multiline => $"Enter text with at most {multiline.MaximumLength} characters.",
                integer => $"Enter a whole number from {integer.Minimum} to {integer.Maximum}.",
                number => $"Enter a number from {number.Minimum} to {number.Maximum}.",
                duration =>
                    $"Enter a duration from {duration.MinimumSeconds} to {duration.MaximumSeconds} seconds.",
                _ => "Choose one of the available values.",
                _ => "Use the secret control for this setting."
            )
            : "Enter the correct value type.";

    private static bool MatchingKind(PluginSettingSchema schema, PluginSettingValue value) =>
        schema.Match(
            _ => value is PluginSettingValue.Boolean,
            _ => value is PluginSettingValue.Text,
            _ => value is PluginSettingValue.Text,
            _ => value is PluginSettingValue.Integer,
            _ => value is PluginSettingValue.Number,
            _ => value is PluginSettingValue.Duration,
            _ => value is PluginSettingValue.Choice,
            _ => false
        );

    private static void ValidateSecrets(
        IReadOnlyDictionary<PluginSettingId, PluginSettingDescriptor> descriptors,
        IReadOnlyList<PluginSecretPresence> secrets,
        IReadOnlyList<PluginSecretUpdateEntry> updates,
        ICollection<PluginSettingValidationIssue> issues
    )
    {
        if (updates.Select(static update => update.SettingId).Distinct().Count() != updates.Count)
        {
            issues.Add(
                new(
                    null,
                    PluginSettingValidationCode.DuplicateSecretUpdate,
                    "A secret update appears more than once."
                )
            );
        }

        foreach (var update in updates)
        {
            if (
                !descriptors.TryGetValue(update.SettingId, out var descriptor)
                || descriptor.Schema is not PluginSettingSchema.Secret
            )
            {
                issues.Add(
                    new(
                        update.SettingId,
                        PluginSettingValidationCode.UnknownSetting,
                        "This secret is not declared for this page."
                    )
                );
            }
            else if (
                update.Update is PluginSecretUpdate.Replace replacement
                && replacement.Value.Length
                    > ((PluginSettingSchema.Secret)descriptor.Schema).MaximumLength
            )
            {
                issues.Add(
                    new(
                        update.SettingId,
                        PluginSettingValidationCode.TooLong,
                        "The secret is too long."
                    )
                );
            }
        }
    }

    private static void ValidateRequired(
        IEnumerable<PluginSettingDescriptor> descriptors,
        PluginSettingValues values,
        IReadOnlyList<PluginSecretPresence> secrets,
        IReadOnlyList<PluginSecretUpdateEntry> updates,
        ICollection<PluginSettingValidationIssue> issues
    )
    {
        var valueIds = values.Entries.Select(static entry => entry.SettingId).ToHashSet();
        var secretIds = secrets
            .Where(static secret => secret.HasValue)
            .Select(static secret => secret.SettingId)
            .ToHashSet();
        foreach (var update in updates)
        {
            _ = update.Update.Match(
                _ => false,
                _ => secretIds.Add(update.SettingId),
                _ => secretIds.Remove(update.SettingId)
            );
        }

        foreach (var descriptor in descriptors.Where(static descriptor => descriptor.Required))
        {
            var present =
                descriptor.Schema is PluginSettingSchema.Secret
                    ? secretIds.Contains(descriptor.Id)
                    : valueIds.Contains(descriptor.Id);
            if (!present)
            {
                issues.Add(
                    new(
                        descriptor.Id,
                        PluginSettingValidationCode.Required,
                        "Enter a value before you save or enable this feature."
                    )
                );
            }
        }
    }
}
