using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Functional;

namespace BlokeBot.Core.Features.HostedChannels.Status;

public sealed class FollowerOnlyChatReadinessService(
    IHostBotAppAccessTokenSource appTokens,
    IHostBotAccountTokenStatusProvider botAccounts,
    HelixClient helix,
    BotSettings settings,
    TimeProvider timeProvider
)
{
    public IO<FollowerOnlyChatReadiness, Never> GetReadiness(string channelLogin) =>
        IO<FollowerOnlyChatReadiness, Never>.Create(async ct =>
            Result<FollowerOnlyChatReadiness, Never>.Success(
                await EvaluateReadinessAsync(channelLogin, ct)
            )
        );

    private async Task<FollowerOnlyChatReadiness> EvaluateReadinessAsync(
        string channelLogin,
        CancellationToken ct
    )
    {
        try
        {
            var appToken = await appTokens.GetAccessTokenAsync(ct);
            var channel = await FindChannelAsync(appToken, channelLogin, ct);
            if (channel is null)
            {
                return UnableToVerify(FollowerOnlyChatVerificationFailure.ChannelLookupUnavailable);
            }

            var chatSettings = await helix.GetChatSettingsAsync(
                HelixContext(appToken),
                channel.Id,
                ct
            );
            if (!chatSettings.FollowerMode)
            {
                return new FollowerOnlyChatReadiness.NotRequired();
            }

            return await EvaluateFollowerModeAsync(channel, channelLogin, chatSettings, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HostBotAppAccessTokenUnavailableException)
        {
            return UnableToVerify(FollowerOnlyChatVerificationFailure.ChatSettingsUnavailable);
        }
        catch (AppAccessTokenResponseException)
        {
            return UnableToVerify(FollowerOnlyChatVerificationFailure.ChatSettingsUnavailable);
        }
        catch (HttpRequestException)
        {
            return UnableToVerify(FollowerOnlyChatVerificationFailure.ChatSettingsUnavailable);
        }
        catch (JsonException)
        {
            return UnableToVerify(FollowerOnlyChatVerificationFailure.ChatSettingsUnavailable);
        }
        catch (IOException)
        {
            return UnableToVerify(FollowerOnlyChatVerificationFailure.ChatSettingsUnavailable);
        }
        catch (TimeoutException)
        {
            return UnableToVerify(FollowerOnlyChatVerificationFailure.ChatSettingsUnavailable);
        }
    }

    private async Task<FollowerOnlyChatReadiness> EvaluateFollowerModeAsync(
        HelixUser channel,
        string channelLogin,
        ChatSettings chatSettings,
        CancellationToken ct
    )
    {
        var tokenStatus = await botAccounts.GetActiveTokenStatusAsync(
            channelLogin,
            settings.Identity.Scopes,
            ct
        );
        return await tokenStatus.Status.Match<Task<FollowerOnlyChatReadiness>>(
            _ =>
                Task.FromResult<FollowerOnlyChatReadiness>(
                    UnableToVerify(FollowerOnlyChatVerificationFailure.BotTokenUnknown)
                ),
            _ =>
                Task.FromResult<FollowerOnlyChatReadiness>(
                    UnableToVerify(FollowerOnlyChatVerificationFailure.BotTokenUnavailable)
                ),
            _ =>
                Task.FromResult<FollowerOnlyChatReadiness>(
                    UnableToVerify(FollowerOnlyChatVerificationFailure.BotTokenInvalid)
                ),
            missingScopes =>
                EvaluateWithValidatedBotTokenAsync(
                    channel,
                    tokenStatus.BotLogin,
                    missingScopes.AccessToken,
                    missingScopes.Validation,
                    missingScopes.GrantedScopes,
                    chatSettings,
                    ct
                ),
            ready =>
                EvaluateWithValidatedBotTokenAsync(
                    channel,
                    tokenStatus.BotLogin,
                    ready.AccessToken,
                    ready.Validation,
                    ready.GrantedScopes,
                    chatSettings,
                    ct
                )
        );
    }

    private async Task<FollowerOnlyChatReadiness> EvaluateWithValidatedBotTokenAsync(
        HelixUser channel,
        string botLogin,
        string accessToken,
        TokenValidation validation,
        IReadOnlyList<string> grantedScopes,
        ChatSettings chatSettings,
        CancellationToken ct
    )
    {
        if (
            !string.IsNullOrWhiteSpace(botLogin)
            && !string.Equals(
                Login.Normalize(botLogin),
                Login.Normalize(validation.Login),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return UnableToVerify(FollowerOnlyChatVerificationFailure.BotAccountMismatch);
        }

        if (string.Equals(validation.UserId, channel.Id, StringComparison.Ordinal))
        {
            return new FollowerOnlyChatReadiness.Exempt(FollowerOnlyChatExemption.Broadcaster);
        }

        var scopes = grantedScopes.Select(ScopeSet.Normalize).ToHashSet();
        if (!scopes.Contains(Scopes.UserReadModeratedChannels))
        {
            return UnableToVerify(FollowerOnlyChatVerificationFailure.MissingModeratorCheckScope);
        }

        var moderatorStatus = await helix.GetModeratedChannelStatusAsync(
            HelixContext(accessToken),
            validation.UserId,
            channel.Id,
            ct
        );
        return await moderatorStatus.Match<Task<FollowerOnlyChatReadiness>>(
            _ =>
                Task.FromResult<FollowerOnlyChatReadiness>(
                    UnableToVerify(FollowerOnlyChatVerificationFailure.ModeratorCheckUnavailable)
                ),
            _ =>
                Task.FromResult<FollowerOnlyChatReadiness>(
                    UnableToVerify(FollowerOnlyChatVerificationFailure.ModeratorCheckUnavailable)
                ),
            _ =>
                Task.FromResult<FollowerOnlyChatReadiness>(
                    UnableToVerify(FollowerOnlyChatVerificationFailure.ModeratorCheckUnavailable)
                ),
            _ =>
                Task.FromResult<FollowerOnlyChatReadiness>(
                    new FollowerOnlyChatReadiness.Exempt(FollowerOnlyChatExemption.Moderator)
                ),
            _ =>
                EvaluateNonModeratorAsync(
                    channel.Id,
                    accessToken,
                    validation.UserId,
                    scopes,
                    chatSettings,
                    ct
                )
        );
    }

    private async Task<FollowerOnlyChatReadiness> EvaluateNonModeratorAsync(
        string channelId,
        string accessToken,
        string botUserId,
        ISet<string> grantedScopes,
        ChatSettings chatSettings,
        CancellationToken ct
    )
    {
        if (!grantedScopes.Contains(Scopes.UserReadFollows))
        {
            return UnableToVerify(FollowerOnlyChatVerificationFailure.MissingFollowReadScope);
        }

        var followStatus = await helix.GetFollowedChannelStatusAsync(
            HelixContext(accessToken),
            botUserId,
            channelId,
            ct
        );
        return followStatus.Match<FollowerOnlyChatReadiness>(
            follows =>
                ReadinessFromFollow(follows.FollowedAtUtc, chatSettings.FollowerModeDuration),
            _ => new FollowerOnlyChatReadiness.NotFollowing(),
            _ => UnableToVerify(FollowerOnlyChatVerificationFailure.FollowReadUnavailable)
        );
    }

    private FollowerOnlyChatReadiness ReadinessFromFollow(
        DateTimeOffset followedAtUtc,
        TimeSpan? minimumFollowDuration
    )
    {
        var eligibleAtUtc = followedAtUtc + (minimumFollowDuration ?? TimeSpan.Zero);
        return eligibleAtUtc > timeProvider.GetUtcNow()
            ? new FollowerOnlyChatReadiness.WaitingUntil(eligibleAtUtc)
            : new FollowerOnlyChatReadiness.EligibleNow();
    }

    private async Task<HelixUser?> FindChannelAsync(
        string appToken,
        string channelLogin,
        CancellationToken ct
    )
    {
        var channels = await helix.GetUsersByLoginAsync(
            HelixContext(appToken),
            [Login.Normalize(channelLogin)],
            ct
        );
        return channels.SingleOrDefault();
    }

    private HelixRequestContext HelixContext(string accessToken) =>
        new(settings.Identity.ClientId, accessToken);

    private static FollowerOnlyChatReadiness.UnableToVerify UnableToVerify(
        FollowerOnlyChatVerificationFailure failure
    ) => new(failure);
}
