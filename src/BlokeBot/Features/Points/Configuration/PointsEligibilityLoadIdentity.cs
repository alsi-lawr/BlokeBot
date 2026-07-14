namespace BlokeBot.Features.Points.Configuration;

public sealed record PointsEligibilityLoadIdentity
{
    private PointsEligibilityLoadIdentity(string hostLogin)
    {
        HostLogin = hostLogin;
    }

    public string HostLogin { get; }

    public static PointsEligibilityLoadIdentity? From(string hostLogin)
    {
        return string.IsNullOrWhiteSpace(hostLogin)
            ? null
            : new(hostLogin.Trim().ToLowerInvariant());
    }
}
