using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public enum PluginSettingValidationCode
{
    UnknownSetting,
    WrongValueKind,
    Required,
    TooLong,
    OutOfRange,
    InvalidChoice,
    DuplicateSecretUpdate,
    TooManyValues,
}

public sealed record PluginSettingValidationIssue(
    PluginSettingId? SettingId,
    PluginSettingValidationCode Code,
    string Message
);

public abstract record PluginSettingsValidationOutcome
{
    private PluginSettingsValidationOutcome() { }

    public sealed record Valid : PluginSettingsValidationOutcome;

    public sealed record Invalid(IReadOnlyList<PluginSettingValidationIssue> Issues)
        : PluginSettingsValidationOutcome;
}
