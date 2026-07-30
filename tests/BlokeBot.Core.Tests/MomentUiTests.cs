using System.Security.Claims;
using AngleSharp.Dom;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Moments;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class MomentUiTests
{
    [Test]
    public async Task ModeratorPage_KeepsWeeklyRecapInANewTabAndUsesSemanticSettingsAlignment()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var service = new MomentHubService(
            database,
            new UnusedMomentProvider(),
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        await using var context = UiTestContextFactory.Create(database, hostId);
        context.Services.AddSingleton(service);
        context.Services.AddSingleton<IHostStreamLivenessProvider>(
            new OfflineStreamLivenessProvider()
        );

        var page = context.Render<MomentsPage>();

        page.WaitForAssertion(() =>
        {
            var recap = page.Find("a[aria-label='Open weekly recap (opens in a new tab)']");
            recap.TextContent.Trim().ShouldBe("Open weekly recap");
            recap.GetAttribute("href").ShouldBe("/moments/streamer");
            recap.GetAttribute("target").ShouldBe("_blank");
            recap.GetAttribute("rel").ShouldBe("noopener");
            recap.Closest(".page-header__actions").ShouldNotBeNull();
            page.Find("#moment-marker-fallback").ShouldNotBeNull();
            page.Find("label[for='moment-marker-fallback']")
                .TextContent.ShouldContain("Use a stream marker");
            page.Find(".moment-setting-toggle").ShouldNotBeNull();
            page.Markup.ShouldNotContain("pt-6");
        });
    }

    [Test]
    public async Task ModeratorPage_WithoutSelectedHost_OmitsTheWeeklyRecapNavigation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var service = new MomentHubService(
            database,
            new UnusedMomentProvider(),
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        var testContext = UiTestContextFactory.CreateWithAuthorization(database, hostId);
        await using var context = testContext.Context;
        testContext.Authorization.SetNotAuthorized();
        context.Services.AddSingleton(service);
        context.Services.AddSingleton<IHostStreamLivenessProvider>(
            new OfflineStreamLivenessProvider()
        );

        var page = context.Render<MomentsPage>();

        page.WaitForAssertion(() =>
            page.Markup.ShouldContain("Choose a channel to manage moments")
        );
        page.FindAll("a[aria-label^='Open weekly recap']").ShouldBeEmpty();
        page.FindAll("a[href='#']").ShouldBeEmpty();
    }

    [Test]
    public async Task PublicRecap_RendersApprovedTwitchLinkAndNeverPrivateModerationText()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        int clipId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                TwitchUserId = "streamer-id",
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Hosts.Add(host);
            await db.SaveChangesAsync();
            hostId = host.Id;
            var clip = new TwitchClip
            {
                HostId = hostId,
                IdempotencyKey = "ui-clip",
                Status = TwitchClipStatus.Available,
                FinalUrl = "https://clips.twitch.tv/PublicMoment",
                RequestedAtUtc = DateTime.UtcNow,
                ResolvedAtUtc = DateTime.UtcNow,
            };
            db.TwitchClips.Add(clip);
            await db.SaveChangesAsync();
            clipId = clip.Id;
        }
        var service = new MomentHubService(
            database,
            new ReadyProvider(clipId),
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        var captured = (
            await service.CaptureAsync(
                hostId,
                new CaptureMomentCommand("stream-id", new("viewer", "viewer-id"), "Public title"),
                CancellationToken.None
            )
        ).Match(
            succeeded => succeeded.Value,
            rejected => throw new InvalidOperationException(rejected.Reason.Message)
        );
        _ = await service.ApproveAsync(
            hostId,
            new ModerateMomentCommand(
                captured.PublicId,
                "Public title",
                "Gameplay",
                "moderator",
                "PRIVATE-MODERATOR-NOTE"
            ),
            CancellationToken.None
        );
        using var context = new BunitContext();
        context.Services.AddSingleton(service);

        var page = context.Render<PublicMomentRecapPage>(parameters =>
            parameters.Add(component => component.Channel, "streamer")
        );

        page.WaitForAssertion(() => page.Find("h1").TextContent.ShouldBe("Weekly recap"));
        page.Markup.ShouldContain("Public title");
        page.Find("a[href='https://clips.twitch.tv/PublicMoment']").ShouldNotBeNull();
        page.Markup.ShouldNotContain("PRIVATE-MODERATOR-NOTE");
        page.Find("input#moment-voter-login").GetAttribute("maxlength").ShouldBe("128");
    }

    [Test]
    public void Routes_DeclareModeratorAndPublicAuthorizationAudiences()
    {
        var moderator = typeof(MomentsPage)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .ShouldHaveSingleItem();
        var publicRoute = typeof(PublicMomentRecapPage)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), true)
            .ShouldHaveSingleItem();

        moderator.Policy.ShouldBe("HostSelected");
        publicRoute.ShouldNotBeNull();
    }

    [Test]
    public async Task PublicRecap_AuthenticatedVoteThenLoginFallbackIsOneLogicalVote()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        int clipId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                Login = "streamer",
                DisplayName = "Streamer",
                TwitchUserId = "streamer-id",
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Hosts.Add(host);
            await db.SaveChangesAsync();
            hostId = host.Id;
            var clip = new TwitchClip
            {
                HostId = hostId,
                IdempotencyKey = "identity-ui-clip",
                Status = TwitchClipStatus.Available,
                FinalUrl = "https://clips.twitch.tv/IdentityMoment",
                RequestedAtUtc = DateTime.UtcNow,
                ResolvedAtUtc = DateTime.UtcNow,
            };
            db.TwitchClips.Add(clip);
            await db.SaveChangesAsync();
            clipId = clip.Id;
        }
        var service = new MomentHubService(
            database,
            new ReadyProvider(clipId),
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        var captured = (
            await service.CaptureAsync(
                hostId,
                new CaptureMomentCommand("stream-id", new("viewer"), "Identity moment"),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<MomentResult<MomentView>.Succeeded>()
            .Value;
        _ = await service.ApproveAsync(
            hostId,
            new ModerateMomentCommand(
                captured.PublicId,
                "Identity moment",
                "Gameplay",
                "moderator"
            ),
            CancellationToken.None
        );

        using (var authenticated = new BunitContext())
        {
            authenticated.Services.AddSingleton(service);
            var authorization = authenticated.AddAuthorization();
            authorization.SetAuthorized("OAuth Viewer");
            authorization.SetClaims(
                new Claim(ClaimTypes.NameIdentifier, "oauth-viewer-id"),
                new Claim(ClaimTypes.Name, "OAuth Viewer"),
                new Claim(AuthClaims.Login, "oauth_viewer")
            );
            var page = authenticated.Render<PublicMomentRecapPage>(parameters =>
                parameters.Add(component => component.Channel, "streamer")
            );
            page.WaitForAssertion(() => page.Markup.ShouldContain("Voting as"));
            await page.Find("button.btn-secondary").ClickAsync(new());
            page.WaitForAssertion(() => page.Markup.ShouldContain("Vote recorded."));
        }

        using (var anonymous = new BunitContext())
        {
            anonymous.Services.AddSingleton(service);
            _ = anonymous.AddAuthorization();
            var page = anonymous.Render<PublicMomentRecapPage>(parameters =>
                parameters.Add(component => component.Channel, "streamer")
            );
            page.WaitForAssertion(() => page.Find("#moment-voter-login").ShouldNotBeNull());
            page.Find("#moment-voter-login").Change("oauth_viewer");
            await page.Find("button.btn-secondary").ClickAsync(new());
            page.WaitForAssertion(() =>
                page.Markup.ShouldContain("Your vote was already recorded.")
            );
        }

        await using var verify = await database.CreateDbContextAsync();
        var vote = await verify.MomentVotes.SingleAsync();
        vote.IdentityKey.ShouldBe("id:oauth-viewer-id");
        vote.TwitchUserId.ShouldBe("oauth-viewer-id");
    }

    private sealed class ReadyProvider(int clipId) : IMomentProviderOperations
    {
        public Task<MomentProviderOutcome> CaptureAsync(
            int hostId,
            Guid publicId,
            bool markerFallbackEnabled,
            string description,
            CancellationToken ct
        )
        {
            return Task.FromResult<MomentProviderOutcome>(
                new MomentProviderOutcome.ClipReady(clipId)
            );
        }
    }

    private sealed class UnusedMomentProvider : IMomentProviderOperations
    {
        public Task<MomentProviderOutcome> CaptureAsync(
            int hostId,
            Guid publicId,
            bool markerFallbackEnabled,
            string description,
            CancellationToken ct
        )
        {
            return Task.FromResult<MomentProviderOutcome>(
                new MomentProviderOutcome.Failed(null, null, "Not used by this UI test.")
            );
        }
    }

    private sealed class OfflineStreamLivenessProvider : IHostStreamLivenessProvider
    {
        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin)
        {
            return IO<HostStreamLivenessOutcome, Never>.Create(_ =>
                ValueTask.FromResult(
                    Result<HostStreamLivenessOutcome, Never>.Success(
                        new HostStreamLivenessOutcome.Offline()
                    )
                )
            );
        }
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            TwitchUserId = "streamer-id",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }
}
