using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlokeBot.Features.HostedChannels.Whispers;

public sealed class HostWhisperCommandResponseSender(
    ITwitchChatMessageSender chat,
    HostBotAccountAuthorizationService botAccounts,
    HostWhisperQuotaService quota,
    TwitchHelixApiClient users,
    TwitchHelixChatClient helix,
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IOptions<TwitchBotIdentityOptions> options,
    ILogger<HostWhisperCommandResponseSender> log
) : ITwitchCommandResponseSender
{
    private readonly TwitchBotIdentityOptions identity = options.Value;

    public async ValueTask SendAsync(
        TwitchChatMessage sourceMessage,
        TwitchCommandResponse response,
        CancellationToken cancellationToken
    )
    {
        if (response.Target != TwitchCommandResponseTarget.Whisper)
        {
            await chat.SendAsync(sourceMessage.Channel, response.Message, cancellationToken);
            return;
        }

        var outcome = await TrySendWhisperAsync(sourceMessage, response.Message, cancellationToken);
        if (outcome == HostWhisperSendOutcome.Sent)
            return;

        log.LogInformation(
            "Falling back to public chat response for {Login} in #{Channel}. Whisper outcome: {Outcome}.",
            sourceMessage.Login,
            sourceMessage.Channel,
            outcome
        );
        await chat.SendAsync(sourceMessage.Channel, response.Message, cancellationToken);
    }

    private async Task<HostWhisperSendOutcome> TrySendWhisperAsync(
        TwitchChatMessage sourceMessage,
        string message,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var host = await ResolveHostAsync(sourceMessage.Channel, cancellationToken);
            if (host is null)
                return HostWhisperSendOutcome.Disabled;

            var tokenStatus = await botAccounts.GetCustomBotTokenStatusAsync(
                host.Id,
                [TwitchScopes.UserManageWhispers],
                cancellationToken
            );
            if (
                tokenStatus.State != TwitchTokenStatusState.Ready
                || string.IsNullOrWhiteSpace(tokenStatus.AccessToken)
                || tokenStatus.Validation is null
            )
            {
                return HostWhisperSendOutcome.CustomBotUnavailable;
            }

            var recipientUserId = await ResolveRecipientUserIdAsync(
                sourceMessage,
                tokenStatus.AccessToken,
                cancellationToken
            );
            if (string.IsNullOrWhiteSpace(recipientUserId))
                return HostWhisperSendOutcome.RecipientUnavailable;

            var senderUserId = tokenStatus.Validation.UserId;
            var reservation = await quota.ReserveRecipientAsync(
                host.Id,
                senderUserId,
                recipientUserId,
                sourceMessage.Login,
                cancellationToken
            );
            if (!reservation.Allowed)
                return HostWhisperSendOutcome.QuotaExceeded;

            var result = await helix.SendWhisperAsync(
                tokenStatus.AccessToken,
                senderUserId,
                recipientUserId,
                message,
                cancellationToken
            );
            if (result.IsAccepted)
                return HostWhisperSendOutcome.Sent;

            if (result.Status == TwitchWhisperSendStatus.RateLimited)
            {
                await quota.MarkExhaustedAsync(host.Id, senderUserId, cancellationToken);
                return HostWhisperSendOutcome.RateLimited;
            }

            return HostWhisperSendOutcome.Rejected;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(
                ex,
                "Could not send Twitch whisper response to {Login} in #{Channel}.",
                sourceMessage.Login,
                sourceMessage.Channel
            );
            return HostWhisperSendOutcome.Rejected;
        }
    }

    private async Task<WhisperHost?> ResolveHostAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        var login = TwitchLogin.Normalize(channel);
        if (string.IsNullOrWhiteSpace(login))
            return null;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == login)
            .Select(x => new { x.Id })
            .SingleOrDefaultAsync(cancellationToken);
        if (host is null)
            return null;

        var enabled = await db
            .HostBotAccountSettings.AsNoTracking()
            .Where(x => x.HostId == host.Id)
            .Select(x => x.OverrideEnabled && x.WhisperResponsesEnabled)
            .SingleOrDefaultAsync(cancellationToken);

        return enabled ? new WhisperHost(host.Id) : null;
    }

    private async Task<string?> ResolveRecipientUserIdAsync(
        TwitchChatMessage sourceMessage,
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        if (
            sourceMessage.Tags.TryGetValue("user-id", out var taggedUserId)
            && !string.IsNullOrWhiteSpace(taggedUserId)
        )
        {
            return taggedUserId.Trim();
        }

        var login = TwitchLogin.Normalize(sourceMessage.Login);
        if (string.IsNullOrWhiteSpace(login))
            return null;

        var resolved = await users.GetUsersByLoginAsync(
            new TwitchHelixRequestContext(identity.ClientId, accessToken),
            [login],
            cancellationToken
        );
        return resolved
            .FirstOrDefault(user => user.Login.Equals(login, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    private sealed record WhisperHost(int Id);

    private enum HostWhisperSendOutcome
    {
        Sent,
        Disabled,
        CustomBotUnavailable,
        RecipientUnavailable,
        QuotaExceeded,
        RateLimited,
        Rejected,
    }
}
