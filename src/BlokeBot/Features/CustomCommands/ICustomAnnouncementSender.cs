using System.Diagnostics;
using BlokeBot.Twitch.Runtime;

namespace BlokeBot.Features.CustomCommands;

internal readonly record struct AnnouncementEnqueueFailureType
{
    internal AnnouncementEnqueueFailureType(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    internal string Value { get; }
}

internal abstract record AnnouncementEnqueueOutcome
{
    private AnnouncementEnqueueOutcome() { }

    internal sealed record Accepted : AnnouncementEnqueueOutcome;

    internal sealed record SafePreEnqueueTransient(AnnouncementEnqueueFailureType FailureType)
        : AnnouncementEnqueueOutcome;

    internal sealed record Rejected : AnnouncementEnqueueOutcome;

    internal sealed record Ambiguous(AnnouncementEnqueueFailureType FailureType)
        : AnnouncementEnqueueOutcome;

    internal sealed record Unexpected(AnnouncementEnqueueFailureType FailureType)
        : AnnouncementEnqueueOutcome;
}

internal interface ICustomAnnouncementSender
{
    ValueTask<AnnouncementEnqueueOutcome> EnqueueAsync(
        string channel,
        string message,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken
    );
}

internal sealed class DisabledCustomAnnouncementSender : ICustomAnnouncementSender
{
    public ValueTask<AnnouncementEnqueueOutcome> EnqueueAsync(
        string channel,
        string message,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<AnnouncementEnqueueOutcome>(new AnnouncementEnqueueOutcome.Rejected());
}

internal sealed class TwitchCustomAnnouncementSender(PublicChatMessageQueue queue)
    : ICustomAnnouncementSender
{
    public async ValueTask<AnnouncementEnqueueOutcome> EnqueueAsync(
        string channel,
        string message,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken
    ) =>
        await queue.EnqueueAsync(
            new PublicChatEnqueueCommand
            {
                Channel = channel,
                Message = message,
                Deadline = new PublicChatDeliveryDeadline.ProducerAbsolute(expiresAt),
            },
            cancellationToken
        ) switch
        {
            PublicChatEnqueueOutcome.Accepted => new AnnouncementEnqueueOutcome.Accepted(),
            PublicChatEnqueueOutcome.Rejected => new AnnouncementEnqueueOutcome.Rejected(),
            PublicChatEnqueueOutcome.SafePreEnqueueTransient transient =>
                new AnnouncementEnqueueOutcome.SafePreEnqueueTransient(
                    FailureType(transient.Cause)
                ),
            PublicChatEnqueueOutcome.Ambiguous ambiguous =>
                new AnnouncementEnqueueOutcome.Ambiguous(FailureType(ambiguous.Cause)),
            PublicChatEnqueueOutcome.Unexpected unexpected =>
                new AnnouncementEnqueueOutcome.Unexpected(FailureType(unexpected.Cause)),
            _ => throw new UnreachableException("Unknown public-chat enqueue outcome."),
        };

    private static AnnouncementEnqueueFailureType FailureType(Exception exception) =>
        new(exception.GetType().Name);
}
