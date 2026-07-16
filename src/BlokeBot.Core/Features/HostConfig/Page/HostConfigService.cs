using System.Diagnostics;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Core.Features.SiteAccess;
using BlokeBot.Core.Hosts;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostConfig.Page;

public sealed class HostConfigService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostModAccessService modAccess,
    HostBotAccountAuthorizationService botAccounts,
    WhisperQuotaService whisperQuota,
    HostedChannelRuntimeStatusService runtimeStatus,
    SiteAccessService siteAccess
)
{
    public IO<Option<HostConfigState>, Never> Load(AuthenticatedSession session)
    {
        return IO<Option<HostConfigState>, Never>.Create(async ct =>
            Result<Option<HostConfigState>, Never>.Success(await LoadStateAsync(session, ct))
        );
    }

    private async Task<Option<HostConfigState>> LoadStateAsync(
        AuthenticatedSession session,
        CancellationToken ct
    )
    {
        var login = session.Login;
        if (string.IsNullOrWhiteSpace(login))
        {
            return Option<HostConfigState>.None;
        }

        var canCreateHost = await siteAccess.CanCreateHostAsync(login, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.AsNoTracking().SingleOrDefaultAsync(x => x.Login == login, ct);
        if (host is null)
        {
            return Option<HostConfigState>.Some(
                new HostConfigState(
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
                )
            );
        }

        var statusResult = await runtimeStatus.LoadHostRuntimeSummary(host.Id).ExecuteAsync(ct);
        var status = statusResult.Match(
            option => option.Match<HostedChannelRuntimeSummary?>(value => value, () => null),
            _ => throw new UnreachableException()
        );
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
        return Option<HostConfigState>.Some(
            new HostConfigState(
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
            )
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
