namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts;

public abstract record ShoutoutOperationOutcome
{
    private ShoutoutOperationOutcome() { }

    public sealed record Sent(string TargetLogin) : ShoutoutOperationOutcome;

    public sealed record TargetNotFound(string TargetLogin) : ShoutoutOperationOutcome;

    public sealed record SelfTarget : ShoutoutOperationOutcome;

    public sealed record TargetOffline(string TargetLogin) : ShoutoutOperationOutcome;

    public sealed record NotReady(string Message) : ShoutoutOperationOutcome;

    public sealed record CooldownUnknown : ShoutoutOperationOutcome;

    public sealed record CooldownActive(DateTime EligibleAtUtc) : ShoutoutOperationOutcome;

    public sealed record ProviderRejected(string Message) : ShoutoutOperationOutcome;
}
