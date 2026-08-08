using System.Security.Claims;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Moments;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class MomentUiTests
{
    [Test]
    public async Task ModeratorPage_KeepsWeeklyRecapInANewTabAndEveryCaptureSetting()
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
        _ = context.Services.AddSingleton(service);
        _ = context.Services.AddSingleton<IHostStreamLivenessProvider>(
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
            _ = recap.Closest(".page-header__actions").ShouldNotBeNull();
            var marker = page.Find("#moment-marker-fallback");
            marker.GetAttribute("role").ShouldBe("switch");
            page.Find($"#{marker.GetAttribute("aria-labelledby")}")
                .TextContent.ShouldContain("Use a stream marker");
            _ = page.Find("#moment-window").ShouldNotBeNull();
            _ = page.Find("#moment-reward-amount").ShouldNotBeNull();
            page.FindAll(".studio-segmented[aria-label='Point reward'] button")
                .Select(option => option.TextContent)
                .ShouldBe(["No reward", "First viewer to request", "All contributing viewers"]);
            page.Markup.ShouldNotContain("pt-6");
        });
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
                EnabledFeatures = HostFeatureFlags.All,
                Login = "streamer",
                DisplayName = "Streamer",
                TwitchUserId = "streamer-id",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
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
            _ = db.TwitchClips.Add(clip);
            _ = await db.SaveChangesAsync();
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
        _ = context.Services.AddSingleton(service);

        var page = context.Render<PublicMomentRecapPage>(parameters =>
            parameters.Add(component => component.Channel, "streamer")
        );

        page.WaitForAssertion(() => page.Find("h1").TextContent.ShouldBe("Weekly recap"));
        page.Markup.ShouldContain("Public title");
        _ = page.Find("a[href='https://clips.twitch.tv/PublicMoment']").ShouldNotBeNull();
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
        _ = publicRoute.ShouldNotBeNull();
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
                EnabledFeatures = HostFeatureFlags.All,
                Login = "streamer",
                DisplayName = "Streamer",
                TwitchUserId = "streamer-id",
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
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
            _ = db.TwitchClips.Add(clip);
            _ = await db.SaveChangesAsync();
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
            _ = authenticated.Services.AddSingleton(service);
            var authorization = authenticated.AddAuthorization();
            _ = authorization.SetAuthorized("OAuth Viewer");
            _ = authorization.SetClaims(
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
            _ = anonymous.Services.AddSingleton(service);
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
        ) => Task.FromResult<MomentProviderOutcome>(new MomentProviderOutcome.ClipReady(clipId));
    }

    private sealed class UnusedMomentProvider : IMomentProviderOperations
    {
        public Task<MomentProviderOutcome> CaptureAsync(
            int hostId,
            Guid publicId,
            bool markerFallbackEnabled,
            string description,
            CancellationToken ct
        ) =>
            Task.FromResult<MomentProviderOutcome>(
                new MomentProviderOutcome.Failed(null, null, "Not used by this UI test.")
            );
    }

    private sealed class OfflineStreamLivenessProvider : IHostStreamLivenessProvider
    {
        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin) =>
            IO<HostStreamLivenessOutcome, Never>.Create(static _ =>
                ValueTask.FromResult(
                    Result<HostStreamLivenessOutcome, Never>.Success(
                        new HostStreamLivenessOutcome.Offline()
                    )
                )
            );
    }

    private sealed class UnavailableStreamLivenessProvider : IHostStreamLivenessProvider
    {
        public IO<HostStreamLivenessOutcome, Never> GetStreamLiveness(string channelLogin) =>
            IO<HostStreamLivenessOutcome, Never>.Create(static _ =>
                ValueTask.FromResult(
                    Result<HostStreamLivenessOutcome, Never>.Success(
                        new HostStreamLivenessOutcome.Unavailable(
                            HostStreamLivenessUnavailableReason.ProviderRequestFailed,
                            new HttpRequestException("Unavailable")
                        )
                    )
                )
            );
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            TwitchUserId = "streamer-id",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
