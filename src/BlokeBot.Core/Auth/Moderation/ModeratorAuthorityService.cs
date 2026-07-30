using System.Collections.Concurrent;
using System.Text.Json;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Hosts;

namespace BlokeBot.Core.Auth.Moderation;

public sealed class ModeratorAuthorityService(
    IHostBotAppAccessTokenSource appTokens,
    HelixClient helix,
    BotSettings settings,
    HostModAccessService moderatorAccess,
    TimeProvider timeProvider
) : IModeratorAuthorityService
{
    private static readonly TimeSpan _definitiveCacheLifetime = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<AuthorityCacheKey, CachedAuthority> _cache = new();

    public async Task<ModeratorAuthorityOutcome> AuthorizeAsync(
        AuthenticatedSession session,
        int requestedHostId,
        CancellationToken ct
    )
    {
        var selectedHost = SelectedHost(session);
        if (
            !session.IsAuthenticated
            || string.IsNullOrWhiteSpace(session.UserId)
            || selectedHost is null
            || selectedHost.Id != requestedHostId
        )
        {
            return new ModeratorAuthorityOutcome.HostMismatch();
        }

        if (selectedHost.Role is AuthRole.Streamer or AuthRole.Admin)
        {
            return new ModeratorAuthorityOutcome.Granted();
        }

        if (selectedHost.Role != AuthRole.Moderator || string.IsNullOrWhiteSpace(session.Login))
        {
            return new ModeratorAuthorityOutcome.HostMismatch();
        }

        if (!await moderatorAccess.CanModeratorAccessAsync(selectedHost.Id, session.Login, ct))
        {
            return new ModeratorAuthorityOutcome.Revoked();
        }

        var key = new AuthorityCacheKey(session.UserId, selectedHost.Id);
        var now = timeProvider.GetUtcNow();
        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAtUtc > now)
        {
            return cached.Outcome;
        }

        _cache.TryRemove(key, out _);
        return await ConfirmModeratorAuthorityAsync(key, selectedHost, ct);
    }

    private async Task<ModeratorAuthorityOutcome> ConfirmModeratorAuthorityAsync(
        AuthorityCacheKey key,
        BotHostChoice selectedHost,
        CancellationToken ct
    )
    {
        try
        {
            var appToken = await appTokens.GetAccessTokenAsync(ct);
            var channels = await helix.GetModeratedChannelsAsync(
                new HelixRequestContext(settings.Identity.ClientId, appToken),
                key.UserId,
                ct
            );
            ModeratorAuthorityOutcome outcome = channels.Any(channel =>
                string.Equals(
                    Login.Normalize(channel.BroadcasterLogin),
                    Login.Normalize(selectedHost.Login),
                    StringComparison.Ordinal
                )
            )
                ? new ModeratorAuthorityOutcome.Granted()
                : new ModeratorAuthorityOutcome.Revoked();
            _cache[key] = new CachedAuthority(
                outcome,
                timeProvider.GetUtcNow().Add(_definitiveCacheLifetime)
            );
            return outcome;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ModeratorAuthorityOutcome.Unavailable();
        }
        catch (HostBotAppAccessTokenUnavailableException)
        {
            return new ModeratorAuthorityOutcome.Unavailable();
        }
        catch (AppAccessTokenResponseException)
        {
            return new ModeratorAuthorityOutcome.Unavailable();
        }
        catch (HttpRequestException)
        {
            return new ModeratorAuthorityOutcome.Unavailable();
        }
        catch (JsonException)
        {
            return new ModeratorAuthorityOutcome.Unavailable();
        }
        catch (IOException)
        {
            return new ModeratorAuthorityOutcome.Unavailable();
        }
        catch (TimeoutException)
        {
            return new ModeratorAuthorityOutcome.Unavailable();
        }
    }

    private static BotHostChoice? SelectedHost(AuthenticatedSession session)
    {
        return session.State.Match<BotHostChoice?>(
            _ => null,
            selected => selected.Selection.Current,
            _ => null
        );
    }

    private sealed record AuthorityCacheKey(string UserId, int HostId);

    private sealed record CachedAuthority(
        ModeratorAuthorityOutcome Outcome,
        DateTimeOffset ExpiresAtUtc
    );
}
