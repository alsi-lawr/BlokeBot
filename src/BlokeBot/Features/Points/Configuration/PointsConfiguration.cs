using BlokeBot.Features.Points.Commands;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Points.Configuration;

public sealed class PointsConfiguration
{
    public string PointLabel { get; set; } = "points";
    public PointsCommandAliasEditor Aliases { get; set; } = new();
    public PointsReplySettingsEditor Replies { get; set; } = new();
    public int GamblingWinRatePercent { get; set; } = 50;
    public int GiveawayDurationSeconds { get; set; } = 300;
    public string GiveawayMinimumPayout { get; set; } = "10";
    public string GiveawayMaximumPayout { get; set; } = "100";
    public int GiveawayWinnerCount { get; set; } = 1;
    public PointsEligibilityMode GiveawayEligibility { get; set; } = PointsEligibilityMode.Everyone;
    public int GiveawayCooldownSeconds { get; set; } = 300;
}
