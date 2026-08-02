using System.Diagnostics;
using BlokeBot.Persistence.Models;
using TransportTwitchAnnouncementColor = BlokeBot.Twitch.TwitchAnnouncementColor;

namespace BlokeBot.Core.Features.CustomCommands;

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

    internal sealed record Accepted(
        CustomAnnouncementLatestDeliveryResult LatestDeliveryResult =
            CustomAnnouncementLatestDeliveryResult.Success
    ) : AnnouncementEnqueueOutcome;

    internal sealed record SafePreEnqueueTransient(
        AnnouncementEnqueueFailureType FailureType,
        CustomAnnouncementLatestDeliveryResult LatestDeliveryResult =
            CustomAnnouncementLatestDeliveryResult.Unexpected
    ) : AnnouncementEnqueueOutcome;

    internal sealed record Rejected(
        CustomAnnouncementLatestDeliveryResult LatestDeliveryResult =
            CustomAnnouncementLatestDeliveryResult.Unexpected
    ) : AnnouncementEnqueueOutcome;

    internal sealed record Ambiguous(
        AnnouncementEnqueueFailureType FailureType,
        CustomAnnouncementLatestDeliveryResult LatestDeliveryResult =
            CustomAnnouncementLatestDeliveryResult.Ambiguous
    ) : AnnouncementEnqueueOutcome;

    internal sealed record Unexpected(
        AnnouncementEnqueueFailureType FailureType,
        CustomAnnouncementLatestDeliveryResult LatestDeliveryResult =
            CustomAnnouncementLatestDeliveryResult.Unexpected
    ) : AnnouncementEnqueueOutcome;
}

internal sealed record CustomAnnouncementDeliveryRequest(
    string Channel,
    string Message,
    DateTimeOffset ExpiresAt,
    CustomAnnouncementDeliveryType DeliveryType,
    BlokeBot.Persistence.Models.TwitchAnnouncementColor AnnouncementColor
);

internal interface ICustomAnnouncementSender
{
    ValueTask<AnnouncementEnqueueOutcome> EnqueueAsync(
        CustomAnnouncementDeliveryRequest request,
        CancellationToken cancellationToken
    );
}

