using System.Diagnostics;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Functional;

namespace BlokeBot.Core.Features.HostedChannels.Status;

public sealed class HostBotStatusService(
    IHostBotAppAccessTokenSource appTokens,
    IHostBotAccountTokenStatusProvider botAccounts,
    HelixClient helix,
    BotSettings settings
) : IHostStreamLivenessProvider
{
    public IO<HostBotChannelStatus, Never> GetStatus(string channelLogin)
    {
        return GetReadiness(channelLogin).Map(HostBotChannelStatus.FromReadiness);
    }

    public IO<HostBotReadinessOutcome, Never> GetReadiness(string channelLogin)
    {
        return IO<HostBotReadinessOutcome, Never>.Create(async ct =>
            Result<HostBotReadinessOutcome, Never>.Success(
                await EvaluateReadinessCoreAsync(channelLogin, ct)
            )
        );
    }

    private async Task<HostBotReadinessOutcome> EvaluateReadinessCoreAsync(
        string channelLogin,
        CancellationToken ct
    )
    {
        var configured = ConfiguredCapabilities();
        if (!configured.ModeratorCheckConfigured)
        {
            return new HostBotReadinessOutcome.NotConfigured();
        }

        try
        {
            return await EvaluateReadinessAsync(channelLogin, configured, ct);
        }
        catch (HttpRequestException)
        {
            ct.ThrowIfCancellationRequested();
            return new HostBotReadinessOutcome.Unknown(configured);
        }
        catch (JsonException)
        {
            ct.ThrowIfCancellationRequested();
            return new HostBotReadinessOutcome.Unknown(configured);
        }
    }

    private async Task<HostBotReadinessOutcome> EvaluateReadinessAsync(
        string channelLogin,
        HostBotReadinessCapabilities configured,
        CancellationToken ct
    )
    {
        var tokenStatus = await botAccounts.GetActiveTokenStatusAsync(
            channelLogin,
            settings.Identity.Scopes,
            ct
        );
        return await tokenStatus.Status.Match(
            _ =>
                Task.FromResult<HostBotReadinessOutcome>(
                    new HostBotReadinessOutcome.Unknown(configured)
                ),
            _ =>
                Task.FromResult<HostBotReadinessOutcome>(
                    new HostBotReadinessOutcome.TokenUnavailable(configured)
                ),
            _ =>
                Task.FromResult<HostBotReadinessOutcome>(
                    new HostBotReadinessOutcome.InvalidToken(configured)
                ),
            missingScopes =>
                EvaluateAuthorizedReadinessAsync(
                    channelLogin,
                    tokenStatus.BotLogin,
                    missingScopes.AccessToken,
                    missingScopes.Validation,
                    missingScopes.GrantedScopes,
                    configured,
                    ct
                ),
            ready =>
                EvaluateAuthorizedReadinessAsync(
                    channelLogin,
                    tokenStatus.BotLogin,
                    ready.AccessToken,
                    ready.Validation,
                    ready.GrantedScopes,
                    configured,
                    ct
                )
        );
    }

    private async Task<HostBotReadinessOutcome> EvaluateAuthorizedReadinessAsync(
        string channelLogin,
        string botLogin,
        string accessToken,
        TokenValidation validation,
        IReadOnlyList<string> grantedScopes,
        HostBotReadinessCapabilities configured,
        CancellationToken ct
    )
    {
        var capabilities = GrantedCapabilities(configured, grantedScopes);
        if (!capabilities.ModeratorCheckGranted)
        {
            return new HostBotReadinessOutcome.MissingModeratorCheckScope(capabilities);
        }

        if (
            !string.IsNullOrWhiteSpace(botLogin)
            && !string.Equals(
                Login.Normalize(botLogin),
                Login.Normalize(validation.Login),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return new HostBotReadinessOutcome.BotAccountMismatch(capabilities);
        }

        var identities = await LookupUsersAsync(accessToken, [Login.Normalize(channelLogin)], ct);
        if (!identities.TryGetValue(Login.Normalize(channelLogin), out var channelId))
        {
            return new HostBotReadinessOutcome.IdentityLookupFailed(capabilities);
        }

        if (string.Equals(validation.UserId, channelId, StringComparison.Ordinal))
        {
            return ChannelAuthorityReadyOutcome(capabilities);
        }

        var moderatorCheck = await helix.GetModeratedChannelStatusAsync(
            HelixContext(accessToken),
            validation.UserId,
            channelId,
            ct
        );
        return moderatorCheck.Match<HostBotReadinessOutcome>(
            _ => new HostBotReadinessOutcome.Unknown(capabilities),
            _ => new HostBotReadinessOutcome.NeedsAuthorization(capabilities),
            _ => new HostBotReadinessOutcome.MissingModeratorCheckPermission(capabilities),
            _ => ChannelAuthorityReadyOutcome(capabilities),
            _ => new HostBotReadinessOutcome.NotModerator(capabilities)
        );
    }

    private static HostBotReadinessOutcome ChannelAuthorityReadyOutcome(
        HostBotReadinessCapabilities capabilities
    )
    {
        return capabilities.FollowerReadGranted
            ? new HostBotReadinessOutcome.Ready()
            : new HostBotReadinessOutcome.MissingFollowerReadScope(capabilities);
    }

    public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin)
    {
        return IO<HostStreamLivenessOutcome, Never>.Create(async ct =>
            Result<HostStreamLivenessOutcome, Never>.Success(
                await EvaluateStreamLivenessAsync(channelLogin, ct)
            )
        );
    }

    private async Task<HostStreamLivenessOutcome> EvaluateStreamLivenessAsync(
        string channelLogin,
        CancellationToken ct
    )
    {
        try
        {
            var token = await appTokens.GetAccessTokenAsync(ct);
            return await helix.GetStreamAsync(HelixContext(token), channelLogin, ct) is { } stream
                ? new HostStreamLivenessOutcome.Live(stream.Id)
                : new HostStreamLivenessOutcome.Offline();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HostBotAppAccessTokenUnavailableException exception)
        {
            return Unavailable(
                HostStreamLivenessUnavailableReason.AppAccessTokenUnavailable,
                exception
            );
        }
        catch (AppAccessTokenResponseException exception)
        {
            return Unavailable(
                HostStreamLivenessUnavailableReason.ProviderResponseInvalid,
                exception
            );
        }
        catch (HttpRequestException exception)
        {
            return Unavailable(
                HostStreamLivenessUnavailableReason.ProviderRequestFailed,
                exception
            );
        }
        catch (IOException exception)
        {
            return Unavailable(
                HostStreamLivenessUnavailableReason.ProviderRequestFailed,
                exception
            );
        }
        catch (JsonException exception)
        {
            return Unavailable(
                HostStreamLivenessUnavailableReason.ProviderResponseInvalid,
                exception
            );
        }
        catch (TimeoutException exception)
        {
            return Unavailable(HostStreamLivenessUnavailableReason.ProviderTimedOut, exception);
        }
    }

    public IO<FollowerCheckOutcome, Never> CheckFollower(string channelLogin, string viewerLogin)
    {
        return IO<FollowerCheckOutcome, Never>.Create(async ct =>
            Result<FollowerCheckOutcome, Never>.Success(
                await CheckFollowerAsync(channelLogin, viewerLogin, ct)
            )
        );
    }

    private async Task<FollowerCheckOutcome> CheckFollowerAsync(
        string channelLogin,
        string viewerLogin,
        CancellationToken ct
    )
    {
        var statusResult = await GetStatus(channelLogin).ExecuteAsync(ct);
        var status = statusResult.Match(value => value, _ => throw new UnreachableException());
        if (!status.IsModerator)
        {
            return new FollowerCheckOutcome.Unavailable();
        }

        var token = (
            await GetValidatedUserAccessTokenAsync(channelLogin, ct)
        ).Match<ValidatedUserAccessToken?>(value => value, () => null);
        if (token is null)
        {
            return new FollowerCheckOutcome.Unavailable();
        }

        var identities = await LookupUsersAsync(
            token.AccessToken,
            [Login.Normalize(channelLogin), Login.Normalize(viewerLogin)],
            ct
        );
        if (
            !identities.TryGetValue(Login.Normalize(channelLogin), out var channelId)
            || !identities.TryGetValue(Login.Normalize(viewerLogin), out var viewerId)
        )
        {
            return new FollowerCheckOutcome.NotEligible();
        }

        var followerStatus = await helix.GetFollowerStatusAsync(
            HelixContext(token.AccessToken),
            channelId,
            viewerId,
            token.Validation.UserId,
            ct
        );
        return followerStatus.Match<FollowerCheckOutcome>(
            _ => new FollowerCheckOutcome.Eligible(),
            _ => new FollowerCheckOutcome.NotEligible(),
            _ => new FollowerCheckOutcome.Unavailable()
        );
    }

    private HostBotReadinessCapabilities ConfiguredCapabilities()
    {
        var scopes = settings.Identity.Scopes.Select(ScopeSet.Normalize).ToHashSet();
        return new(
            scopes.Contains(Scopes.UserReadModeratedChannels),
            false,
            scopes.Contains(Scopes.ModeratorReadFollowers),
            false
        );
    }

    private static HostBotReadinessCapabilities GrantedCapabilities(
        HostBotReadinessCapabilities configured,
        IReadOnlyList<string> grantedScopes
    )
    {
        var scopes = grantedScopes.Select(ScopeSet.Normalize).ToHashSet();
        return configured with
        {
            ModeratorCheckGranted = scopes.Contains(Scopes.UserReadModeratedChannels),
            FollowerReadGranted = scopes.Contains(Scopes.ModeratorReadFollowers),
        };
    }

    private async Task<Option<ValidatedUserAccessToken>> GetValidatedUserAccessTokenAsync(
        string channelLogin,
        CancellationToken ct
    )
    {
        var status = await botAccounts.GetActiveTokenStatusAsync(
            channelLogin,
            settings.Identity.Scopes,
            ct
        );
        return status.Status.Match(
            _ => Option<ValidatedUserAccessToken>.None,
            _ => Option<ValidatedUserAccessToken>.None,
            _ => Option<ValidatedUserAccessToken>.None,
            missingScopes =>
                Option<ValidatedUserAccessToken>.Some(
                    new(missingScopes.AccessToken, missingScopes.Validation)
                ),
            ready => Option<ValidatedUserAccessToken>.Some(new(ready.AccessToken, ready.Validation))
        );
    }

    private async Task<Dictionary<string, string>> LookupUsersAsync(
        string token,
        IReadOnlyList<string> logins,
        CancellationToken ct
    )
    {
        var users = await helix.GetUsersByLoginAsync(HelixContext(token), logins, ct);
        return users.ToDictionary(
            x => Login.Normalize(x.Login),
            x => x.Id,
            StringComparer.OrdinalIgnoreCase
        );
    }

    private HelixRequestContext HelixContext(string token)
    {
        return new(settings.Identity.ClientId, token);
    }

    private static HostStreamLivenessOutcome.Unavailable Unavailable(
        HostStreamLivenessUnavailableReason reason,
        Exception cause
    )
    {
        return new(reason, cause);
    }

    private sealed record ValidatedUserAccessToken(string AccessToken, TokenValidation Validation);
}
