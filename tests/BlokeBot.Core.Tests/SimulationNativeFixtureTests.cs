using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints.Page;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Simulation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class SimulationNativeFixtureTests
{
    [Test]
    public void NativeAliasesResolveToTheFiveExactRoutes() =>
        new Dictionary<string, string>
        {
            ["native-shoutouts"] = "/twitch-operations/shoutouts",
            ["native-polls"] = "/twitch-operations/polls",
            ["native-clips-markers"] = "/twitch-operations/clips-markers",
            ["native-channel-points"] = "/twitch-operations/channel-points",
            ["native-predictions"] = "/twitch-operations/predictions",
        }.ShouldAllBe(pair => SimulationViewCatalog.PathFor(pair.Key) == pair.Value);

    [Test]
    public void OverlayAliasResolvesToTheDeterministicDashboardRoute() =>
        SimulationViewCatalog.PathFor("overlays").ShouldBe("/overlays");

    [Test]
    public async Task OfflineSimulationSeedsAutomaticConfigurationOutcomesAndLocalDelivery()
    {
        await using var simulation = await SimulationApplication.BuildAsync(
            [],
            CancellationToken.None
        );
        var app = simulation.App;
        await app.InitializeSimulationAsync(CancellationToken.None);

        var factory = app.Services.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync(value => value.Login == SimulationMode.Login);
        var settings = await db.AutomaticRaidShoutoutSettings.SingleAsync(value =>
            value.HostId == host.Id
        );
        settings.Enabled.ShouldBeTrue();
        settings.Mechanism.ShouldBe(AutomaticRaidShoutoutMechanism.Chat);
        settings.ChatPresentation.ShouldBe(AutomaticRaidChatPresentation.Pinned);
        (
            await db.AutomaticRaidShoutoutOutcomes.CountAsync(value => value.HostId == host.Id)
        ).ShouldBe(4);
        (
            await db.AutomaticRaidShoutoutOutcomes.AnyAsync(value =>
                value.HostId == host.Id
                && value.ResultCode == AutomaticRaidShoutoutResultCode.PartialFailure
            )
        ).ShouldBeTrue();

        var delivery = app.Services.GetRequiredService<IAutomaticRaidShoutoutDelivery>();
        delivery.GetType().Assembly.ShouldBe(typeof(SimulationApplication).Assembly);
    }

    [Test]
    public async Task OfflineSimulationCompletesEveryNativeActionAgainstHostIsolatedLocalState()
    {
        await using var simulation = await SimulationApplication.BuildAsync(
            [],
            CancellationToken.None
        );
        var app = simulation.App;
        await app.InitializeSimulationAsync(CancellationToken.None);

        var factory = app.Services.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var hostId = await db
            .Hosts.Where(value => value.Login == SimulationMode.Login)
            .Select(value => value.Id)
            .SingleAsync();

        var shoutouts = app.Services.GetRequiredService<IShoutoutDashboardOperations>();
        var polls = app.Services.GetRequiredService<IPollDashboardOperations>();
        var clipsMarkers = app.Services.GetRequiredService<IClipMarkerDashboardOperations>();
        var channelPoints = app.Services.GetRequiredService<IChannelPointsDashboardOperations>();
        var predictions = app.Services.GetRequiredService<IPredictionDashboardOperations>();
        new object[] { shoutouts, polls, clipsMarkers, channelPoints, predictions }.ShouldAllBe(
            value => value.GetType().Assembly == typeof(SimulationApplication).Assembly
        );

        (
            await shoutouts.SendAsync(hostId, "friendlychannel", CancellationToken.None)
        ).ShouldBeOfType<ShoutoutOperationOutcome.Sent>();
        (await shoutouts.LoadAsync(hostId, "friendlychannel", CancellationToken.None))
            .History.Single()
            .TargetLogin.ShouldBe("friendlychannel");

        var savedPoll = (
            await polls.SaveTemplateAsync(
                hostId,
                new PollTemplateDraft("Choose a snack", ["Popcorn", "Fruit"], 60, false, null),
                CancellationToken.None
            )
        ).ShouldBeOfType<PollOperationOutcome.TemplateSaved>();
        (
            await polls.StartAsync(hostId, savedPoll.Template.Id, CancellationToken.None)
        ).ShouldBeOfType<PollOperationOutcome.Started>();
        (
            await polls.EndAsync(hostId, true, CancellationToken.None)
        ).ShouldBeOfType<PollOperationOutcome.Ended>();
        var pollState = await polls.LoadAsync(hostId, CancellationToken.None);
        pollState.ActivePoll.ShouldBeNull();
        pollState.Results.Single().Title.ShouldBe("Choose a snack");

        var clip = (
            await clipsMarkers.CreateClipAsync(hostId, false, CancellationToken.None)
        ).ShouldBeOfType<ClipMarkerOperationOutcome.ClipPending>();
        (
            await clipsMarkers.RetryClipAsync(hostId, clip.Clip.Attempt, CancellationToken.None)
        ).ShouldBeOfType<ClipMarkerOperationOutcome.ClipAvailable>();
        (
            await clipsMarkers.CreateMarkerAsync(hostId, "Boss defeated", CancellationToken.None)
        ).ShouldBeOfType<ClipMarkerOperationOutcome.MarkerCreated>();
        var clipState = await clipsMarkers.LoadAsync(hostId, CancellationToken.None);
        clipState.PendingClips.ShouldBeEmpty();
        clipState.Results.Single().FinalUrl.ShouldNotBeNullOrWhiteSpace();
        clipState.Markers.Single().Description.ShouldBe("Boss defeated");

        var rewardDraft = new ChannelPointsRewardDraft(
            "Hydration reminder",
            "Take a sip of water",
            250,
            false,
            false,
            null,
            false,
            null,
            false,
            null,
            false,
            "#9147FF"
        );
        var initialPointsState = await channelPoints.LoadAsync(hostId, CancellationToken.None);
        var timeProvider = app.Services.GetRequiredService<TimeProvider>();
        initialPointsState
            .ActiveRedemptions.Select(redemption =>
                RedemptionWaitingAgePresentation.Create(redemption.RedeemedAtUtc, timeProvider).Band
            )
            .ShouldBe([
                RedemptionWaitingAgeBand.NeedsAttention,
                RedemptionWaitingAgeBand.Waiting,
                RedemptionWaitingAgeBand.Fresh,
            ]);
        var createdReward = (
            await channelPoints.CreateRewardAsync(hostId, rewardDraft, CancellationToken.None)
        ).ShouldBeOfType<ChannelPointsOperationOutcome.RewardCreated>();
        (
            await channelPoints.UpdateRewardAsync(
                hostId,
                createdReward.Reward.ProviderRewardId,
                rewardDraft with
                {
                    Cost = 300,
                },
                false,
                true,
                CancellationToken.None
            )
        ).ShouldBeOfType<ChannelPointsOperationOutcome.RewardUpdated>();
        (
            await channelPoints.UpdateRewardAsync(
                hostId,
                createdReward.Reward.ProviderRewardId,
                rewardDraft,
                true,
                false,
                CancellationToken.None
            )
        ).ShouldBeOfType<ChannelPointsOperationOutcome.RewardUpdated>();
        (
            await channelPoints.UpdateRedemptionAsync(
                hostId,
                "simulation-redemption-1",
                true,
                CancellationToken.None
            )
        ).ShouldBeOfType<ChannelPointsOperationOutcome.RedemptionUpdated>();
        (
            await channelPoints.UpdateRedemptionAsync(
                hostId,
                "simulation-redemption-2",
                false,
                CancellationToken.None
            )
        ).ShouldBeOfType<ChannelPointsOperationOutcome.RedemptionUpdated>();
        (
            await channelPoints.UpdateRedemptionAsync(
                hostId,
                "simulation-redemption-3",
                true,
                CancellationToken.None
            )
        ).ShouldBeOfType<ChannelPointsOperationOutcome.RedemptionUpdated>();
        (
            await channelPoints.DeleteRewardAsync(
                hostId,
                createdReward.Reward.ProviderRewardId,
                true,
                CancellationToken.None
            )
        ).ShouldBeOfType<ChannelPointsOperationOutcome.RewardDeleted>();
        var pointsState = await channelPoints.LoadAsync(hostId, CancellationToken.None);
        pointsState.ActiveRedemptions.ShouldBeEmpty();
        pointsState
            .History.Select(value => value.Status)
            .ShouldBe(["Fulfilled", "Canceled", "Fulfilled"]);

        var savedPrediction = (
            await predictions.SaveTemplateAsync(
                hostId,
                new PredictionTemplateDraft("Will chat solve it?", ["Yes", "Not yet"], 60),
                CancellationToken.None
            )
        ).ShouldBeOfType<PredictionOperationOutcome.TemplateSaved>();
        (
            await predictions.StartAsync(
                hostId,
                savedPrediction.Template.Id,
                CancellationToken.None
            )
        ).ShouldBeOfType<PredictionOperationOutcome.Started>();
        var locked = (
            await predictions.LockAsync(hostId, true, CancellationToken.None)
        ).ShouldBeOfType<PredictionOperationOutcome.Updated>();
        (
            await predictions.ResolveAsync(
                hostId,
                locked.Prediction.Outcomes[0].Id,
                true,
                CancellationToken.None
            )
        ).ShouldBeOfType<PredictionOperationOutcome.Updated>();
        (
            await predictions.StartAsync(hostId, 1, CancellationToken.None)
        ).ShouldBeOfType<PredictionOperationOutcome.Started>();
        (
            await predictions.CancelAsync(hostId, true, CancellationToken.None)
        ).ShouldBeOfType<PredictionOperationOutcome.Updated>();
        (await predictions.LoadAsync(hostId, CancellationToken.None)).Results.Count.ShouldBe(2);

        const int OtherHostId = 999;
        (
            await shoutouts.LoadAsync(OtherHostId, null, CancellationToken.None)
        ).History.ShouldBeEmpty();
        (await channelPoints.LoadAsync(OtherHostId, CancellationToken.None)).Rewards.Count.ShouldBe(
            1
        );
        (await polls.LoadAsync(OtherHostId, CancellationToken.None)).ActivePoll.ShouldBeNull();
        (
            await predictions.LoadAsync(OtherHostId, CancellationToken.None)
        ).ActivePrediction.ShouldBeNull();
    }
}
