using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlokeBot.Core.Tests;

public sealed class AutomationEventsPageUiTests
{
    private static BunitContext CreateContext(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        TokenStatus tokenStatus
    )
    {
        var context = UiTestContextFactory.Create(dbFactory, hostId);
        _ = context.Services.AddSingleton<IPublicChatMessageSender>(new IgnoredChatSender());
        _ = context.Services.AddSingleton<IOverlayCueAdmissionService>(new NoOverlayCues());
        _ = context.Services.AddSingleton<IHostBroadcasterTokenStatusProvider>(
            new FixedBroadcasterTokens(tokenStatus)
        );
        _ = context.Services.AddBlokeBotAutomations();
        return context;
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        HostFeatureFlags enabledFeatures
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "streamer-id",
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = enabledFeatures,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class FixedBroadcasterTokens(TokenStatus status)
        : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        ) => Task.FromResult(status);

        public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(
            string channelLogin
        ) =>
            IO<BotAccount, AccessTokenUnavailableReason>.Create(static _ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                    )
                )
            );
    }

    private sealed class IgnoredChatSender : IPublicChatMessageSender
    {
        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<PublicChatSendOutcome>(new PublicChatSendOutcome.Accepted());
    }

    private sealed class NoOverlayCues : IOverlayCueAdmissionService
    {
        public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
            OverlayCueReferenceRequest request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<OverlayCueReferenceOutcome>(
                new OverlayCueReferenceOutcome.Missing(OverlayCueReferencePart.Cue)
            );

        public Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(new OverlayCueAdmissionCatalog([], []));

        public Task<OverlayCueAdmissionOutcome> AdmitAsync(
            OverlayCueAdmissionRequest request,
            CancellationToken cancellationToken
        ) => Task.FromResult<OverlayCueAdmissionOutcome>(new OverlayCueAdmissionOutcome.Missing());
    }
}
