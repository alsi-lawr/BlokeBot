namespace BlokeBot.Core.Features.TwitchOperations.Polls;

public abstract record PollOperationOutcome
{
    private PollOperationOutcome() { }

    public sealed record Started(PollView Poll) : PollOperationOutcome;

    public sealed record Ended(PollView Poll) : PollOperationOutcome;

    public sealed record TemplateSaved(PollTemplateView Template) : PollOperationOutcome;

    public sealed record ActivePollExists : PollOperationOutcome;

    public sealed record TemplateNotFound : PollOperationOutcome;

    public sealed record ConfirmationRequired : PollOperationOutcome;

    public sealed record NotReady(string Message) : PollOperationOutcome;

    public sealed record InvalidTemplate(string Message) : PollOperationOutcome;

    public sealed record ProviderRejected(string Message) : PollOperationOutcome;
}
