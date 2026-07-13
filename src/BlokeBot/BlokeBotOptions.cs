namespace BlokeBot;

public sealed record BlokeBotOptions
{
    public string[] BotAdmins { get; init; } = [];

    public int BotStateChangeCooldownSeconds { get; init; } = 60;

    public BlokeBotCustomCommandOptions CustomCommands { get; init; } = new();

    public BlokeBotPointsOptions Points { get; init; } = new();

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

public static class BlokeBotOptionsValidation
{
    public static bool IsValid(BlokeBotOptions options)
    {
        return options.BotStateChangeCooldownSeconds >= 0
            && options.CustomCommands.MinimumCooldownSeconds >= 0
            && options.CustomCommands.AnnouncementSchedulerTickSeconds > 0
            && options.Points.MinimumGamblingCooldownSeconds >= 0;
    }
}
