using BlokeBot.Core.Features.Moments;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

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
}
