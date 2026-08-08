using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomationEventsPageUiTests
{
    [Test]
    public async Task DisabledAutomations_ShowDisabledRecoveryWithAChannelSetupLink()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, HostFeatureFlags.CustomCommands);
        await using var context = CreateContext(
            dbFactory,
            hostId,
            new TokenStatus.Unavailable(
                AccessTokenUnavailableReason.MissingRefreshToken,
                [.. HostBroadcasterAuthorizationService.MilestoneScopes]
            )
        );

        var cut = context.Render<AutomationEventsPage>();

        cut.Markup.ShouldContain("Automations are off");
        cut.Markup.ShouldContain("Suppressed events are not replayed");
        cut.Find("[data-automation-events-channel-setup]")
            .GetAttribute("href")
            .ShouldBe("/host#chat-tools");
        cut.FindAll("[data-automation-event-sources]").ShouldBeEmpty();
    }

    [Test]
    public async Task MissingScopes_SurfaceExactScopesAndTheExistingReconnectAction()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(
            dbFactory,
            HostFeatureFlags.Automations | HostFeatureFlags.CustomCommands
        );
        await using var context = CreateContext(
            dbFactory,
            hostId,
            new TokenStatus.MissingScopes(
                "token",
                new("streamer-id", "streamer", OAuthScopeSet.Empty),
                [.. HostBroadcasterAuthorizationService.MilestoneScopes],
                [],
                ["bits:read"]
            )
        );

        var cut = context.Render<AutomationEventsPage>();

        cut.Markup.ShouldContain("Connect this channel again");
        cut.Markup.ShouldContain("bits:read");
        cut.Markup.ShouldContain("Reconnect to Twitch");
        cut.Find("[data-automation-event-source='cheer']")
            .GetAttribute("data-source-state")
            .ShouldBe("missing-scopes");
        cut.Find("[data-automation-event-source='subscription']")
            .GetAttribute("data-source-state")
            .ShouldBe("ready");
        cut.Find("[data-automation-event-source='follow']")
            .GetAttribute("data-source-state")
            .ShouldBe("ready");
    }

    [Test]
    public async Task ReadyBroadcaster_ListsEveryTypedSourceWithoutAReconnectPanel()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(
            dbFactory,
            HostFeatureFlags.Automations | HostFeatureFlags.CustomCommands
        );
        await using var context = CreateContext(
            dbFactory,
            hostId,
            new TokenStatus.Ready(
                "token",
                new("streamer-id", "streamer", OAuthScopeSet.Empty),
                [.. HostBroadcasterAuthorizationService.MilestoneScopes],
                [.. HostBroadcasterAuthorizationService.MilestoneScopes]
            )
        );

        var cut = context.Render<AutomationEventsPage>();

        cut.Markup.ShouldNotContain("Connect this channel again");
        cut.FindAll("[data-automation-event-source]").Count.ShouldBe(12);
        cut.Find("[data-automation-event-source='chat-notification']")
            .TextContent.ShouldContain("Ordinary chat messages never start automations");
        cut.Find("[data-automation-events-editor-note]")
            .TextContent.ShouldContain(
                "Tools for building and editing flows arrive in a later release"
            );
    }

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
