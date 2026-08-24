using System.Globalization;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

internal enum PluginSecretEditMode
{
    Empty,
    Saved,
    Replacing,
    Clearing,
}

public sealed class PluginSettingEditor
{
    private PluginSettingEditor(PluginSettingDescriptor descriptor) => Descriptor = descriptor;

    internal PluginSettingDescriptor Descriptor { get; }
    internal string Value { get; set; } = string.Empty;
    internal bool BooleanValue { get; set; }
    internal bool HasValue { get; private set; }
    internal string? Error { get; private set; }
    internal long FocusRequest { get; private set; }
    internal PluginSecretEditMode SecretMode { get; private set; }
    internal bool HadSavedSecret { get; private set; }

    internal string InputId => $"plugin-setting-{Descriptor.Id.Value}";
    internal string ErrorId => $"{InputId}-error";

    internal static PluginSettingEditor Create(
        PluginSettingDescriptor descriptor,
        PluginConfigurationState configuration
    )
    {
        var editor = new PluginSettingEditor(descriptor);
        var value = configuration.Values.Entries.FirstOrDefault(entry =>
            entry.SettingId == descriptor.Id
        );
        if (value is not null)
        {
            editor.HasValue = true;
            _ = value.Value.Match(
                boolean => editor.BooleanValue = boolean.Value,
                text => Set(editor, text.Value),
                integer => Set(editor, integer.Value.ToString(CultureInfo.InvariantCulture)),
                number => Set(editor, number.Value.ToString(CultureInfo.InvariantCulture)),
                duration => Set(editor, duration.Seconds.ToString(CultureInfo.InvariantCulture)),
                choice => Set(editor, choice.Value.Value)
            );
        }
        if (descriptor.Schema is PluginSettingSchema.Secret)
        {
            editor.SecretMode = configuration.Secrets.Any(secret =>
                secret.SettingId == descriptor.Id && secret.HasValue
            )
                ? PluginSecretEditMode.Saved
                : PluginSecretEditMode.Empty;
            editor.HadSavedSecret = editor.SecretMode == PluginSecretEditMode.Saved;
        }
        return editor;
    }

    internal void ReplaceSecret()
    {
        Value = string.Empty;
        Error = null;
        SecretMode = PluginSecretEditMode.Replacing;
        FocusRequest++;
    }

    internal void ClearSecret()
    {
        Value = string.Empty;
        Error = null;
        SecretMode = PluginSecretEditMode.Clearing;
    }

    internal void CancelSecretEdit()
    {
        Value = string.Empty;
        Error = null;
        SecretMode = HadSavedSecret ? PluginSecretEditMode.Saved : PluginSecretEditMode.Empty;
    }

    internal PluginSettingEditorOutcome Build()
    {
        Error = null;
        return Descriptor.Schema.Match<PluginSettingEditorOutcome>(
            _ => BooleanSettingValue(),
            _ => TextValue(),
            _ => TextValue(),
            _ => IntegerValue(),
            _ => NumberValue(),
            _ => DurationValue(),
            _ => ChoiceValue(),
            SecretValue
        );
    }

    internal void ToggleBoolean()
    {
        HasValue = true;
        BooleanValue = !BooleanValue;
    }

    internal void SetOptionalBoolean(string? value)
    {
        HasValue = bool.TryParse(value, out var parsed);
        BooleanValue = HasValue && parsed;
    }

    internal void ApplyServerError(string message, bool focus)
    {
        Error = message;
        if (focus)
        {
            FocusRequest++;
        }
    }

    internal void RequestFocus() => FocusRequest++;

    private PluginSettingEditorOutcome BooleanSettingValue() =>
        Descriptor.Required || HasValue
            ? BuiltValue(new PluginSettingValue.Boolean(BooleanValue))
            : new PluginSettingEditorOutcome.Omitted();

    private PluginSettingEditorOutcome TextValue() =>
        string.IsNullOrWhiteSpace(Value)
            ? Descriptor.Required
                ? Invalid("Enter a value.")
                : new PluginSettingEditorOutcome.Omitted()
            : BuiltValue(new PluginSettingValue.Text(Value));

    private PluginSettingEditorOutcome IntegerValue() =>
        OptionalBlank() ? new PluginSettingEditorOutcome.Omitted()
        : long.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? BuiltValue(new PluginSettingValue.Integer(parsed))
        : Invalid("Enter a whole number.");

    private PluginSettingEditorOutcome NumberValue() =>
        OptionalBlank() ? new PluginSettingEditorOutcome.Omitted()
        : decimal.TryParse(Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? BuiltValue(new PluginSettingValue.Number(parsed))
        : Invalid("Enter a number.");

    private PluginSettingEditorOutcome DurationValue() =>
        OptionalBlank() ? new PluginSettingEditorOutcome.Omitted()
        : long.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? BuiltValue(new PluginSettingValue.Duration(seconds))
        : Invalid("Enter a duration in seconds.");

    private PluginSettingEditorOutcome ChoiceValue() =>
        OptionalBlank() ? new PluginSettingEditorOutcome.Omitted()
        : PluginSettingChoiceId.TryCreate(Value, out var choice)
            ? BuiltValue(new PluginSettingValue.Choice(choice))
        : Invalid("Choose a value.");

    private PluginSettingEditorOutcome SecretValue(PluginSettingSchema.Secret schema)
    {
        var validReplacement = PluginSecretPlaintext.TryCreate(
            Value,
            schema.MaximumLength,
            out var replacement
        );
        return SecretMode switch
        {
            PluginSecretEditMode.Saved => Secret(new PluginSecretUpdate.Keep()),
            PluginSecretEditMode.Clearing => Secret(new PluginSecretUpdate.Clear()),
            _ when OptionalBlank() => new PluginSettingEditorOutcome.Omitted(),
            _ when validReplacement => Secret(new PluginSecretUpdate.Replace(replacement)),
            _ => Invalid(
                string.IsNullOrEmpty(Value) ? "Enter a new secret." : "The secret is too long."
            ),
        };
    }

    private bool OptionalBlank() => !Descriptor.Required && string.IsNullOrWhiteSpace(Value);

    private PluginSettingEditorOutcome BuiltValue(PluginSettingValue value) =>
        new PluginSettingEditorOutcome.Setting(new(Descriptor.Id, value));

    private PluginSettingEditorOutcome Secret(PluginSecretUpdate update) =>
        new PluginSettingEditorOutcome.Secret(new(Descriptor.Id, update));

    private PluginSettingEditorOutcome Invalid(string message)
    {
        Error = message;
        return new PluginSettingEditorOutcome.Invalid();
    }

    private static bool Set(PluginSettingEditor editor, string value)
    {
        editor.Value = value;
        return true;
    }
}

internal abstract record PluginSettingEditorOutcome
{
    private PluginSettingEditorOutcome() { }

    internal sealed record Setting(PluginSettingValueEntry Entry) : PluginSettingEditorOutcome;

    internal sealed record Secret(PluginSecretUpdateEntry Entry) : PluginSettingEditorOutcome;

    internal sealed record Omitted : PluginSettingEditorOutcome;

    internal sealed record Invalid : PluginSettingEditorOutcome;
}
