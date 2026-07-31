namespace BlokeBot.Core;

public sealed record BlokeBotOptions
{
    public string[] BotAdmins { get; init; } = [];

    public int BotStateChangeCooldownSeconds { get; init; } = 60;

    public BlokeBotCustomCommandOptions CustomCommands { get; init; } = new();

    public BlokeBotPointsOptions Points { get; init; } = new();

    public BlokeBotOverlayOptions Overlays { get; init; } = new();

    public string DatabasePath { get; init; } = "blokebot.db";
}

public sealed record BlokeBotCustomCommandOptions
{
    public int MinimumCooldownSeconds { get; init; } = 5;

    public int AnnouncementSchedulerTickSeconds { get; init; } = 10;
}

public sealed record BlokeBotPointsOptions
{
    public int MinimumGamblingCooldownSeconds { get; init; } = 0;
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
    public static bool IsValid(BlokeBotOptions options)
    {
        return options.BotStateChangeCooldownSeconds >= 0
            && options.CustomCommands.MinimumCooldownSeconds >= 0
            && options.CustomCommands.AnnouncementSchedulerTickSeconds > 0
            && options.Points.MinimumGamblingCooldownSeconds >= 0
            && options.Overlays.Media.MaximumUploadBytes is >= 1024 and <= 2L * 1024 * 1024 * 1024
            && options.Overlays.Media.MaximumHostStorageBytes
                >= options.Overlays.Media.MaximumUploadBytes
            && options.Overlays.Media.MaximumHostStorageBytes <= 20L * 1024 * 1024 * 1024
            && options.Overlays.Media.DisconnectedQueueExpirySeconds is >= 1 and <= 300;
    }
}
