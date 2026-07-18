namespace BlokeBot.Core.Features.CustomCommands;

public enum CustomCommandSettingsTab
{
    Commands,
    MessageLibrary,
}

public abstract record CustomCommandConfigurationValidationTarget
{
    private CustomCommandConfigurationValidationTarget() { }

    public abstract CustomCommandSettingsTab Tab { get; }

    public sealed record MessageVariant(int EntryId, int VariantId)
        : CustomCommandConfigurationValidationTarget
    {
        public override CustomCommandSettingsTab Tab => CustomCommandSettingsTab.MessageLibrary;
    }

    public sealed record CommandAliases(int CommandId) : CustomCommandConfigurationValidationTarget
    {
        public override CustomCommandSettingsTab Tab => CustomCommandSettingsTab.Commands;
    }

    public sealed record CommandReply(int CommandId) : CustomCommandConfigurationValidationTarget
    {
        public override CustomCommandSettingsTab Tab => CustomCommandSettingsTab.Commands;
    }
}
