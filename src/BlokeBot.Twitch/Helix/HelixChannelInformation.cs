namespace BlokeBot.Twitch;

public abstract record HelixChannelInformationOutcome
{
    private HelixChannelInformationOutcome() { }

    public sealed record Found(string? GameName, string? Title) : HelixChannelInformationOutcome;

    public sealed record NotFound : HelixChannelInformationOutcome;

    public sealed record Invalid : HelixChannelInformationOutcome;

    public sealed record PermissionDenied : HelixChannelInformationOutcome;

    public sealed record Unavailable : HelixChannelInformationOutcome;
}
