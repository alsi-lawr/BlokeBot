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
        var backgroundInvalid =
            BackgroundColor is { Length: > 0 } color && (color.Length != 7 || color[0] != '#');
        var titleInvalid = Title.Trim().Length is < 1 or > 45;
        var promptInvalid = Prompt?.Trim().Length > 200;
        var costInvalid = Cost is < 1 or > 1_000_000;
        var maxPerStreamInvalid = IsMaxPerStreamEnabled && MaxPerStream is not > 0;
        var maxPerUserPerStreamInvalid =
            IsMaxPerUserPerStreamEnabled && MaxPerUserPerStream is not > 0;
        var globalCooldownInvalid =
            IsGlobalCooldownEnabled && GlobalCooldownSeconds is not (>= 1 and <= 604_800);
        return titleInvalid switch
        {
            true => "Reward titles must be 1–45 characters.",
            false when promptInvalid => "Reward prompts must be at most 200 characters.",
            false when costInvalid => "Reward cost must be 1–1,000,000 Channel Points.",
            false when maxPerStreamInvalid => "A per-stream limit is required.",
            false when maxPerUserPerStreamInvalid => "A per-user-per-stream limit is required.",
            false when globalCooldownInvalid => "Global cooldown must be 1–604,800 seconds.",
            false when backgroundInvalid => "Background colour must be a #RRGGBB value.",
            _ => null,
        };
    }
}
