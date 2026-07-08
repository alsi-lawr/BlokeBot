namespace BlokeBot.Persistence.Models;

public static class PointsEligibilityModeStore
{
    public static string Format(PointsEligibilityMode mode) =>
        mode switch
        {
            PointsEligibilityMode.Subscribers => "subscribers",
            PointsEligibilityMode.Followers => "followers",
            _ => "everyone",
        };

    public static PointsEligibilityMode Parse(string value) =>
        value.ToLowerInvariant() switch
        {
            "subscribers" => PointsEligibilityMode.Subscribers,
            "followers" => PointsEligibilityMode.Followers,
            _ => PointsEligibilityMode.Everyone,
        };
}
