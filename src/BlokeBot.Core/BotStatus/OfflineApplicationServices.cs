using BlokeBot.Core.Features.Points.Balances;

namespace BlokeBot.Core.BotStatus;

internal sealed class OfflinePointTargetUserLookup : IPointTargetUserLookup
{
    public Task<bool> ExistsAsync(string login, CancellationToken ct) => Task.FromResult(false);
}

internal sealed class OfflinePublicChatMessageSender : IPublicChatMessageSender
{
    public ValueTask<PublicChatSendOutcome> SendAsync(
        string channel,
        string message,
        PublicChatDeliveryDeadline deadline,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<PublicChatSendOutcome>(new PublicChatSendOutcome.Rejected());
}
