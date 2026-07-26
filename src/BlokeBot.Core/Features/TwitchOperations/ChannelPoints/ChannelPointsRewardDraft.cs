namespace BlokeBot.Core.Features.TwitchOperations.ChannelPoints;

public sealed record ChannelPointsRewardDraft(
    string Title,
    string? Prompt,
    int Cost,
    bool IsUserInputRequired,
    bool IsMaxPerStreamEnabled,
    int? MaxPerStream,
    bool IsMaxPerUserPerStreamEnabled,
    int? MaxPerUserPerStream,
    bool IsGlobalCooldownEnabled,
    int? GlobalCooldownSeconds,
    bool ShouldRedemptionsSkipRequestQueue,
    string? BackgroundColor
)
{
    public string? Validate()
    {
        if (Title.Trim().Length is < 1 or > 45)
        {
            return "Reward titles must be 1–45 characters.";
        }
        if (Prompt?.Trim().Length > 200)
        {
            return "Reward prompts must be at most 200 characters.";
        }
        if (Cost is < 1 or > 1_000_000)
        {
            return "Reward cost must be 1–1,000,000 Channel Points.";
        }
        if (IsMaxPerStreamEnabled && MaxPerStream is not (> 0))
        {
            return "A per-stream limit is required.";
        }
        if (IsMaxPerUserPerStreamEnabled && MaxPerUserPerStream is not (> 0))
        {
            return "A per-user-per-stream limit is required.";
        }
        if (IsGlobalCooldownEnabled && GlobalCooldownSeconds is not (>= 1 and <= 604_800))
        {
            return "Global cooldown must be 1–604,800 seconds.";
        }
        if (BackgroundColor is { Length: > 0 } color && (color.Length != 7 || color[0] != '#'))
        {
            return "Background colour must be a #RRGGBB value.";
        }
        return null;
    }
}
