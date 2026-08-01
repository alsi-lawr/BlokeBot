using System.Diagnostics;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Auth;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using PersistedAnnouncementColor = BlokeBot.Persistence.Models.TwitchAnnouncementColor;
using TransportAnnouncementColor = BlokeBot.Twitch.TwitchAnnouncementColor;

namespace BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;

internal interface IAutomaticRaidNativeShoutoutSender
{
    Task<AutomaticRaidShoutoutDeliveryResult> SendAsync(
        int hostId,
        string targetLogin,
        CancellationToken cancellationToken
    );
}

internal interface IAutomaticRaidNativeShoutoutOperation
{
    Task<ShoutoutOperationOutcome> SendAsync(
        int hostId,
        string targetLogin,
        CancellationToken cancellationToken
    );
}

internal sealed class AutomaticRaidNativeShoutoutSender(
    IAutomaticRaidNativeShoutoutOperation shoutouts
) : IAutomaticRaidNativeShoutoutSender
{
    public async Task<AutomaticRaidShoutoutDeliveryResult> SendAsync(
        int hostId,
        string targetLogin,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await shoutouts.SendAsync(hostId, targetLogin, cancellationToken) switch
            {
                ShoutoutOperationOutcome.Sent =>
                    new AutomaticRaidShoutoutDeliveryResult.Delivered(),
                ShoutoutOperationOutcome.CooldownActive
                or ShoutoutOperationOutcome.CooldownUnknown => NotDelivered(
                    AutomaticRaidShoutoutResultCode.Cooldown
                ),
                ShoutoutOperationOutcome.TargetNotFound
                or ShoutoutOperationOutcome.SelfTarget
                or ShoutoutOperationOutcome.TargetOffline => NotDelivered(
                    AutomaticRaidShoutoutResultCode.Invalid
                ),
                ShoutoutOperationOutcome.NotReady notReady => NotDelivered(
                    IsAuthorityFailure(notReady.Message)
                ),
                ShoutoutOperationOutcome.ProviderRejected rejected
                    when rejected.Message.Contains(
                        "could not confirm",
                        StringComparison.OrdinalIgnoreCase
                    ) => new AutomaticRaidShoutoutDeliveryResult.Ambiguous(),
                ShoutoutOperationOutcome.ProviderRejected => NotDelivered(
                    AutomaticRaidShoutoutResultCode.Rejected
                ),
                _ => NotDelivered(AutomaticRaidShoutoutResultCode.Unexpected),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or TimeoutException)
        {
            return new AutomaticRaidShoutoutDeliveryResult.Ambiguous();
        }
        catch
        {
            return NotDelivered(AutomaticRaidShoutoutResultCode.Unexpected);
        }
    }

    private static AutomaticRaidShoutoutDeliveryResult.NotDelivered NotDelivered(
        AutomaticRaidShoutoutResultCode reason
    ) => new(reason);

    private static AutomaticRaidShoutoutResultCode IsAuthorityFailure(string message) =>
        message.Contains("permission", StringComparison.OrdinalIgnoreCase)
        || message.Contains("moderator", StringComparison.OrdinalIgnoreCase)
        || message.Contains("reconnect", StringComparison.OrdinalIgnoreCase)
        || message.Contains("connect the bot", StringComparison.OrdinalIgnoreCase)
        || message.Equals(ShoutoutService.UnauthorizedAuthorityMessage, StringComparison.Ordinal)
            ? AutomaticRaidShoutoutResultCode.AuthorityRequired
            : AutomaticRaidShoutoutResultCode.NotReady;
}

internal abstract record AutomaticRaidChannelInformationResult
{
    private AutomaticRaidChannelInformationResult() { }

    internal sealed record Found(string? GameName, string? StreamTitle)
        : AutomaticRaidChannelInformationResult;

    internal sealed record Unavailable : AutomaticRaidChannelInformationResult;
}

internal interface IAutomaticRaidChannelInformationProvider
{
    Task<AutomaticRaidChannelInformationResult> GetAsync(
        string raiderTwitchUserId,
        CancellationToken cancellationToken
    );
}