internal sealed class DisabledCustomAnnouncementSender : ICustomAnnouncementSender
{
    public ValueTask<AnnouncementEnqueueOutcome> EnqueueAsync(
        CustomAnnouncementDeliveryRequest request,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<AnnouncementEnqueueOutcome>(new AnnouncementEnqueueOutcome.Rejected());
}

internal sealed class TwitchAnnouncementCustomAnnouncementSender(
    PublicChatMessageQueue queue,
    ITwitchAnnouncementAccessService access,
    ChatAnnouncementClient announcements
) : ICustomAnnouncementSender
{
    public async ValueTask<AnnouncementEnqueueOutcome> EnqueueAsync(
        CustomAnnouncementDeliveryRequest request,
        CancellationToken cancellationToken
    ) =>
        request.DeliveryType switch
        {
            CustomAnnouncementDeliveryType.ChatMessage => await EnqueueChatAsync(
                queue,
                request,
                cancellationToken
            ),
            CustomAnnouncementDeliveryType.TwitchAnnouncement => await SendAnnouncementAsync(
                request,
                cancellationToken
            ),
            _ => new AnnouncementEnqueueOutcome.Rejected(
                CustomAnnouncementLatestDeliveryResult.Invalid
            ),
        };

    private async Task<AnnouncementEnqueueOutcome> SendAnnouncementAsync(
        CustomAnnouncementDeliveryRequest request,
        CancellationToken cancellationToken
    )
    {
        var resolved = await access.GetAccessAsync(request.Channel, cancellationToken);
        if (
            resolved
            is TwitchAnnouncementAccess.ReconnectRequired
                or TwitchAnnouncementAccess.AuthorityRequired
        )
        {
            return new AnnouncementEnqueueOutcome.Rejected(
                CustomAnnouncementLatestDeliveryResult.Permission
            );
        }

        if (resolved is TwitchAnnouncementAccess.Unavailable)
        {
            return new AnnouncementEnqueueOutcome.SafePreEnqueueTransient(
                new AnnouncementEnqueueFailureType("AnnouncementAccessUnavailable")
            );
        }

        var ready =
            resolved as TwitchAnnouncementAccess.Ready
            ?? throw new UnreachableException("Unknown Twitch announcement access.");
        var result = await announcements.SendAsync(
            ready.Context,
            ready.BroadcasterId,
            ready.ModeratorId,
            request.Message,
            ToTransportColor(request.AnnouncementColor),
            cancellationToken
        );
        return result switch
        {
            ChatAnnouncementSendResult.Sent => new AnnouncementEnqueueOutcome.Accepted(),
            ChatAnnouncementSendResult.Invalid => new AnnouncementEnqueueOutcome.Rejected(
                CustomAnnouncementLatestDeliveryResult.Invalid
            ),
            ChatAnnouncementSendResult.PermissionDenied => new AnnouncementEnqueueOutcome.Rejected(
                CustomAnnouncementLatestDeliveryResult.Permission
            ),
            ChatAnnouncementSendResult.RateLimited =>
                new AnnouncementEnqueueOutcome.SafePreEnqueueTransient(
                    new AnnouncementEnqueueFailureType("TwitchAnnouncementRateLimited"),
                    CustomAnnouncementLatestDeliveryResult.RateLimitRetry
                ),
            ChatAnnouncementSendResult.Unexpected => new AnnouncementEnqueueOutcome.Unexpected(
                new AnnouncementEnqueueFailureType("TwitchAnnouncementUnexpected")
            ),
            ChatAnnouncementSendResult.Ambiguous => new AnnouncementEnqueueOutcome.Ambiguous(
                new AnnouncementEnqueueFailureType("TwitchAnnouncementAmbiguous")
            ),
            _ => throw new UnreachableException("Unknown Twitch announcement send result."),
        };
    }

    private static async Task<AnnouncementEnqueueOutcome> EnqueueChatAsync(
        PublicChatMessageQueue queue,
        CustomAnnouncementDeliveryRequest request,
        CancellationToken cancellationToken
    ) =>
        await queue.EnqueueAsync(
            new PublicChatEnqueueCommand
            {
                Channel = request.Channel,
                Message = request.Message,
                Deadline = new PublicChatDeliveryDeadline.ProducerAbsolute(request.ExpiresAt),
            },
            cancellationToken
        ) switch
        {
            PublicChatEnqueueOutcome.Accepted => new AnnouncementEnqueueOutcome.Accepted(),
            PublicChatEnqueueOutcome.Rejected => new AnnouncementEnqueueOutcome.Rejected(),
            PublicChatEnqueueOutcome.SafePreEnqueueTransient transient =>
                new AnnouncementEnqueueOutcome.SafePreEnqueueTransient(
                    new AnnouncementEnqueueFailureType(transient.Cause.GetType().Name)
                ),
            PublicChatEnqueueOutcome.Ambiguous ambiguous =>
                new AnnouncementEnqueueOutcome.Ambiguous(
                    new AnnouncementEnqueueFailureType(ambiguous.Cause.GetType().Name)
                ),
            PublicChatEnqueueOutcome.Unexpected unexpected =>
                new AnnouncementEnqueueOutcome.Unexpected(
                    new AnnouncementEnqueueFailureType(unexpected.Cause.GetType().Name)
                ),
            _ => throw new UnreachableException("Unknown public-chat enqueue outcome."),
        };

    private static TransportTwitchAnnouncementColor ToTransportColor(
        BlokeBot.Persistence.Models.TwitchAnnouncementColor color
    ) =>
        color switch
        {
            BlokeBot.Persistence.Models.TwitchAnnouncementColor.Primary =>
                TransportTwitchAnnouncementColor.Primary,
            BlokeBot.Persistence.Models.TwitchAnnouncementColor.Blue =>
                TransportTwitchAnnouncementColor.Blue,
            BlokeBot.Persistence.Models.TwitchAnnouncementColor.Green =>
                TransportTwitchAnnouncementColor.Green,
            BlokeBot.Persistence.Models.TwitchAnnouncementColor.Orange =>
                TransportTwitchAnnouncementColor.Orange,
            BlokeBot.Persistence.Models.TwitchAnnouncementColor.Purple =>
                TransportTwitchAnnouncementColor.Purple,
            _ => throw new ArgumentOutOfRangeException(
                nameof(color),
                color,
                "Unsupported Twitch announcement color."
            ),
        };
}
