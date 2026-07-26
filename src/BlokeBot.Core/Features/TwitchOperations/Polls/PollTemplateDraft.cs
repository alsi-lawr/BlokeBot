namespace BlokeBot.Core.Features.TwitchOperations.Polls;

public sealed record PollTemplateDraft(
    string Title,
    IReadOnlyList<string> Choices,
    int DurationSeconds,
    bool ChannelPointsVotingEnabled,
    int? ChannelPointsPerVote
)
{
    public PollTemplateValidationOutcome Validate()
    {
        var title = Title.Trim();
        var choices = Choices.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        if (title.Length is < 1 or > 60)
        {
            return new PollTemplateValidationOutcome.Invalid(
                "Poll titles must be 1–60 characters."
            );
        }
        if (choices.Length is < 2 or > 5 || choices.Any(x => x.Length > 25))
        {
            return new PollTemplateValidationOutcome.Invalid(
                "Polls need 2–5 choices, each no longer than 25 characters."
            );
        }
        if (DurationSeconds is < 15 or > 1800)
        {
            return new PollTemplateValidationOutcome.Invalid(
                "Poll duration must be 15–1800 seconds."
            );
        }
        if (ChannelPointsVotingEnabled && ChannelPointsPerVote is not (>= 1 and <= 1_000_000))
        {
            return new PollTemplateValidationOutcome.Invalid(
                "Channel Points voting needs a cost from 1 to 1,000,000 per vote."
            );
        }
        return new PollTemplateValidationOutcome.Valid(
            new(title, choices, DurationSeconds, ChannelPointsVotingEnabled, ChannelPointsPerVote)
        );
    }
}

public abstract record PollTemplateValidationOutcome
{
    private PollTemplateValidationOutcome() { }

    public sealed record Valid(PollTemplateDraft Draft) : PollTemplateValidationOutcome;

    public sealed record Invalid(string Message) : PollTemplateValidationOutcome;
}
