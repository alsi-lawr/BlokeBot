using System.Security.Claims;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Moments;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class MomentUiTests
{
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
        _ = context.AddAuthorization().SetNotAuthorized();

        context.AddPublicViewerBoundary(database);
        var page = context.Render<PublicMomentRecapPage>(parameters =>
            parameters.Add(component => component.Channel, "streamer")
        );

        page.WaitForAssertion(() =>
        {
            page.Markup.ShouldContain("Public title");
            _ = page.Find("a[href='https://clips.twitch.tv/PublicMoment']");
            page.Markup.ShouldNotContain("PRIVATE-MODERATOR-NOTE");
        });
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
            authenticated.AddPublicViewerBoundary(database);
            var page = authenticated.Render<PublicMomentRecapPage>(parameters =>
                parameters.Add(component => component.Channel, "streamer")
            );
            _ = page.WaitForElement("button.btn-secondary");
            await page.Find("button.btn-secondary").ClickAsync(new());
        }

        using (var reclaimed = new BunitContext())
        {
            _ = reclaimed.Services.AddSingleton(service);
            var authorization = reclaimed.AddAuthorization();
            _ = authorization.SetAuthorized("Reclaimed login");
            _ = authorization.SetClaims(
                new Claim(ClaimTypes.NameIdentifier, "other-known-id"),
                new Claim(AuthClaims.Login, "oauth_viewer")
            );
            reclaimed.AddPublicViewerBoundary(database);
            var page = reclaimed.Render<PublicMomentRecapPage>(parameters =>
                parameters.Add(component => component.Channel, "streamer")
            );
            _ = page.WaitForElement("button.btn-secondary");
            await page.Find("button.btn-secondary").ClickAsync(new());
            await using var unchanged = await database.CreateDbContextAsync();
            (await unchanged.MomentVotes.SingleAsync()).TwitchUserId.ShouldBe("oauth-viewer-id");
            _ = page.Find("[role='status'][data-error='true']").ShouldNotBeNull();
        }

        using (var anonymous = new BunitContext())
        {
            _ = anonymous.Services.AddSingleton(service);
            _ = anonymous.AddAuthorization();
            anonymous.AddPublicViewerBoundary(database);
            var page = anonymous.Render<PublicMomentRecapPage>(parameters =>
                parameters.Add(component => component.Channel, "streamer")
            );
            page.WaitForAssertion(() => page.Find("#moment-voter-login").ShouldNotBeNull());
            page.Find("#moment-voter-login").Change("oauth_viewer");
            await page.Find("button.btn-secondary").ClickAsync(new());
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
}
