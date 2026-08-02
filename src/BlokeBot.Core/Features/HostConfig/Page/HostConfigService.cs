using System.Diagnostics;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostConfig.StartupMessage;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Core.Features.SiteAccess;
using BlokeBot.Core.Hosts;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostConfig.Page;

public sealed class HostConfigService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostModAccessService modAccess,
    HostBotAccountAuthorizationService botAccounts,
    IHostBroadcasterTokenStatusProvider broadcasters,
    WhisperQuotaService whisperQuota,
    HostedChannelRuntimeStatusService runtimeStatus,
    SiteAccessService siteAccess,
    StartupMessageConfigurationService startupMessages,
    CommandsConfigurationService commands
)
{
    public IO<Option<HostConfigState>, Never> Load(AuthenticatedSession session) =>
        IO<Option<HostConfigState>, Never>.Create(async ct =>
            Result<Option<HostConfigState>, Never>.Success(await LoadStateAsync(session, ct))
        );

    private async Task<Option<HostConfigState>> LoadStateAsync(
        AuthenticatedSession session,
        CancellationToken ct
    )
    {
        var selectedHost = session.State.Match<BotHostChoice?>(
            _ => null,
            selected => selected.Selection.Current,
            _ => null
        );
        if (selectedHost is not null && session.CanManageSelectedHostConfig)
        {
            await using var selectedHostDb = await dbFactory.CreateDbContextAsync(ct);
            var selected = await selectedHostDb
                .Hosts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == selectedHost.Id, ct);
            return selected is null
                ? Option<HostConfigState>.None
                : Option<HostConfigState>.Some(
                    await LoadCreatedHostStateAsync(selectedHostDb, selected, false, ct)
                );
        }

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
                    TwitchOperationsAuthorizationState.Missing,
                    null,
                    new StartupMessageConfiguration(false, string.Empty),
                    new CommandsConfiguration(string.Empty, null),
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

        return Option<HostConfigState>.Some(
            await LoadCreatedHostStateAsync(db, host, canCreateHost, ct)
        );
    }

    private async Task<HostConfigState> LoadCreatedHostStateAsync(
        BlokeBotDbContext db,
        BotHost host,
        bool canCreateHost,
        CancellationToken ct
    )
    {
        var statusResult = await runtimeStatus.LoadHostRuntimeSummary(host.Id).ExecuteAsync(ct);
        var status = statusResult.Match(
            option => option.Match<HostedChannelRuntimeSummary?>(value => value, () => null),
            _ => throw new UnreachableException()
        );
        var botOverrideStatus = await botAccounts.GetStatusAsync(host.Id, ct);
        var broadcasterStatus = await broadcasters.GetTokenStatusAsync(
            host.Id,
            HostBroadcasterAuthorizationService.MilestoneScopes,
            ct
        );
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
            TwitchOperationsAuthorization(broadcasterStatus),
            status,
            startupMessages.EffectiveConfiguration(host),
            await commands.LoadAsync(host.Id, ct),
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

    private static TwitchOperationsAuthorizationState TwitchOperationsAuthorization(
        TokenStatus status
    ) =>
        status switch
        {
            TokenStatus.Ready => TwitchOperationsAuthorizationState.Ready,
            TokenStatus.Unavailable { Reason: AccessTokenUnavailableReason.MissingRefreshToken } =>
                TwitchOperationsAuthorizationState.Missing,
            _ => TwitchOperationsAuthorizationState.Stale,
        };

    private static BotAccountAuthorizationStatus DisabledBotOverrideStatus() =>
        new(
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
