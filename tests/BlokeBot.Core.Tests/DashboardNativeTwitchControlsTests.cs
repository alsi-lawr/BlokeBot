using System.Net;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardNativeTwitchControlsTests
{
    [Test]
    public async Task DashboardNative_PollLifecycle_RefreshesBoundCurrentStateWithoutPageHistory()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedHistoryAsync(150);
        PollControlState? latest = null;
        var refreshes = 0;
        using var subscription = f.Events.Subscribe(
            AppEventKind.TwitchOperationsChanged,
            ObserverIdentity.Named("dashboard-poll-test"),
            async (_, ct) =>
            {
                latest = Value(
                    await f.PollControls.ReadAsync(f.Owner, f.Host, f.PollConfiguration, ct)
                );
                refreshes++;
            }
        );

        var started = Value(
                await f.PollControls.StartAsync(f.Owner, f.Host, f.PollConfiguration, _ct)
            )
            .ShouldBeOfType<PollOperationOutcome.Started>();
        latest
            .ShouldNotBeNull()
            .ActivePoll.ShouldNotBeNull()
            .ProviderPollId.ShouldBe(started.Poll.ProviderPollId);
        latest.Template.ShouldNotBeNull().Id.ShouldBe(f.PollTemplateId);
        refreshes.ShouldBe(1);
        _ = Value(await f.PollControls.StartAsync(f.Owner, f.Host, f.PollConfiguration, _ct))
            .ShouldBeOfType<PollOperationOutcome.ActivePollExists>();
        f.Provider.Mutations.ShouldBe(1);
        f.Queries.Reset();
        _ = Value(await f.PollControls.ReadAsync(f.Owner, f.Host, f.PollConfiguration, _ct));
        f.Queries.Materialized<TwitchPollTemplate>().ShouldBe(1);
        f.Queries.Materialized<TwitchPollTemplateChoice>().ShouldBe(2);
        f.Queries.Materialized<TwitchPoll>().ShouldBe(1);
        f.Queries.Materialized<TwitchPrediction>().ShouldBe(0);
        f.Queries.Materialized<TwitchPredictionTemplate>().ShouldBe(0);

        _ = Value(await f.PollControls.EndAsync(f.Owner, f.Host, f.PollConfiguration, false, _ct))
            .ShouldBeOfType<PollOperationOutcome.Ended>();
        latest.ShouldNotBeNull().ActivePoll.ShouldBeNull();
        refreshes.ShouldBe(2);
        subscription.Dispose();
        _ = await f.PollControls.StartAsync(f.Owner, f.Host, f.PollConfiguration, _ct);
        refreshes.ShouldBe(2);
    }

    [Test]
    public async Task DashboardNative_ExternalPollEnd_ReauthorizesAfterConfirmationAndKeepsNativeCurrentTarget()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Polls.ReconcileAsync(f.Host.Value, _ct);
        var moderator = Session(f.Host, AuthRole.Moderator);
        _ = Value(await f.PollControls.EndAsync(moderator, f.Host, f.PollConfiguration, false, _ct))
            .ShouldBeOfType<PollOperationOutcome.ConfirmationRequired>();
        f.Provider.Mutations.ShouldBe(0);
        f.Moderator.Outcome = new ModeratorAuthorityOutcome.Revoked();

        (await f.PollControls.EndAsync(moderator, f.Host, f.PollConfiguration, true, _ct))
            .ShouldBeOfType<DashboardResult<PollOperationOutcome>.Rejected>()
            .Reason.ShouldBe(DashboardRejection.Revoked);
        f.Provider.Mutations.ShouldBe(0);
        Value(await f.PollControls.ReadAsync(f.Owner, f.Host, f.PollConfiguration, _ct))
            .ActivePoll.ShouldNotBeNull()
            .IsExternallyStarted.ShouldBeTrue();
        f.Moderator.Outcome = new ModeratorAuthorityOutcome.Granted();
        _ = Value(await f.PollControls.EndAsync(moderator, f.Host, f.PollConfiguration, true, _ct))
            .ShouldBeOfType<PollOperationOutcome.Ended>();
        f.Provider.Mutations.ShouldBe(1);
        Value(await f.PollControls.ReadAsync(f.Owner, f.Host, f.PollConfiguration, _ct))
            .ActivePoll.ShouldBeNull();
    }

    [Test]
    public async Task DashboardNative_PredictionLifecycle_RequiresConfirmationsAndExplicitCurrentWinner()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedHistoryAsync(150);
        PredictionControlState? latest = null;
        using var subscription = f.Events.Subscribe(
            AppEventKind.TwitchOperationsChanged,
            ObserverIdentity.Named("dashboard-prediction-test"),
            async (_, ct) =>
                latest = Value(
                    await f.PredictionControls.ReadAsync(
                        f.Owner,
                        f.Host,
                        f.PredictionConfiguration,
                        ct
                    )
                )
        );
        _ = Value(
                await f.PredictionControls.StartAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.Started>();
        latest.ShouldNotBeNull().ActivePrediction.ShouldNotBeNull().Status.ShouldBe("Active");
        _ = Value(
                await f.PredictionControls.StartAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.ActivePredictionExists>();
        _ = Value(
                await f.PredictionControls.LockAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    false,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.ConfirmationRequired>();
        _ = Value(
                await f.PredictionControls.CancelAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    false,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.ConfirmationRequired>();
        _ = Value(
                await f.PredictionControls.ResolveAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    "yes",
                    false,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.ConfirmationRequired>();
        f.Provider.Mutations.ShouldBe(1);
        _ = Value(
                await f.PredictionControls.LockAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    true,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.Updated>();
        latest.ShouldNotBeNull().ActivePrediction.ShouldNotBeNull().Status.ShouldBe("Locked");
        _ = Value(
                await f.PredictionControls.StartAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.ActivePredictionExists>();
        _ = Value(
                await f.PredictionControls.ResolveAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    "old-outcome",
                    true,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.InvalidOutcome>();
        f.Provider.Mutations.ShouldBe(2);
        var currentWinner = latest
            .ShouldNotBeNull()
            .ActivePrediction.ShouldNotBeNull()
            .Outcomes.Single(x => x.Title == "No")
            .Id;
        _ = Value(
                await f.PredictionControls.ResolveAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    currentWinner,
                    true,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.Updated>();
        latest.ShouldNotBeNull().ActivePrediction.ShouldBeNull();
        f.Provider.Requests.Last(x => x.Method == HttpMethod.Patch)
            .Body.ShouldContain("\"winning_outcome_id\":\"no\"");
        _ = Value(
                await f.PredictionControls.StartAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.Started>();
        _ = Value(
                await f.PredictionControls.CancelAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    true,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.Updated>();
        latest.ShouldNotBeNull().ActivePrediction.ShouldBeNull();
    }

    [Test]
    public async Task DashboardNative_ConfirmedPrediction_RetainsCurrentStateWhenActorOrHostChanges()
    {
        await using var f = await Fixture.CreateAsync();
        _ = await f.PredictionControls.StartAsync(f.Owner, f.Host, f.PredictionConfiguration, _ct);
        var moderator = Session(f.Host, AuthRole.Moderator);
        _ = Value(
                await f.PredictionControls.ResolveAsync(
                    moderator,
                    f.Host,
                    f.PredictionConfiguration,
                    "yes",
                    false,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.ConfirmationRequired>();
        f.Moderator.Outcome = new ModeratorAuthorityOutcome.Unavailable();

        (
            await f.PredictionControls.ResolveAsync(
                moderator,
                f.Host,
                f.PredictionConfiguration,
                "yes",
                true,
                _ct
            )
        )
            .ShouldBeOfType<DashboardResult<PredictionOperationOutcome>.Rejected>()
            .Reason.ShouldBe(DashboardRejection.AuthorityUnavailable);
        f.Moderator.Outcome = new ModeratorAuthorityOutcome.Revoked();
        _ = (
            await f.PredictionControls.LockAsync(
                moderator,
                f.Host,
                f.PredictionConfiguration,
                true,
                _ct
            )
        ).ShouldBeOfType<DashboardResult<PredictionOperationOutcome>.Rejected>();
        _ = (
            await f.PredictionControls.CancelAsync(
                moderator,
                f.Host,
                f.PredictionConfiguration,
                true,
                _ct
            )
        ).ShouldBeOfType<DashboardResult<PredictionOperationOutcome>.Rejected>();
        (
            await f.PredictionControls.ResolveAsync(
                Session(f.Other),
                f.Host,
                f.PredictionConfiguration,
                "yes",
                true,
                _ct
            )
        )
            .ShouldBeOfType<DashboardResult<PredictionOperationOutcome>.Rejected>()
            .Reason.ShouldBe(DashboardRejection.HostMismatch);
        _ = (
            await f.PollControls.StartAsync(Session(f.Other), f.Host, f.PollConfiguration, _ct)
        ).ShouldBeOfType<DashboardResult<PollOperationOutcome>.Rejected>();
        f.Provider.Mutations.ShouldBe(1);
        Value(await f.PredictionControls.ReadAsync(f.Owner, f.Host, f.PredictionConfiguration, _ct))
            .ActivePrediction.ShouldNotBeNull()
            .Status.ShouldBe("Active");
        f.Moderator.Outcome = new ModeratorAuthorityOutcome.Granted();
        _ = Value(
                await f.PredictionControls.ResolveAsync(
                    moderator,
                    f.Host,
                    f.PredictionConfiguration,
                    "yes",
                    true,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.Updated>();
    }

    [Test]
    [Arguments(HttpStatusCode.Unauthorized)]
    [Arguments(HttpStatusCode.Forbidden)]
    [Arguments(HttpStatusCode.BadRequest)]
    [Arguments(HttpStatusCode.ServiceUnavailable)]
    public async Task DashboardNative_PredictionProviderRejection_IsNotLocalCompletion(
        HttpStatusCode status
    )
    {
        await using var f = await Fixture.CreateAsync();
        _ = await f.PredictionControls.StartAsync(f.Owner, f.Host, f.PredictionConfiguration, _ct);
        f.Provider.MutationStatus = status;
        f.Provider.FailureMessage =
            status == HttpStatusCode.Forbidden
                ? "Broadcaster must be an affiliate or partner"
                : "Provider failed";
        var result = Value(
            await f.PredictionControls.ResolveAsync(
                f.Owner,
                f.Host,
                f.PredictionConfiguration,
                "yes",
                true,
                _ct
            )
        );
        switch (status)
        {
            case HttpStatusCode.Unauthorized:
                _ = result.ShouldBeOfType<PredictionOperationOutcome.NotReady>();
                break;
            case HttpStatusCode.Forbidden:
                _ = result.ShouldBeOfType<PredictionOperationOutcome.Ineligible>();
                break;
            case HttpStatusCode.BadRequest:
                _ = result.ShouldBeOfType<PredictionOperationOutcome.ProviderRejected>();
                break;
            case HttpStatusCode.ServiceUnavailable:
                _ = result.ShouldBeOfType<PredictionOperationOutcome.Unavailable>();
                break;
        }
        Value(await f.PredictionControls.ReadAsync(f.Owner, f.Host, f.PredictionConfiguration, _ct))
            .ActivePrediction.ShouldNotBeNull()
            .Status.ShouldBe("Active");
        await using var verify = f.Database.CreateDbContext();
        (await verify.TwitchPredictions.SingleAsync()).Status.ShouldBe(
            TwitchPredictionStatus.Active
        );
    }

    [Test]
    public async Task DashboardNative_MissingOrForeignTemplates_CannotMutateCurrentHostAndRemainRepairable()
    {
        await using var f = await Fixture.CreateAsync();
        _ = await f.PollControls.StartAsync(f.Owner, f.Host, f.PollConfiguration, _ct);
        _ = await f.PredictionControls.StartAsync(f.Owner, f.Host, f.PredictionConfiguration, _ct);
        var foreignPoll = (
            await f.Polls.SaveTemplateAsync(
                f.Other.Value,
                new("Foreign", ["Yes", "No"], 60, false, 0),
                _ct
            )
        )
            .ShouldBeOfType<PollOperationOutcome.TemplateSaved>()
            .Template;
        var foreignPrediction = (
            await f.Predictions.SaveTemplateAsync(
                f.Other.Value,
                new("Foreign", ["Yes", "No"], 60),
                _ct
            )
        )
            .ShouldBeOfType<PredictionOperationOutcome.TemplateSaved>()
            .Template;
        var poll = new DashboardWidgetConfiguration.Poll(
            new DashboardBinding.Configured(new DashboardTarget.Poll(foreignPoll.Id))
        );
        var prediction = new DashboardWidgetConfiguration.Prediction(
            new DashboardBinding.Configured(new DashboardTarget.Prediction(foreignPrediction.Id))
        );

        _ = Value(await f.PollControls.StartAsync(f.Owner, f.Host, poll, _ct))
            .ShouldBeOfType<PollOperationOutcome.TemplateNotFound>();
        _ = Value(await f.PollControls.EndAsync(f.Owner, f.Host, poll, true, _ct))
            .ShouldBeOfType<PollOperationOutcome.TemplateNotFound>();
        _ = Value(await f.PredictionControls.StartAsync(f.Owner, f.Host, prediction, _ct))
            .ShouldBeOfType<PredictionOperationOutcome.TemplateNotFound>();
        _ = Value(
                await f.PredictionControls.ResolveAsync(
                    f.Owner,
                    f.Host,
                    prediction,
                    "yes",
                    true,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.TemplateNotFound>();
        Value(await f.PollControls.ReadAsync(f.Owner, f.Host, poll, _ct)).Template.ShouldBeNull();
        Value(await f.PredictionControls.ReadAsync(f.Owner, f.Host, prediction, _ct))
            .Template.ShouldBeNull();
        _ = await f.Predictions.DeleteTemplateAsync(f.Host.Value, f.PredictionTemplateId, _ct);
        _ = Value(
                await f.PredictionControls.CancelAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    true,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.TemplateNotFound>();
        f.Provider.Mutations.ShouldBe(2);
        f.PredictionConfiguration.Binding.ShouldBeOfType<DashboardBinding.Configured>()
            .Target.ShouldBeOfType<DashboardTarget.Prediction>()
            .TemplateId.ShouldBe(f.PredictionTemplateId);
    }

    [Test]
    public async Task DashboardNative_FeatureAndTokenGates_RetainTemplatesAndMakeNoProviderMutation()
    {
        await using var f = await Fixture.CreateAsync();
        await f.FeaturesAsync(
            HostFeatureFlags.All & ~HostFeatureFlags.Polls & ~HostFeatureFlags.Predictions
        );
        _ = Value(await f.PollControls.ReadAsync(f.Owner, f.Host, f.PollConfiguration, _ct))
            .Authorization.ShouldBeOfType<PollAuthorizationReadiness.Disabled>();
        _ = Value(
            await f.PredictionControls.ReadAsync(f.Owner, f.Host, f.PredictionConfiguration, _ct)
        )
            .Authorization.ShouldBeOfType<PredictionAuthorizationReadiness.Disabled>();
        _ = Value(await f.PollControls.StartAsync(f.Owner, f.Host, f.PollConfiguration, _ct))
            .ShouldBeOfType<PollOperationOutcome.NotReady>();
        _ = Value(
                await f.PredictionControls.StartAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.NotReady>();
        f.Provider.Requests.ShouldBeEmpty();
        await f.FeaturesAsync(HostFeatureFlags.All);
        f.Broadcaster.Ready = false;
        _ = Value(await f.PollControls.ReadAsync(f.Owner, f.Host, f.PollConfiguration, _ct))
            .Authorization.ShouldBeOfType<PollAuthorizationReadiness.NeedsBroadcasterAuthorization>();
        _ = Value(
            await f.PredictionControls.ReadAsync(f.Owner, f.Host, f.PredictionConfiguration, _ct)
        )
            .Authorization.ShouldBeOfType<PredictionAuthorizationReadiness.NeedsBroadcasterAuthorization>();
        _ = Value(await f.PollControls.StartAsync(f.Owner, f.Host, f.PollConfiguration, _ct))
            .ShouldBeOfType<PollOperationOutcome.NotReady>();
        _ = Value(
                await f.PredictionControls.StartAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.NotReady>();
        f.Provider.Mutations.ShouldBe(0);
        f.Broadcaster.Ready = true;
        _ = Value(await f.PollControls.ReadAsync(f.Owner, f.Host, f.PollConfiguration, _ct))
            .Template.ShouldNotBeNull();
        _ = Value(
            await f.PredictionControls.ReadAsync(f.Owner, f.Host, f.PredictionConfiguration, _ct)
        )
            .Template.ShouldNotBeNull();
    }

    [Test]
    public async Task DashboardNative_PredictionReadinessAndQuery_UseEligibilityAndOnlyBoundCurrentRows()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedHistoryAsync(150);
        _ = await f.PredictionControls.StartAsync(f.Owner, f.Host, f.PredictionConfiguration, _ct);
        f.Queries.Reset();
        var requests = f.Provider.Requests.Count;
        var current = Value(
            await f.PredictionControls.ReadAsync(f.Owner, f.Host, f.PredictionConfiguration, _ct)
        );
        current.Template.ShouldNotBeNull().Id.ShouldBe(f.PredictionTemplateId);
        current.ActivePrediction.ShouldNotBeNull().Outcomes.Count.ShouldBe(2);
        f.Queries.Materialized<TwitchPredictionTemplate>().ShouldBe(1);
        f.Queries.Materialized<TwitchPredictionTemplateOutcome>().ShouldBe(2);
        f.Queries.Materialized<TwitchPrediction>().ShouldBe(0);
        f.Queries.Materialized<TwitchPoll>().ShouldBe(0);
        f.Queries.Materialized<TwitchPollTemplate>().ShouldBe(0);
        f.Provider.Requests.Count.ShouldBe(requests + 1);
        Console.WriteLine("COMPACT PREDICTION SQL:\n" + string.Join("\n---\n", f.Queries.Commands));
        f.Provider.BroadcasterType = string.Empty;
        _ = Value(
            await f.PredictionControls.ReadAsync(f.Owner, f.Host, f.PredictionConfiguration, _ct)
        )
            .Authorization.ShouldBeOfType<PredictionAuthorizationReadiness.Ineligible>();
        _ = Value(
                await f.PredictionControls.StartAsync(
                    f.Owner,
                    f.Host,
                    f.PredictionConfiguration,
                    _ct
                )
            )
            .ShouldBeOfType<PredictionOperationOutcome.Ineligible>();
        f.Provider.EligibilityStatus = HttpStatusCode.ServiceUnavailable;
        _ = Value(
            await f.PredictionControls.ReadAsync(f.Owner, f.Host, f.PredictionConfiguration, _ct)
        )
            .Authorization.ShouldBeOfType<PredictionAuthorizationReadiness.Unavailable>();
        f.Provider.EligibilityStatus = HttpStatusCode.Unauthorized;
        _ = Value(
            await f.PredictionControls.ReadAsync(f.Owner, f.Host, f.PredictionConfiguration, _ct)
        )
            .Authorization.ShouldBeOfType<PredictionAuthorizationReadiness.NeedsBroadcasterAuthorization>();
        f.Provider.Mutations.ShouldBe(1);
    }
}