internal sealed class AutomaticRaidChannelInformationProvider(
    AppAccessTokenProvider appTokens,
    BotSettings settings,
    HelixClient helix
) : IAutomaticRaidChannelInformationProvider
{
    public async Task<AutomaticRaidChannelInformationResult> GetAsync(
        string raiderTwitchUserId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var token = await appTokens.GetAccessTokenAsync(cancellationToken);
            var result = await helix.GetChannelInformationAsync(
                new HelixRequestContext(settings.Identity.ClientId, token),
                raiderTwitchUserId,
                cancellationToken
            );
            return result is HelixChannelInformationOutcome.Found found
                ? new AutomaticRaidChannelInformationResult.Found(found.GameName, found.Title)
                : new AutomaticRaidChannelInformationResult.Unavailable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new AutomaticRaidChannelInformationResult.Unavailable();
        }
    }
}

internal abstract record AutomaticRaidAnnouncementSendResult
{
    private AutomaticRaidAnnouncementSendResult() { }

    internal sealed record Sent : AutomaticRaidAnnouncementSendResult;

    internal sealed record AuthorityRequired : AutomaticRaidAnnouncementSendResult;

    internal sealed record NotReady : AutomaticRaidAnnouncementSendResult;

    internal sealed record Invalid : AutomaticRaidAnnouncementSendResult;

    internal sealed record RateLimited : AutomaticRaidAnnouncementSendResult;

    internal sealed record Rejected : AutomaticRaidAnnouncementSendResult;

    internal sealed record Unexpected : AutomaticRaidAnnouncementSendResult;

    internal sealed record Ambiguous : AutomaticRaidAnnouncementSendResult;
}

internal interface IAutomaticRaidAnnouncementSender
{
    Task<AutomaticRaidAnnouncementSendResult> SendAsync(
        string channelLogin,
        string message,
        PersistedAnnouncementColor color,
        CancellationToken cancellationToken
    );
}

internal sealed class AutomaticRaidAnnouncementSender(
    ITwitchAnnouncementAccessService access,
    ChatAnnouncementClient announcements
) : IAutomaticRaidAnnouncementSender
{
    public async Task<AutomaticRaidAnnouncementSendResult> SendAsync(
        string channelLogin,
        string message,
        PersistedAnnouncementColor color,
        CancellationToken cancellationToken
    )
    {
        var resolved = await access.GetAccessAsync(channelLogin, cancellationToken);
        if (
            resolved
            is TwitchAnnouncementAccess.ReconnectRequired
                or TwitchAnnouncementAccess.AuthorityRequired
        )
        {
            return new AutomaticRaidAnnouncementSendResult.AuthorityRequired();
        }
        if (resolved is TwitchAnnouncementAccess.Unavailable)
        {
            return new AutomaticRaidAnnouncementSendResult.NotReady();
        }

        var ready =
            resolved as TwitchAnnouncementAccess.Ready
            ?? throw new UnreachableException("Unknown Twitch announcement access.");
        return await announcements.SendAsync(
            ready.Context,
            ready.BroadcasterId,
            ready.ModeratorId,
            message,
            ToTransportColor(color),
            cancellationToken
        ) switch
        {
            ChatAnnouncementSendResult.Sent => new AutomaticRaidAnnouncementSendResult.Sent(),
            ChatAnnouncementSendResult.Invalid => new AutomaticRaidAnnouncementSendResult.Invalid(),
            ChatAnnouncementSendResult.PermissionDenied =>
                new AutomaticRaidAnnouncementSendResult.AuthorityRequired(),
            ChatAnnouncementSendResult.RateLimited =>
                new AutomaticRaidAnnouncementSendResult.RateLimited(),
            ChatAnnouncementSendResult.Unexpected =>
                new AutomaticRaidAnnouncementSendResult.Unexpected(),
            ChatAnnouncementSendResult.Ambiguous =>
                new AutomaticRaidAnnouncementSendResult.Ambiguous(),
            _ => new AutomaticRaidAnnouncementSendResult.Rejected(),
        };
    }

    private static TransportAnnouncementColor ToTransportColor(PersistedAnnouncementColor color) =>
        color switch
        {
            PersistedAnnouncementColor.Primary => TransportAnnouncementColor.Primary,
            PersistedAnnouncementColor.Blue => TransportAnnouncementColor.Blue,
            PersistedAnnouncementColor.Green => TransportAnnouncementColor.Green,
            PersistedAnnouncementColor.Orange => TransportAnnouncementColor.Orange,
            PersistedAnnouncementColor.Purple => TransportAnnouncementColor.Purple,
            _ => throw new ArgumentOutOfRangeException(
                nameof(color),
                color,
                "Unsupported Twitch announcement color."
            ),
        };
}

