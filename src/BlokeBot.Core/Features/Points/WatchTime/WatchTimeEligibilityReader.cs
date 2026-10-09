using System.Collections.Immutable;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Identity;

namespace BlokeBot.Core.Features.Points.WatchTime;

internal sealed class WatchTimeEligibilityReader(
    IHostBotAccountTokenStatusProvider tokens,
    IHostStreamLivenessProvider streams,
    HelixClient helix,
    BotSettings botSettings
)
{
    private static readonly string[] _scopes = ["moderator:read:chatters"];

    internal async Task<WatchTimeEligibility> ReadAsync(
        WatchTimeHostSnapshot host,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(host.UserId))
        {
            return new WatchTimeEligibility.Unavailable();
        }
        var firstToken = await tokens.GetActiveTokenStatusAsync(host.Login, _scopes, ct);
        var firstStream = (await streams.GetStreamLiveness(host.Login).ExecuteAsync(ct)).Match(
            value => value,
            _ => throw new System.Diagnostics.UnreachableException()
        );
        if (firstStream is HostStreamLivenessOutcome.Offline)
        {
            return new WatchTimeEligibility.Offline();
        }
        if (
            firstToken.Status is not TokenStatus.Ready first
            || firstStream is not HostStreamLivenessOutcome.Live live
            || !MatchesAccount(host, firstToken, first)
        )
        {
            return new WatchTimeEligibility.Unavailable();
        }
        var chatters = await helix.GetChattersAsync(
            new(botSettings.Identity.ClientId, first.AccessToken),
            host.UserId,
            first.Validation.UserId,
            ct
        );
        if (chatters is not HelixChattersOutcome.Complete complete)
        {
            return new WatchTimeEligibility.Unavailable();
        }
        var finalToken = await tokens.GetActiveTokenStatusAsync(host.Login, _scopes, ct);
        var finalStream = (await streams.GetStreamLiveness(host.Login).ExecuteAsync(ct)).Match(
            value => value,
            _ => throw new System.Diagnostics.UnreachableException()
        );
        if (finalStream is HostStreamLivenessOutcome.Offline)
        {
            return new WatchTimeEligibility.Offline();
        }
        if (
            finalToken.Status is not TokenStatus.Ready final
            || finalStream is not HostStreamLivenessOutcome.Live finalLive
            || final.Validation.UserId != first.Validation.UserId
            || LoginName.Parse(final.Validation.Login) != LoginName.Parse(first.Validation.Login)
            || finalLive.StreamId != live.StreamId
            || finalLive.StartedAtUtc != live.StartedAtUtc
            || !MatchesAccount(host, finalToken, final)
        )
        {
            return new WatchTimeEligibility.Unavailable();
        }
        var targets = ImmutableArray.CreateBuilder<HelixChatter>();
        var users = new HashSet<string>(StringComparer.Ordinal);
        var logins = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chatter in complete.Chatters)
        {
            var login = LoginName.Parse(chatter.Login);
            if (
                login.IsEmpty
                || !login.Value.All(character =>
                    char.IsAsciiLetterOrDigit(character) || character == '_'
                )
                || string.IsNullOrWhiteSpace(chatter.UserId)
                || !chatter.UserId.All(char.IsAsciiDigit)
                || (logins.TryGetValue(login.Value, out var existing) && existing != chatter.UserId)
            )
            {
                return new WatchTimeEligibility.Unavailable();
            }
            logins[login.Value] = chatter.UserId;
            if (chatter.UserId != first.Validation.UserId && users.Add(chatter.UserId))
            {
                targets.Add(chatter with { Login = login.Value });
            }
        }
        return new WatchTimeEligibility.Complete(
            first.Validation.UserId,
            LoginName.Parse(first.Validation.Login).Value,
            live.StreamId,
            live.StartedAtUtc,
            targets.ToImmutable()
        );
    }

    private bool MatchesAccount(
        WatchTimeHostSnapshot host,
        ActiveBotAccountTokenStatus account,
        TokenStatus.Ready ready
    )
    {
        var expected = host.Account(LoginName.Parse(botSettings.Identity.BotUsername).Value);
        return !string.IsNullOrWhiteSpace(ready.Validation.UserId)
            && LoginName.Parse(account.BotLogin).Value == expected.Login
            && LoginName.Parse(ready.Validation.Login).Value == expected.Login
            && (!expected.Custom || expected.UserId == ready.Validation.UserId);
    }
}
