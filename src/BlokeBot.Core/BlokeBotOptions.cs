namespace BlokeBot.Core;

public sealed record BlokeBotOptions
{
    public string[] BotAdmins { get; init; } = [];

    public int BotStateChangeCooldownSeconds { get; init; } = 60;

    public BlokeBotCustomCommandOptions CustomCommands { get; init; } = new();

    public BlokeBotPointsOptions Points { get; init; } = new();

    public BlokeBotOverlayOptions Overlays { get; init; } = new();

    public string DatabasePath { get; init; } = "blokebot.db";

    /// <summary>
    /// Optional base address of the deployment's BlokeBot.Site guide. Page help adds one guide
    /// link when it resolves and omits the link otherwise; it is deliberately outside
    /// <see cref="BlokeBotOptionsValidation"/> so a bad value never prevents startup.
    /// </summary>
    public string? HelpSiteBaseUrl { get; init; }

    /// <summary>
    /// Optional base address this deployment is reached on. Chat replies that point a viewer at a
    /// page use it to send a full link instead of a bare path. It follows
    /// <see cref="HelpSiteBaseUrl"/> in staying outside <see cref="BlokeBotOptionsValidation"/>, so
    /// a bad value degrades the reply to the path it already used and never prevents startup.
    /// </summary>
    public string? PublicBaseUrl { get; init; }
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
    public static bool IsValid(BlokeBotOptions options) =>
        options.BotStateChangeCooldownSeconds >= 0
        && options.CustomCommands.MinimumCooldownSeconds >= 0
        && options.CustomCommands.AnnouncementSchedulerTickSeconds > 0
        && options.Points.MinimumGamblingCooldownSeconds >= 0
        && options.Overlays.Media.MaximumUploadBytes is >= 1024 and <= 2L * 1024 * 1024 * 1024
        && options.Overlays.Media.MaximumHostStorageBytes
            >= options.Overlays.Media.MaximumUploadBytes
        && options.Overlays.Media.MaximumHostStorageBytes <= 20L * 1024 * 1024 * 1024
        && options.Overlays.Media.DisconnectedQueueExpirySeconds is >= 1 and <= 300;
}
