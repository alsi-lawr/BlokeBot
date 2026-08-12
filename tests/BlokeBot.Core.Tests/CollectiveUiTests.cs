using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.Collectives;
using BlokeBot.Core.Features.RaidCollaboration;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CollectiveUiTests
{
    [Test]
    public async Task SignedInDirectRoute_WhenDisabledShowsRecoveryWithoutRetainedData()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "host-id",
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.None,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
            _ = db.Collectives.Add(
                new Collective
                {
                    PublicId = Guid.NewGuid(),
                    CreationOperationId = Guid.NewGuid(),
                    Name = "RETAINED PRIVATE COLLECTIVE",
                    Revision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Memberships =
                    [
                        new()
                        {
                            HostId = hostId,
                            Role = CollectiveMembershipRole.Coordinator,
                            Status = CollectiveMembershipStatus.Active,
                            AcceptWorkAfterUtc = DateTime.UtcNow,
                            InvitedAtUtc = DateTime.UtcNow,
                            RespondedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow,
                        },
                    ],
                    Audits =
                    [
                        new()
                        {
                            OperationId = "private-audit-marker",
                            Action = CollectiveAuditAction.Created,
                            ActingHostId = hostId,
                            AffectedHostId = hostId,
                            ActorLogin = "PRIVATE ACTOR",
                            OccurredAtUtc = DateTime.UtcNow,
                        },
                    ],
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var service = new CollectiveService(
            database,
            new UnavailableRaidProvider(),
            TimeProvider.System
        );
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);

        var cut = context.Render<CollectivesPage>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-collectives-disabled-recovery]")
                .TextContent.ShouldContain("Channel setup");
            cut.Markup.ShouldContain("Nothing missed is repeated");
            cut.Markup.ShouldNotContain("RETAINED PRIVATE COLLECTIVE");
            cut.Markup.ShouldNotContain("PRIVATE ACTOR");
            cut.FindAll("input[type='checkbox']").ShouldBeEmpty();
        });
    }

    [Test]
    public void CollectivesRoute_HasUsefulOptInAuthorityPrivacyAndRecoveryHelp() =>
        PageHelpButton.HasUsefulHelpForPath("/collectives").ShouldBeTrue();

    private sealed class UnavailableRaidProvider : IRaidCollaborationProvider
    {
        public Task<RaidChannelSnapshotOutcome> LoadLiveChannelAsync(
            int hostId,
            string login,
            string? approvedClipId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<RaidChannelSnapshotOutcome>(
                new RaidChannelSnapshotOutcome.Unavailable()
            );

        public Task<ConfirmedRaidStartOutcome> StartConfirmedRaidAsync(
            int hostId,
            string targetTwitchUserId,
            string targetLogin,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<ConfirmedRaidStartOutcome>(
                new ConfirmedRaidStartOutcome.ProviderRejected()
            );

        public Task<bool> HasRaidManagementAuthorizationAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(false);
    }
}
