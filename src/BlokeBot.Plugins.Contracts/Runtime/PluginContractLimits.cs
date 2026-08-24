namespace BlokeBot.Plugins.Contracts;

public static class PluginContractLimits
{
    public const int MaximumManifestBytes = 256 * 1024;
    public const int MaximumPackageEntries = 512;
    public const long MaximumPackageBytes = 64L * 1024 * 1024;
    public const int MaximumLuaModuleBytes = 1024 * 1024;
    public const int MaximumBrowserAssetBytes = 4 * 1024 * 1024;
    public const int MaximumMediaAssetBytes = 32 * 1024 * 1024;
    public const long MaximumDeclaredPayloadBytes = MaximumPackageBytes;
    public const int MaximumDeclarationsPerSurface = 128;
    public const int MaximumNameCharacters = 120;
    public const int MaximumDescriptionCharacters = 1_000;
    public const int MaximumPluginValueDepth = 16;
    public const int MaximumPluginValueNodes = 1_024;
    public const int MaximumPluginValueStringBytes = 64 * 1024;
    public const int MaximumPluginValuePayloadBytes = 256 * 1024;
    public const int MaximumHostFailureSafeMessageCharacters = 1_000;
    public const int MaximumHostFailureSafeMessageBytes = 2 * 1024;
    public const int MaximumTextSettingCharacters = 4_096;
    public const int MaximumMultilineSettingCharacters = 16_384;
    public const int MaximumSecretSettingCharacters = 4_096;
    public const int MaximumSettingChoices = 64;
    public const long MinimumIntegerSettingValue = -1_000_000_000_000;
    public const long MaximumIntegerSettingValue = 1_000_000_000_000;
    public const decimal MinimumNumberSettingValue = -1_000_000_000_000m;
    public const decimal MaximumNumberSettingValue = 1_000_000_000_000m;
    public const int MaximumNumberDecimalPlaces = 6;
    public const long MaximumDurationSettingSeconds = 31_536_000;
    public const int MaximumOrdinarySettingsJsonBytes = 64 * 1024;
    public const int MaximumReadinessReasonCharacters = 256;
}
