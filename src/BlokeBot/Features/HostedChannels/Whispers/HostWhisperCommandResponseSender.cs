using System.Net;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Features.HostedChannels.Whispers;

public sealed class HostWhisperCommandResponseSender(
    ITwitchChatMessageSender chat,
    HostBotAccountAuthorizationService botAccounts,
    HostWhisperQuotaService quota,
    TwitchHelixApiClient users,
    TwitchHelixChatClient helix,
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TwitchBotIdentity identity,
    ILogger<HostWhisperCommandResponseSender> log
) : ITwitchCommandResponseSender
{
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

        var result = await TrySendWhisperAsync(sourceMessage, response.Message, cancellationToken);
        if (result.Outcome == HostWhisperSendOutcome.Sent)
            return;

        log.LogInformation(
            "Falling back to public chat response for {Login} in #{Channel}. Whisper outcome: {Outcome}; StatusCode: {StatusCode}; Detail: {Detail}.",
            sourceMessage.Login,
            sourceMessage.Channel,
            result.Outcome,
            result.StatusCode?.ToString() ?? "n/a",
            result.Detail ?? "n/a"
        );
        await chat.SendAsync(sourceMessage.Channel, response.Message, cancellationToken);
    }

    private async Task<HostWhisperSendResult> TrySendWhisperAsync(
        TwitchChatMessage sourceMessage,
        string message,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var host = await ResolveHostAsync(sourceMessage.Channel, cancellationToken);
            if (host is null)
                return new HostWhisperSendResult(HostWhisperSendOutcome.Disabled);

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
                return new HostWhisperSendResult(HostWhisperSendOutcome.CustomBotUnavailable);
            }

            var recipientUserId = await ResolveRecipientUserIdAsync(
                sourceMessage,
                tokenStatus.AccessToken,
                cancellationToken
            );
            if (string.IsNullOrWhiteSpace(recipientUserId))
                return new HostWhisperSendResult(HostWhisperSendOutcome.RecipientUnavailable);

            var senderUserId = tokenStatus.Validation.UserId;
            if (string.Equals(senderUserId, recipientUserId, StringComparison.Ordinal))
            {
                return new HostWhisperSendResult(
                    HostWhisperSendOutcome.SelfRecipient,
                    Detail: "sender and recipient user IDs match"
                );
            }

            var reservation = await quota.ReserveRecipientAsync(
                host.Id,
                senderUserId,
                recipientUserId,
                sourceMessage.Login,
                cancellationToken
            );
            if (!reservation.Allowed)
                return new HostWhisperSendResult(HostWhisperSendOutcome.QuotaExceeded);

            var result = await helix.SendWhisperAsync(
                tokenStatus.AccessToken,
                senderUserId,
                recipientUserId,
                message,
                cancellationToken
            );
            if (result.IsAccepted)
                return new HostWhisperSendResult(HostWhisperSendOutcome.Sent);

            if (result.Status == TwitchWhisperSendStatus.RateLimited)
            {
                await quota.MarkExhaustedAsync(host.Id, senderUserId, cancellationToken);
                return new HostWhisperSendResult(
                    HostWhisperSendOutcome.RateLimited,
                    result.StatusCode,
                    result.ResponseBody
                );
            }

            return new HostWhisperSendResult(
                HostWhisperSendOutcome.Rejected,
                result.StatusCode,
                result.ResponseBody
            );
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
            return new HostWhisperSendResult(HostWhisperSendOutcome.Rejected, Detail: ex.Message);
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

    private sealed record HostWhisperSendResult(
        HostWhisperSendOutcome Outcome,
        HttpStatusCode? StatusCode = null,
        string? Detail = null
    );

    private enum HostWhisperSendOutcome
    {
        Sent,
        Disabled,
        CustomBotUnavailable,
        RecipientUnavailable,
        SelfRecipient,
        QuotaExceeded,
        RateLimited,
        Rejected,
    }
}
