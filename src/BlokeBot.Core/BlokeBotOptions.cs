namespace BlokeBot.Core;

public sealed record BlokeBotOptions
{
    public string[] BotAdmins { get; init; } = [];

    public int BotStateChangeCooldownSeconds { get; init; } = 60;

    public BlokeBotCustomCommandOptions CustomCommands { get; init; } = new();

    public BlokeBotPointsOptions Points { get; init; } = new();

    public BlokeBotOverlayOptions Overlays { get; init; } = new();

    public string DatabasePath { get; init; } = "blokebot.db";

    public string? StateDirectory { get; init; }

    /// <summary>
    /// Optional base address of the deployment's BlokeBot.Site guide. Page help adds one guide
    /// link when it resolves and omits the link otherwise; it is deliberately outside
    /// <see cref="BlokeBotOptionsValidation"/> so a bad value never prevents startup.
    /// </summary>
    public string? HelpSiteBaseUrl { get; init; }

    /// <summary>
    /// Optional base address this deployment is reached on. When omitted, public links use the
    /// origin of <c>TwitchBot:Identity:RedirectUri</c>.
    /// </summary>
    public string? PublicBaseUrl { get; init; }
}

internal static class BlokeBotLocalState
{
    internal static string Directory(BlokeBotOptions options) =>
        !string.IsNullOrWhiteSpace(options.StateDirectory)
            ? Path.GetFullPath(options.StateDirectory)
            : Path.GetDirectoryName(Path.GetFullPath(options.DatabasePath))
                ?? throw new InvalidOperationException(
                    "The database path has no parent directory."
                );
}

public sealed record BlokeBotCustomCommandOptions
{
    public int MinimumCooldownSeconds { get; init; } = 5;

    public int AnnouncementSchedulerTickSeconds { get; init; } = 10;
}

public sealed record BlokeBotPointsOptions
{
    public int MinimumGamblingCooldownSeconds { get; init; }
}

public sealed record BlokeBotOverlayOptions
{
    public BlokeBotOverlayMediaOptions Media { get; init; } = new();
}

public sealed record BlokeBotOverlayMediaOptions
{
    public long MaximumUploadBytes { get; init; } = 50 * 1024 * 1024;

    public long MaximumHostStorageBytes { get; init; } = 500 * 1024 * 1024;

    public int DisconnectedQueueExpirySeconds { get; init; } = 30;

    public bool AllowPrivateNetworkTargets { get; init; }
}

public static class BlokeBotOptionsValidation
{
    internal const string PublicBaseUrlFailure =
        "BlokeBot:PublicBaseUrl must be an absolute HTTP or HTTPS URL without credentials, a query, or a fragment.";

    public static bool IsValid(BlokeBotOptions options) =>
        options.BotStateChangeCooldownSeconds >= 0
        && options.CustomCommands.MinimumCooldownSeconds >= 0
        && options.CustomCommands.AnnouncementSchedulerTickSeconds > 0
        && options.Points.MinimumGamblingCooldownSeconds >= 0
        && options.Overlays.Media.MaximumUploadBytes is >= 1024 and <= 2L * 1024 * 1024 * 1024
        && options.Overlays.Media.MaximumHostStorageBytes
            >= options.Overlays.Media.MaximumUploadBytes
        && options.Overlays.Media.MaximumHostStorageBytes <= 20L * 1024 * 1024 * 1024
        && options.Overlays.Media.DisconnectedQueueExpirySeconds is >= 1 and <= 300
        && PublicSiteLinks.HasValidConfiguredBaseAddress(options.PublicBaseUrl);
}
