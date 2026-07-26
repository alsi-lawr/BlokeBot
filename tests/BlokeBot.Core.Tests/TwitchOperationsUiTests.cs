using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Features.Alerts;
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
    public async Task PollHub_ChannelPointsAndProviderRefresh_RenderProgressFinalResultsAndExternalGuard()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(dbFactory, "streamer", "streamer-id");
        var testContext = UiTestContextFactory.CreateWithAuthorization(
            dbFactory,
            host.Id,
            host.Login
        );
        await using var context = testContext.Context;
        var events = ConfigureServices(context, dbFactory);
        context.Services.GetRequiredService<EventBus<AppEventKind>>().ShouldBeSameAs(events);
        var page = context.Render<ShoutoutsPage>();
        page.WaitForAssertion(() =>
            page.FindAll("button")
                .Any(button => button.TextContent.Trim() == "Save template")
                .ShouldBeTrue()
        );

        page.Find("#poll-title").Input("Channel Points question");
        page.Find("#poll-choices").Input("Yes\nNo");
        page.Find("#poll-duration").Input("60");
        page.Find("#poll-channel-points-enabled").Change(true);
        page.Find("#poll-channel-points-per-vote").Change("250");
        page.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Save template")
            .Click();

        page.WaitForAssertion(() => page.Markup.ShouldContain("250 Channel Points per vote"));
        await using (var verify = await dbFactory.CreateDbContextAsync())
        {
            var template = (await verify.TwitchPollTemplates.ToArrayAsync()).ShouldHaveSingleItem();
            template.ChannelPointsVotingEnabled.ShouldBeTrue();
            template.ChannelPointsPerVote.ShouldBe(250);
        }

        await SetPollAsync(
            dbFactory,
            host.Id,
            TwitchPollStatus.Active,
            votes: 7,
            channelPointsVotes: 3,
            externallyStarted: true
        );
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, CancellationToken.None);

        page.WaitForAssertion(() =>
        {
            page.Markup.ShouldContain("7 votes (3 Channel Points)");
            page.Markup.ShouldContain("This poll was started in Twitch. Confirm before ending it.");
            page.FindAll("button")
                .Any(button => button.TextContent.Trim() == "End poll")
                .ShouldBeTrue();
        });
        page.FindAll("button").Single(button => button.TextContent.Trim() == "End poll").Click();
        context
            .JSInterop.Invocations.Any(invocation => invocation.Identifier == "confirm")
            .ShouldBeTrue();

        await SetPollAsync(
            dbFactory,
            host.Id,
            TwitchPollStatus.Completed,
            votes: 9,
            channelPointsVotes: 4,
            externallyStarted: true
        );
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, CancellationToken.None);

        page.WaitForAssertion(() =>
        {
            page.Markup.ShouldContain("Recent results");
            page.Markup.ShouldContain("Completed");
            page.Markup.ShouldContain("9 votes (4 Channel Points)");
            page.FindAll("button")
                .Any(button => button.TextContent.Trim() == "End poll")
                .ShouldBeFalse();
        });
    }

    private static EventBus<AppEventKind> ConfigureServices(
        BunitContext context,
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        var events = TestEventBus.Create<AppEventKind>();
        var changes = new HostedChannelChangeNotifier(events);
        var alerts = new DurableAlertService(dbFactory, TimeProvider.System, events);
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
                new ReadyBroadcasterProvider(),
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
        return events;
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

    private static async Task SetPollAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        TwitchPollStatus status,
        int votes,
        int channelPointsVotes,
        bool externallyStarted
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var poll = await db.TwitchPolls.SingleOrDefaultAsync(item => item.HostId == hostId);
        if (poll is null)
        {
            poll = new TwitchPoll { HostId = hostId, ProviderPollId = "poll-id" };
            db.TwitchPolls.Add(poll);
        }

        poll.Title = "Provider question";
        poll.ChoicesJson = JsonSerializer.Serialize(
            new[] { new PollChoiceView("yes", "Yes", votes, channelPointsVotes) }
        );
        poll.Status = status;
        poll.IsExternallyStarted = externallyStarted;
        poll.StartedAtUtc = new DateTime(2026, 7, 26, 10, 0, 0, DateTimeKind.Utc);
        poll.EndsAtUtc = new DateTime(2026, 7, 26, 10, 1, 0, DateTimeKind.Utc);
        poll.EndedAtUtc = status is TwitchPollStatus.Active ? null : DateTime.UtcNow;
        poll.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private sealed class ReadyBroadcasterProvider : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        )
        {
            return Task.FromResult<TokenStatus>(
                new TokenStatus.Ready(
                    "broadcaster-token",
                    new TokenValidation(
                        "streamer-id",
                        "streamer",
                        OAuthScopeSet.Create(HostBroadcasterAuthorizationService.MilestoneScopes)
                    ),
                    ImmutableArray.CreateRange(HostBroadcasterAuthorizationService.MilestoneScopes),
                    ImmutableArray.CreateRange(HostBroadcasterAuthorizationService.MilestoneScopes)
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
