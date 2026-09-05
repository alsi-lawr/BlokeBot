using System.Security.Claims;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.ViewerPortal;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ViewerPortalPageTests
{
    [Test]
    public async Task SessionChanges_RetainPublicDataAndReplaceOnlyExactSelfInformation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var services = new ViewerPortalTestContext(database);
        var host = await services.HostAsync(
            "alpha",
            HostFeatureFlags.Points | HostFeatureFlags.ViewerPassports
        );
        await services.PointsAsync(host, "owner", "120");
        _ = await services.Passports.SaveAsync(
            new(
                host,
                new("owner-id", "owner", "Owner"),
                "Owner profile",
                ViewerPassportVisibility.Public,
                true,
                null,
                null
            ),
            default
        );
        using var context = Context(services);
        var auth = context.AddAuthorization();
        _ = auth.SetAuthorized("Owner");
        _ = auth.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "owner-id"),
            new Claim(AuthClaims.Login, "owner")
        );
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/channel/alpha");
        var page = context.Render<ViewerPortalPage>(parameters =>
            parameters.Add(value => value.Login, "alpha")
        );
        page.WaitForAssertion(() =>
            page.FindAll(".portal__you-value")
                .Any(value => value.TextContent.Contains("120"))
                .ShouldBeTrue()
        );
        var publicBefore = page.Find("nav").TextContent;
        _ = auth.SetAuthorized("Different");
        _ = auth.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, "other-id"),
            new Claim(AuthClaims.Login, "owner")
        );
        page.WaitForAssertion(() =>
            page.FindAll(".portal__you-value")
                .Any(value => value.TextContent.Contains("120"))
                .ShouldBeFalse()
        );
        page.Find("nav").TextContent.ShouldBe(publicBefore);
        _ = auth.SetNotAuthorized();
        page.WaitForAssertion(() => page.FindAll(".portal__you-value").ShouldBeEmpty());
        page.Find("nav").TextContent.ShouldBe(publicBefore);
    }

    [Test]
    public async Task FailedFeature_DoesNotHideOtherNavigationAndHostReplacementDropsOldData()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var services = new ViewerPortalTestContext(database, new UnavailableDatabase());
        var host = await services.HostAsync(
            "alpha",
            HostFeatureFlags.Bingo | HostFeatureFlags.Points
        );
        await services.PointsAsync(host, "public_viewer", "44");
        using var context = Context(services);
        _ = context.AddAuthorization();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/channel/alpha");
        var page = context.Render<ViewerPortalPage>(parameters =>
            parameters.Add(value => value.Login, "alpha")
        );
        page.WaitForAssertion(() =>
            page.FindAll("a")
                .Any(link => link.GetAttribute("href") == "/points/leaderboard/alpha")
                .ShouldBeTrue()
        );
        page.WaitForAssertion(() =>
            page.FindAll("[role=status]")
                .Any(value => value.TextContent.Contains("did not load"))
                .ShouldBeTrue()
        );
        page.Markup.ShouldNotContain("private backend diagnostic");
        await using (var db = await database.CreateDbContextAsync())
        {
            _ = await db.Hosts.Where(value => value.Id == host).ExecuteDeleteAsync();
        }
        _ = await services.HostAsync("alpha", HostFeatureFlags.Bingo);
        _ = await services.Events.PublishAsync(AppEventKind.HostedChannelsChanged, default);
        page.WaitForAssertion(() =>
            page.FindAll("a")
                .Any(link => link.GetAttribute("href") == "/points/leaderboard/alpha")
                .ShouldBeFalse()
        );
    }

    [Test]
    public async Task Navigation_CancelsPendingOwnerWithoutLosingOtherPublicLinksOrApplyingOldHost()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var blocked = new BlockingDatabase(database);
        var services = new ViewerPortalTestContext(database, blocked);
        _ = await services.HostAsync("alpha", HostFeatureFlags.Bingo | HostFeatureFlags.Points);
        _ = await services.HostAsync("beta", HostFeatureFlags.Points);
        using var context = Context(services);
        _ = context.AddAuthorization();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/channel/alpha");
        var page = context.Render<ViewerPortalPage>(parameters =>
            parameters.Add(value => value.Login, "alpha")
        );
        var pending = await blocked.Started.Task;
        page.FindAll("a")
            .Any(link => link.GetAttribute("href") == "/points/leaderboard/alpha")
            .ShouldBeTrue();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/channel/beta");
        page.Render(parameters => parameters.Add(value => value.Login, "beta"));
        page.WaitForAssertion(() =>
            page.FindAll("a")
                .Any(link => link.GetAttribute("href") == "/points/leaderboard/beta")
                .ShouldBeTrue()
        );
        pending.IsCancellationRequested.ShouldBeTrue();
        blocked.Release.SetResult();
        _ = await services.Events.PublishAsync(AppEventKind.BingoChanged, default);
        page.FindAll("a").Any(link => link.GetAttribute("href") == "/bingo/alpha").ShouldBeFalse();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/channel/@BETA");
        page.Render(parameters => parameters.Add(value => value.Login, "@BETA"));
        navigation.Uri.ShouldBe("http://localhost/channel/beta");
    }

    [Test]
    public async Task FrameworkDisconnect_CancelsInitialOwnerRetainsPublicContentAndResumesSameChannel()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var blocked = new BlockingDatabase(database);
        var services = new ViewerPortalTestContext(database, blocked);
        _ = await services.HostAsync("alpha", HostFeatureFlags.Bingo | HostFeatureFlags.Points);
        using var context = Context(services);
        _ = context.AddAuthorization();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/channel/alpha");
        var connection = context.Services.GetRequiredService<PortalCircuitConnection>();
        var page = context.Render<ViewerPortalPage>(parameters =>
            parameters.Add(value => value.Login, "alpha")
        );
        var pending = await blocked.Started.Task;
        await connection.OnConnectionDownAsync(null!, default);
        pending.IsCancellationRequested.ShouldBeTrue();
        page.FindAll("a")
            .Any(link => link.GetAttribute("href") == "/points/leaderboard/alpha")
            .ShouldBeTrue();
        blocked.Release.SetResult();
        await connection.OnConnectionUpAsync(null!, default);
        await blocked.Resumed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        page.WaitForAssertion(() =>
            page.FindAll("a")
                .Any(link => link.GetAttribute("href") == "/bingo/alpha")
                .ShouldBeTrue()
        );
    }

    private sealed class BlockingDatabase(SqliteBlokeBotDbFactory database)
        : IDbContextFactory<BlokeBotDbContext>
    {
        internal TaskCompletionSource<CancellationToken> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Resumed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlokeBotDbContext CreateDbContext() => database.CreateDbContext();

        public async Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            if (!Started.TrySetResult(cancellationToken))
            {
                _ = Resumed.TrySetResult();
            }
            await Release.Task.WaitAsync(cancellationToken);
            return await database.CreateDbContextAsync(cancellationToken);
        }
    }

    private static BunitContext Context(ViewerPortalTestContext services)
    {
        var context = new BunitContext();
        _ = context.Services.AddSingleton(services.Access);
        _ = context.Services.AddSingleton(services.Catalogue);
        _ = context.Services.AddSingleton(
            new PortalPersonalReader(
                services.Access,
                services.Passports,
                services.Queues,
                services.Requests,
                services.Bingo,
                services.Scheduler,
                services.Telemetry
            )
        );
        _ = context.Services.AddSingleton<IHostStreamLivenessProvider>(new OfflineStream());
        _ = context.Services.AddSingleton(services.Events);
        _ = context.Services.AddSingleton<TimeProvider>(services.Clock);
        _ = context.Services.AddSingleton<PortalCircuitConnection>();
        context.AddPublicViewerBoundary(services.Database);
        return context;
    }

    private sealed class UnavailableDatabase : IDbContextFactory<BlokeBotDbContext>
    {
        public BlokeBotDbContext CreateDbContext() =>
            throw new InvalidOperationException("private backend diagnostic");
    }

    private sealed class OfflineStream : IHostStreamLivenessProvider
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
}
