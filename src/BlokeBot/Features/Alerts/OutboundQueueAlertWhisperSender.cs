using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Whispers;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Auth;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlokeBot.Features.Alerts;

internal sealed class OutboundQueueAlertWhisperSender(
    HostBotAccountAuthorizationService botAccounts,
    HostWhisperQuotaService quota,
    TwitchHelixApiClient users,
    TwitchHelixChatClient helix,
    IOptions<TwitchBotIdentityOptions> options,
    ILogger<OutboundQueueAlertWhisperSender> log
) : IOutboundQueueAlertWhisperSender
{
    private readonly TwitchBotIdentityOptions identity = options.Value;

    public async Task TrySendAsync(OutboundQueueAlertWhisperRequest request, CancellationToken ct)
    {
        try
        {
            var tokenStatus = await botAccounts.GetCustomBotTokenStatusAsync(
                request.HostId,
                [TwitchScopes.UserManageWhispers],
                ct
            );
            if (
                tokenStatus.State != TwitchTokenStatusState.Ready
                || string.IsNullOrWhiteSpace(tokenStatus.AccessToken)
                || tokenStatus.Validation is null
            )
            {
                log.LogInformation(
                    "Skipped outbound queue alert whisper for #{Channel}: custom bot whisper access unavailable. State: {State}.",
                    request.HostLogin,
                    tokenStatus.State
                );
                return;
            }

            var recipientUserId = await ResolveStreamerUserIdAsync(
                request,
                tokenStatus.AccessToken,
                ct
            );
            if (string.IsNullOrWhiteSpace(recipientUserId))
            {
                log.LogInformation(
                    "Skipped outbound queue alert whisper for #{Channel}: streamer Twitch user ID unavailable.",
                    request.HostLogin
                );
                return;
            }

            var senderUserId = tokenStatus.Validation.UserId;
            if (string.Equals(senderUserId, recipientUserId, StringComparison.Ordinal))
            {
                log.LogInformation(
                    "Skipped outbound queue alert whisper for #{Channel}: custom bot and streamer are the same Twitch user.",
                    request.HostLogin
                );
                return;
            }

            var reservation = await quota.ReserveRecipientAsync(
                request.HostId,
                senderUserId,
                recipientUserId,
                request.HostLogin,
                ct
            );
            if (!reservation.Allowed)
            {
                log.LogInformation(
                    "Skipped outbound queue alert whisper for #{Channel}: whisper quota exhausted.",
                    request.HostLogin
                );
                return;
            }

            var result = await helix.SendWhisperAsync(
                tokenStatus.AccessToken,
                senderUserId,
                recipientUserId,
                WhisperMessage(request),
                ct
            );
            if (result.IsAccepted)
            {
                log.LogInformation(
                    "Sent outbound queue alert whisper to streamer for #{Channel}.",
                    request.HostLogin
                );
                return;
            }

            if (result.Status == TwitchWhisperSendStatus.RateLimited)
                await quota.MarkExhaustedAsync(request.HostId, senderUserId, ct);

            log.LogWarning(
                "Twitch rejected outbound queue alert whisper for #{Channel}. Status: {Status}; StatusCode: {StatusCode}; Body: {Body}.",
                request.HostLogin,
                result.Status,
                result.StatusCode,
                result.ResponseBody ?? "n/a"
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
                "Could not send outbound queue alert whisper for #{Channel}.",
                request.HostLogin
            );
        }
    }

    private async Task<string?> ResolveStreamerUserIdAsync(
        OutboundQueueAlertWhisperRequest request,
        string accessToken,
        CancellationToken ct
    )
    {
        if (!string.IsNullOrWhiteSpace(request.HostTwitchUserId))
            return request.HostTwitchUserId.Trim();

        var login = TwitchLogin.Normalize(request.HostLogin);
        if (string.IsNullOrWhiteSpace(login))
            return null;

        var resolved = await users.GetUsersByLoginAsync(
            new TwitchHelixRequestContext(identity.ClientId, accessToken),
            [login],
            ct
        );
        return resolved
            .FirstOrDefault(user => user.Login.Equals(login, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    private static string WhisperMessage(OutboundQueueAlertWhisperRequest request) =>
        $"BlokeBot alert: outbound chat messages for #{request.HostLogin} are delayed. {request.PendingCount} messages are pending; the oldest has waited about {FormatAge(request.OldestPendingAge)}. Open BlokeBot alerts to acknowledge it.";

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes >= 1)
            return $"{Math.Round(age.TotalMinutes, 1)} minutes";

        return $"{Math.Max(1, (int)Math.Round(age.TotalSeconds))} seconds";
    }
}
