using BlokeBot.Core.Features.MomentAttachments;
using BlokeBot.Persistence.Models;
using Bunit;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class MomentAttachmentUiTests
{
    [Test]
    public async Task EmbeddedSection_UsesOneContextualPickerAndNormalFlowDetach()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var fixture = await SeedAsync(database, momentsEnabled: true, attached: true);
        using var context = Context(database, fixture.HostId);

        var rendered = context.Render<MomentAttachmentsSection>(parameters =>
            parameters
                .Add(component => component.SelectedHostId, fixture.HostId)
                .Add(component => component.SelectedHostLogin, "streamer")
                .Add(
                    component => component.Destination,
                    new MomentAttachmentDestination.Bounty(fixture.BountyId)
                )
        );

        _ = rendered.Find("[data-moment-attachments]").ShouldNotBeNull();
        rendered.Find(".moment-attachments__heading button").Click();
        rendered
            .Find(".moment-attachments__choice--attached input")
            .HasAttribute("disabled")
            .ShouldBeTrue();
        rendered.Find("[data-attached-moment] button").Click();
        rendered.WaitForAssertion(() => rendered.FindAll("[data-attached-moment]").ShouldBeEmpty());
    }

    private static BunitContext Context(SqliteBlokeBotDbFactory database, int hostId) =>
        UiTestContextFactory.Create(database, hostId);

    private static async Task<Fixture> SeedAsync(
        SqliteBlokeBotDbFactory database,
        bool momentsEnabled,
        bool attached
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "streamer-id",
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures =
                HostFeatureFlags.Bounties
                | HostFeatureFlags.Points
                | (momentsEnabled ? HostFeatureFlags.Moments : HostFeatureFlags.None),
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var bounty = new Bounty
        {
            PublicId = Guid.NewGuid(),
            HostId = host.Id,
            CreationOperationId = Guid.NewGuid(),
            CreationFingerprint = Guid.NewGuid().ToString("N"),
            Title = "Community challenge",
            Status = BountyStatus.Funding,
            Visibility = BountyVisibility.Public,
            FailurePledgePolicy = BountyFailurePledgePolicy.Refund,
            RewardDistribution = BountyRewardDistribution.Equal,
            FundingTarget = "100",
            PledgedAmount = "0",
            CompletionReward = "0",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
            Revision = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        var moment = new MomentCandidate
        {
            PublicId = Guid.NewGuid(),
            HostId = host.Id,
            StreamIdentity = "stream-1",
            State = MomentCandidateState.Approved,
            PublicTitle = "Approved source",
            PublicCategory = "Highlights",
            CapturedAtUtc = DateTime.UtcNow.AddMinutes(-2),
            LastCapturedAtUtc = DateTime.UtcNow.AddMinutes(-2),
            ApprovedAtUtc = DateTime.UtcNow.AddMinutes(-1),
        };
        _ = db.Bounties.Add(bounty);
        _ = db.MomentCandidates.Add(moment);
        _ = await db.SaveChangesAsync();
        if (attached)
        {
            _ = db.MomentAttachments.Add(
                new MomentAttachment
                {
                    HostId = host.Id,
                    BountyId = bounty.Id,
                    MomentCandidateId = moment.Id,
                    AttachedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        return new(host.Id, bounty.PublicId);
    }

    private sealed record Fixture(int HostId, Guid BountyId);
}