internal sealed class AutomaticRaidShoutoutDelivery(
    IAutomaticRaidNativeShoutoutSender native,
    IAutomaticRaidChannelInformationProvider channelInformation,
    IPublicChatMessageSender chat,
    IAutomaticRaidAnnouncementSender announcements,
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    DurableAlertService alerts
) : IAutomaticRaidShoutoutDelivery
{
    private static readonly TimeSpan _deliveryLifetime = TimeSpan.FromMinutes(2);

    public async Task<AutomaticRaidShoutoutDeliveryResult> DeliverAsync(
        AutomaticRaidShoutoutDeliveryRequest request,
        CancellationToken cancellationToken
    )
    {
        AutomaticRaidShoutoutDeliveryResult result;
        try
        {
            result = request.Configuration.Mechanism switch
            {
                AutomaticRaidShoutoutMechanism.Native => await native.SendAsync(
                    request.HostId,
                    request.RaiderLogin,
                    cancellationToken
                ),
                AutomaticRaidShoutoutMechanism.Chat => await DeliverChatAsync(
                    request,
                    cancellationToken
                ),
                _ => NotDelivered(AutomaticRaidShoutoutResultCode.Invalid),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or TimeoutException)
        {
            result = new AutomaticRaidShoutoutDeliveryResult.Ambiguous();
        }
        catch
        {
            result = NotDelivered(AutomaticRaidShoutoutResultCode.Unexpected);
        }
        if (result is not AutomaticRaidShoutoutDeliveryResult.Delivered)
        {
            await AlertAsync(request, result, cancellationToken);
        }
        return result;
    }

    private async Task<AutomaticRaidShoutoutDeliveryResult> DeliverChatAsync(
        AutomaticRaidShoutoutDeliveryRequest request,
        CancellationToken cancellationToken
    )
    {
        if (
            AutomaticRaidShoutoutTemplate.Parse(request.Configuration.MessageTemplate)
            is not AutomaticRaidTemplateParseOutcome.Valid valid
        )
        {
            return NotDelivered(AutomaticRaidShoutoutResultCode.Invalid);
        }

        var enriched = await channelInformation.GetAsync(
            request.RaiderTwitchUserId,
            cancellationToken
        );
        var information = enriched as AutomaticRaidChannelInformationResult.Found;
        var render = valid.Template.Render(
            new AutomaticRaidTemplateValues(
                $"@{request.RaiderLogin}",
                request.RaiderDisplayName,
                $"https://twitch.tv/{request.RaiderLogin}",
                request.ViewerCount,
                information?.GameName,
                information?.StreamTitle
            )
        );
        if (render is AutomaticRaidTemplateRenderOutcome.TooLong)
        {
            return NotDelivered(AutomaticRaidShoutoutResultCode.RuntimeMessageTooLong);
        }

        var message =
            (render as AutomaticRaidTemplateRenderOutcome.Rendered)?.Message
            ?? throw new UnreachableException("Unknown automatic raid template render outcome.");
        return request.Configuration.ChatPresentation switch
        {
            AutomaticRaidChatPresentation.Regular => await SendChatAsync(
                request,
                message,
                null,
                cancellationToken
            ),
            AutomaticRaidChatPresentation.Pinned => await SendChatAsync(
                request,
                message,
                await PinIntentAsync(request, cancellationToken),
                cancellationToken
            ),
            AutomaticRaidChatPresentation.Announcement => await SendAnnouncementAsync(
                request,
                message,
                cancellationToken
            ),
            _ => NotDelivered(AutomaticRaidShoutoutResultCode.Invalid),
        };
    }

    private async Task<AutomaticRaidShoutoutDeliveryResult> SendChatAsync(
        AutomaticRaidShoutoutDeliveryRequest request,
        string message,
        PublicChatPinIntent? pinIntent,
        CancellationToken cancellationToken
    )
    {
        if (
            request.Configuration.ChatPresentation is AutomaticRaidChatPresentation.Pinned
            && pinIntent is null
        )
        {
            return NotDelivered(AutomaticRaidShoutoutResultCode.NotReady);
        }

        var deadline = new PublicChatDeliveryDeadline.ProducerAbsolute(
            request.MessageTimestamp + _deliveryLifetime
        );
        var correlation = new PublicChatDeliveryCorrelation(
            request.HostId,
            request.ProviderMessageId
        );
        var outcome = pinIntent is null
            ? await chat.SendCorrelatedAsync(
                request.HostLogin,
                message,
                deadline,
                correlation,
                cancellationToken
            )
            : await chat.SendCorrelatedAsync(
                request.HostLogin,
                message,
                deadline,
                correlation,
                pinIntent,
                cancellationToken
            );
        return outcome.Match<AutomaticRaidShoutoutDeliveryResult>(
            _ => new AutomaticRaidShoutoutDeliveryResult.Delivered(),
            _ => NotDelivered(AutomaticRaidShoutoutResultCode.Rejected)
        );
    }

    private async Task<PublicChatPinIntent?> PinIntentAsync(
        AutomaticRaidShoutoutDeliveryRequest request,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var outcomeId = await db
            .AutomaticRaidShoutoutOutcomes.Where(value =>
                value.HostId == request.HostId
                && value.ProviderMessageId == request.ProviderMessageId
            )
            .Select(value => (long?)value.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return outcomeId is { } id
            ? new PublicChatPinIntent(
                request.HostId,
                id,
                AutomaticRaidDeliveryCorrelation.Feature,
                request.ProviderMessageId,
                request.Configuration.PinDurationSeconds,
                false
            )
            : null;
    }

    private async Task<AutomaticRaidShoutoutDeliveryResult> SendAnnouncementAsync(
        AutomaticRaidShoutoutDeliveryRequest request,
        string message,
        CancellationToken cancellationToken
    ) =>
        await announcements.SendAsync(
            request.HostLogin,
            message,
            request.Configuration.AnnouncementColor,
            cancellationToken
        ) switch
        {
            AutomaticRaidAnnouncementSendResult.Sent =>
                new AutomaticRaidShoutoutDeliveryResult.Delivered(),
            AutomaticRaidAnnouncementSendResult.AuthorityRequired => NotDelivered(
                AutomaticRaidShoutoutResultCode.AuthorityRequired
            ),
            AutomaticRaidAnnouncementSendResult.NotReady => NotDelivered(
                AutomaticRaidShoutoutResultCode.NotReady
            ),
            AutomaticRaidAnnouncementSendResult.Invalid => NotDelivered(
                AutomaticRaidShoutoutResultCode.Invalid
            ),
            AutomaticRaidAnnouncementSendResult.RateLimited => NotDelivered(
                AutomaticRaidShoutoutResultCode.RateLimited
            ),
            AutomaticRaidAnnouncementSendResult.Rejected => NotDelivered(
                AutomaticRaidShoutoutResultCode.Rejected
            ),
            AutomaticRaidAnnouncementSendResult.Unexpected => NotDelivered(
                AutomaticRaidShoutoutResultCode.Unexpected
            ),
            AutomaticRaidAnnouncementSendResult.Ambiguous =>
                new AutomaticRaidShoutoutDeliveryResult.Ambiguous(),
            _ => NotDelivered(AutomaticRaidShoutoutResultCode.Unexpected),
        };

    private async Task AlertAsync(
        AutomaticRaidShoutoutDeliveryRequest request,
        AutomaticRaidShoutoutDeliveryResult result,
        CancellationToken cancellationToken
    )
    {
        var code = result switch
        {
            AutomaticRaidShoutoutDeliveryResult.Ambiguous =>
                AutomaticRaidShoutoutResultCode.Ambiguous,
            AutomaticRaidShoutoutDeliveryResult.NotDelivered notDelivered => notDelivered.Reason,
            _ => AutomaticRaidShoutoutResultCode.Unexpected,
        };
        _ = await alerts
            .Create(
                request.HostId,
                DurableAlertSeverity.Warning,
                AutomaticRaidDeliveryCorrelation.AlertSource,
                request.ProviderMessageId,
                "Automatic raid shoutout was not delivered",
                $"The automatic shoutout for @{request.RaiderLogin} ended with {code}. Check the shoutout delivery settings and Twitch connection.",
                "/twitch-operations/shoutouts"
            )
            .ExecuteAsync(cancellationToken);
    }

    private static AutomaticRaidShoutoutDeliveryResult.NotDelivered NotDelivered(
        AutomaticRaidShoutoutResultCode reason
    ) => new(reason);
}

internal static class AutomaticRaidDeliveryCorrelation
{
    internal const string Feature = "automatic-raid-shoutout";
    internal const string AlertSource = "automatic-raid-shoutout";
}
