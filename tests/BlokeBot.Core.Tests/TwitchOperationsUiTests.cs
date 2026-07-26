using System.Collections.Immutable;
using System.Net;
using System.Security.Claims;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Runtime;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class TwitchOperationsUiTests
{
    [Test]
    public async Task StaleSelectedHost_SavingPollTemplate_DoesNotMutateCachedHost()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstHost = await SeedHostAsync(dbFactory, "first", "first-id");
        var secondHost = await SeedHostAsync(dbFactory, "second", "second-id");
        var testContext = UiTestContextFactory.CreateWithAuthorization(
            dbFactory,
            firstHost.Id,
            firstHost.Login
        );
        await using var context = testContext.Context;
        ConfigureServices(context, dbFactory);
        var page = context.Render<ShoutoutsPage>();
        page.WaitForAssertion(() =>
            page.FindAll("button")
                .Any(button => button.TextContent.Trim() == "Save template")
                .ShouldBeTrue()
        );
        SetSelectedHostClaims(testContext.Authorization, secondHost);

        page.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Save template")
            .Click();

        page.WaitForAssertion(() =>
            context
                .Services.GetRequiredService<ToastService>()
                .Current.ShouldHaveSingleItem()
                .Title.ShouldBe("Channel not selected")
        );
        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.TwitchPollTemplates.CountAsync()).ShouldBe(0);
    }

    private static void ConfigureServices(BunitContext context, SqliteBlokeBotDbFactory dbFactory)
    {
        var events = TestEventBus.Create<AppEventKind>();
        var changes = new HostedChannelChangeNotifier(events);
        var alerts = new BlokeBot.Core.Features.Alerts.DurableAlertService(
            dbFactory,
            TimeProvider.System,
            events
        );
        var settings = BotSettings.FromOptions(
            new BotOptions { Identity = new BotIdentityOptions { ClientId = "client" } }
        );
        context.Services.AddSingleton(events);
        context.Services.AddSingleton(changes);
        context.Services.AddSingleton(alerts);
        context.Services.AddSingleton(
            new ShoutoutService(dbFactory, null!, null!, null!, events, TimeProvider.System)
        );
        context.Services.AddSingleton(
            new PollService(
                dbFactory,
                new UnavailableBroadcasterProvider(),
                new HelixClient(new RejectingHttpClientFactory()),
                settings,
                events,
                alerts
            )
        );
        context.Services.AddSingleton(
            new ModeratorAuthorityService(
                null!,
                new HelixClient(new RejectingHttpClientFactory()),
                settings,
                new HostModAccessService(dbFactory, changes),
                TimeProvider.System
            )
        );
    }

    private static async Task<BotHost> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login,
        string twitchUserId
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            TwitchUserId = twitchUserId,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host;
    }

    private static void SetSelectedHostClaims(
        Bunit.TestDoubles.BunitAuthorizationContext authorization,
        BotHost host
    )
    {
        var choice = new BotHostChoice(host.Id, host.Login, host.DisplayName, AuthRole.Streamer);
        authorization.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "first-id"),
            new Claim(ClaimTypes.Name, "first"),
            new Claim(AuthClaims.Login, "first"),
            new Claim(AuthClaims.Role, AuthRoleCodec.Encode(AuthRole.Streamer)),
            new Claim(AuthClaims.CanCreateHost, "false"),
            new Claim(AuthClaims.IsBotAdmin, "false"),
            new Claim(AuthClaims.IsBotAccount, "false"),
            new Claim(BotHostClaims.AvailableHost, BotHostClaimCodec.Encode(choice)),
            new Claim(BotHostClaims.SelectedHost, BotHostClaimCodec.Encode(choice))
        );
    }

    private sealed class UnavailableBroadcasterProvider : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        )
        {
            return Task.FromResult<TokenStatus>(
                new TokenStatus.Unavailable(
                    AccessTokenUnavailableReason.MissingRefreshToken,
                    ImmutableArray.CreateRange(requiredScopes.Select(x => x!))
                )
            );
        }

        public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(
            string channelLogin
        )
        {
            return IO<BotAccount, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                    )
                )
            );
        }
    }

    private sealed class RejectingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new(new RejectingHandler());
        }

        private sealed class RejectingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }
    }
}
