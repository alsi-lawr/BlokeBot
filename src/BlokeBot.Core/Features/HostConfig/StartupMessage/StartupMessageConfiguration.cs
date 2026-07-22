namespace BlokeBot.Core.Features.HostConfig.StartupMessage;

public sealed record StartupMessageConfiguration(bool Enabled, string Text);

public sealed record StartupMessageSaveCommand(bool Enabled, string Text);

public abstract record StartupMessageSaveOutcome
{
    private StartupMessageSaveOutcome() { }

    public sealed record Saved(StartupMessageConfiguration Configuration)
        : StartupMessageSaveOutcome;

    public sealed record Unauthorized : StartupMessageSaveOutcome;

    public sealed record HostNotFound : StartupMessageSaveOutcome;

    public sealed record TextRequired : StartupMessageSaveOutcome;

    public sealed record TextTooLong(int MaximumLength) : StartupMessageSaveOutcome;
}
