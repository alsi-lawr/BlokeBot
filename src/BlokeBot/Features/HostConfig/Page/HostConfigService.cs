using BlokeBot.Auth.Sessions;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.HostedChannels;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Whispers;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.HostConfig.Page;

public sealed class HostConfigService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostModAccessService modAccess,
    HostBotAccountAuthorizationService botAccounts,
    WhisperQuotaService whisperQuota,
    HostedChannelRuntimeStatusService runtimeStatus,
    SiteAccessService siteAccess
)
{
    public async Task<HostConfigState?> LoadAsync(
        AuthenticatedSession session,
        CancellationToken ct
    )
    {
        var login = session.Login;
        if (string.IsNullOrWhiteSpace(login))
        {
            return null;
        }

        var canCreateHost = await siteAccess.CanCreateHostAsync(login, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.AsNoTracking().SingleOrDefaultAsync(x => x.Login == login, ct);
        if (host is null)
        {
            return new HostConfigState(
                null,
                login,
                string.IsNullOrWhiteSpace(session.DisplayName) ? login : session.DisplayName,
                session.ProfileImageUrl,
                canCreateHost,
                false,
                false,
                null,
                new HostBotAccountOverrideState(
                    false,
                    DisabledBotOverrideStatus(),
                    false,
                    new WhisperQuotaStatus(0, WhisperQuotaService.UniqueRecipientLimit, false)
                ),
                [],
                new HostModAccessState(true, true, [], [])
            );
        }

        var status = await runtimeStatus.LoadHostRuntimeSummaryAsync(host.Id, ct);
        var botOverrideStatus = await botAccounts.GetStatusAsync(host.Id, ct);
        var botOverrideSettings = await db
            .HostBotAccountSettings.AsNoTracking()
            .Where(x => x.HostId == host.Id)
            .Select(x => new
            {
                x.OverrideEnabled,
                x.WhisperResponsesEnabled,
                x.TwitchUserId,
            })
            .SingleOrDefaultAsync(ct);
        var whisperQuotaStatus = await whisperQuota.GetStatusAsync(
            host.Id,
            botOverrideSettings?.TwitchUserId,
            ct
        );
        return new HostConfigState(
            host.Id,
            host.Login,
            host.DisplayName,
            host.ProfileImageUrl,
            canCreateHost,
            true,
            host.ChannelBotAuthorizedAtUtc is not null,
            status,
            new HostBotAccountOverrideState(
                botOverrideStatus.State != BotAccountAuthorizationState.Disabled,
                botOverrideStatus,
                botOverrideSettings?.OverrideEnabled == true
                    && botOverrideSettings.WhisperResponsesEnabled,
                whisperQuotaStatus
            ),
            HostFeatureCatalog.Cards(host.EnabledFeatures),
            await modAccess.LoadAsync(host.Id, ct)
        );
    }

    private static BotAccountAuthorizationStatus DisabledBotOverrideStatus()
    {
        return new(
            null,
            null,
            null,
            BotAccountAuthorizationState.Disabled,
            [],
            [],
            [],
            "Create your channel setup before using a custom bot."
        );
    }
}
