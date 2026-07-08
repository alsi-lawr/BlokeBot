namespace BlokeBot;

public sealed record BlokeBotOptions
{
    public string[] BotAdmins { get; init; } = [];

    public int BotStateChangeCooldownSeconds { get; init; } = 60;

    public string DatabasePath { get; init; } = "blokebot.db";
}
