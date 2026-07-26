namespace BlokeBot.Core.Features.TwitchOperations.ChannelPoints;

public abstract record ChannelPointsOperationOutcome
{
    private ChannelPointsOperationOutcome() { }

    public sealed record RewardCreated(ChannelPointsRewardView Reward)
        : ChannelPointsOperationOutcome;

    public sealed record RewardUpdated : ChannelPointsOperationOutcome;

    public sealed record RewardDeleted : ChannelPointsOperationOutcome;

    public sealed record RedemptionUpdated : ChannelPointsOperationOutcome;

    public sealed record ConfirmationRequired(string Message) : ChannelPointsOperationOutcome;

    public sealed record NotReady(string Message) : ChannelPointsOperationOutcome;

    public sealed record Ineligible(string Message) : ChannelPointsOperationOutcome;

    public sealed record ExternalReadOnly : ChannelPointsOperationOutcome;

    public sealed record RedemptionNotActionable : ChannelPointsOperationOutcome;

    public sealed record InvalidRequest(string Message) : ChannelPointsOperationOutcome;

    public sealed record ProviderRejected(string Message) : ChannelPointsOperationOutcome;
}
