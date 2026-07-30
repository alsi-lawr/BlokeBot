using System.Text;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Identity;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class PollAuthorizationTests
{
    [Test]
    public async Task BroadcasterGrant_SelectedHostIdentity_RejectsMismatchAndProtectsFullScopeGrant()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Hosts.Add(
                new BotHost
                {
                    EnabledFeatures = HostFeatureFlags.All,
                    Login = "host",
                    DisplayName = "Host",
                    TwitchUserId = "host-id",
                }
            );
            await db.SaveChangesAsync();
        }
        var service = Service(factory);
        var wrong = await service.AuthorizeAsync(1, Grant("other-id"), CancellationToken.None);
        wrong.ShouldBeOfType<HostBroadcasterAuthorizationOutcome.GrantMismatch>();
        var correct = await service.AuthorizeAsync(1, Grant("host-id"), CancellationToken.None);
        correct.ShouldBeOfType<HostBroadcasterAuthorizationOutcome.Authorized>();
        await using var verify = await factory.CreateDbContextAsync();
        var stored = await verify.HostBroadcasterAuthorizations.SingleAsync();
        var protectedPayload = stored.ProtectedTokenPayload.ShouldNotBeNull();
        protectedPayload.AsSpan().IndexOf(Encoding.UTF8.GetBytes("access")).ShouldBe(-1);
        stored
            .AuthorizedScopes!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ShouldBe(
                HostBroadcasterAuthorizationService
                    .MilestoneScopes.Order(StringComparer.Ordinal)
                    .ToArray()
            );
    }

    [Test]
    public async Task BroadcasterReadiness_WithoutGrant_ReturnsTypedUnavailableWithoutBotFallback()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Hosts.Add(
                new BotHost
                {
                    EnabledFeatures = HostFeatureFlags.All,
                    Login = "host",
                    DisplayName = "Host",
                    TwitchUserId = "host-id",
                }
            );
            await db.SaveChangesAsync();
        }
        var status = await Service(factory)
            .GetTokenStatusAsync(
                1,
                HostBroadcasterAuthorizationService.MilestoneScopes,
                CancellationToken.None
            );
        status.ShouldBeOfType<TokenStatus.Unavailable>();
    }

    private static HostBroadcasterAuthorizationService Service(SqliteBlokeBotDbFactory factory)
    {
        return new(
            factory,
            HostBotAccountTokenProtectionTestSupport.CreateProtector(),
            null!,
            null!,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>())
        );
    }

    private static HostBotAccountAuthorizationGrant Grant(string id)
    {
        return new(
            new HostBotAccountTokenPayload("access", "refresh", DateTimeOffset.UtcNow.AddHours(1)),
            id,
            LoginName.Parse("host"),
            "Host",
            null,
            OAuthScopeSet.Create(HostBroadcasterAuthorizationService.MilestoneScopes)
        );
    }
}
