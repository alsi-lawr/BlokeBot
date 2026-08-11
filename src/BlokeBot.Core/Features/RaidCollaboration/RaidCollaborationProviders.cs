using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.RaidCollaboration;

internal sealed class TwitchRaidCollaborationProvider(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IHostBroadcasterTokenStatusProvider broadcasters,
    HelixClient helix,
    BotSettings botSettings,
    TimeProvider clock
) : IRaidCollaborationProvider
{
    internal static readonly TimeSpan ApprovedClipMaximumAge = TimeSpan.FromDays(30);

    public async Task<RaidChannelSnapshotOutcome> LoadLiveChannelAsync(
        int hostId,
        string login,
        string? approvedClipId,
        CancellationToken cancellationToken
    )
    {
        if (!await FeatureEnabledAsync(hostId, cancellationToken))
        {
            return new RaidChannelSnapshotOutcome.Unavailable();
        }
        var context = await ProviderContextAsync(hostId, [], cancellationToken);
        if (context is null)
        {
            return new RaidChannelSnapshotOutcome.Unavailable();
        }

        try
        {
            var users = await helix.GetUsersByLoginAsync(context, [login], cancellationToken);
            if (users.FirstOrDefault() is not { } user)
            {
                return new RaidChannelSnapshotOutcome.NotFound(Login.Normalize(login));
            }
            var stream = await helix.GetStreamAsync(context, user.Login, cancellationToken);
            if (stream is null || stream.ViewerCount <= 0)
            {
                return new RaidChannelSnapshotOutcome.Offline(user.Login);
            }
            var clip = await LoadApprovedClipAsync(
                context,
                user.Id,
                approvedClipId,
                cancellationToken
            );
            return new RaidChannelSnapshotOutcome.Available(
                new(
                    user.Id,
                    user.Login,
                    user.DisplayName,
                    stream.Id,
                    stream.GameName,
                    stream.Language,
                    stream.Title,
                    stream.ViewerCount,
                    clip
                )
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or TimeoutException)
        {
            return new RaidChannelSnapshotOutcome.Unavailable();
        }
    }

    public async Task<ConfirmedRaidStartOutcome> StartConfirmedRaidAsync(
        int hostId,
        string targetTwitchUserId,
        string targetLogin,
        CancellationToken cancellationToken
    )
    {
        var context = await ProviderContextAsync(
            hostId,
            HostBroadcasterAuthorizationService.RaidManagementScopes,
            cancellationToken
        );
        if (context is null)
        {
            return new ConfirmedRaidStartOutcome.AuthorizationRequired();
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var broadcasterId = await db
            .Hosts.AsNoTracking()
            .Where(host => host.Id == hostId)
            .Select(host => host.TwitchUserId)
            .SingleOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(broadcasterId)
                ? new ConfirmedRaidStartOutcome.AuthorizationRequired()
            : !await FeatureEnabledAsync(hostId, cancellationToken)
                ? new ConfirmedRaidStartOutcome.FeatureDisabled()
            : await helix.StartRaidAsync(
                context,
                broadcasterId,
                targetTwitchUserId,
                cancellationToken
            ) switch
            {
                HelixRaidStartOutcome.Started => new ConfirmedRaidStartOutcome.Started(targetLogin),
                HelixRaidStartOutcome.Unauthorized =>
                    new ConfirmedRaidStartOutcome.AuthorizationRequired(),
                _ => new ConfirmedRaidStartOutcome.ProviderRejected(),
            };
    }

    public async Task<bool> HasRaidManagementAuthorizationAsync(
        int hostId,
        CancellationToken cancellationToken
    ) =>
        await FeatureEnabledAsync(hostId, cancellationToken)
        && await broadcasters.GetTokenStatusAsync(
            hostId,
            HostBroadcasterAuthorizationService.RaidManagementScopes,
            cancellationToken
        ) is TokenStatus.Ready;

    private async Task<HelixRequestContext?> ProviderContextAsync(
        int hostId,
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken
    ) =>
        await broadcasters.GetTokenStatusAsync(hostId, scopes, cancellationToken)
            is TokenStatus.Ready ready
            ? new HelixRequestContext(botSettings.Identity.ClientId, ready.AccessToken)
            : null;

    private async Task<bool> FeatureEnabledAsync(int hostId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .AnyAsync(
                host =>
                    host.Id == hostId
                    && (host.EnabledFeatures & HostFeatureFlags.RaidCollaboration)
                        == HostFeatureFlags.RaidCollaboration,
                cancellationToken
            );
    }

    private async Task<ApprovedRaidClip?> LoadApprovedClipAsync(
        HelixRequestContext context,
        string broadcasterId,
        string? approvedClipId,
        CancellationToken cancellationToken
    ) =>
        string.IsNullOrWhiteSpace(approvedClipId)
            ? null
            : await helix.GetClipAsync(context, approvedClipId, cancellationToken) switch
            {
                HelixClipLookupOutcome.Found found
                    when found.Clip.BroadcasterId == broadcasterId
                        && found.Clip.CreatedAt >= clock.GetUtcNow() - ApprovedClipMaximumAge =>
                    new(
                        found.Clip.Id,
                        found.Clip.Url,
                        found.Clip.Title,
                        found.Clip.CreatedAt,
                        found.Clip.DurationSeconds
                    ),
                _ => null,
            };
}

internal sealed class RaidWelcomeSender(
    IPublicChatMessageSender chat,
    IDbContextFactory<BlokeBotDbContext> dbFactory
) : IRaidWelcomeSender
{
    public async Task<bool> SendAsync(
        int hostId,
        string hostLogin,
        string providerMessageId,
        string message,
        CancellationToken cancellationToken
    )
    {
        if (!await FeatureEnabledAsync(hostId, cancellationToken))
        {
            return false;
        }
        var outcome = await chat.SendCorrelatedAsync(
            hostLogin,
            message,
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            new PublicChatDeliveryCorrelation(hostId, $"raid-collaboration:{providerMessageId}"),
            cancellationToken
        );
        return outcome.Match(static _ => true, static _ => false);
    }

    private async Task<bool> FeatureEnabledAsync(int hostId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .AnyAsync(
                host =>
                    host.Id == hostId
                    && (host.EnabledFeatures & HostFeatureFlags.RaidCollaboration)
                        == HostFeatureFlags.RaidCollaboration,
                cancellationToken
            );
    }
}

internal sealed class RaidCollaborationShoutoutProvider(
    IShoutoutDashboardOperations shoutouts,
    IDbContextFactory<BlokeBotDbContext> dbFactory
) : IRaidCollaborationShoutoutProvider
{
    public async Task<ShoutoutOperationOutcome> SendAsync(
        int hostId,
        string targetLogin,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var enabled = await db
            .Hosts.AsNoTracking()
            .AnyAsync(
                host =>
                    host.Id == hostId
                    && (host.EnabledFeatures & HostFeatureFlags.RaidCollaboration)
                        == HostFeatureFlags.RaidCollaboration,
                cancellationToken
            );
        return enabled
            ? await shoutouts.SendAsync(hostId, targetLogin, cancellationToken)
            : new ShoutoutOperationOutcome.NotReady("Raid & collaboration is turned off.");
    }
}
