using System.Globalization;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardGuessingControlsTests
{
    [Test]
    public async Task DashboardGuessing_StartStopDeclare_PreservesNativePayoutRepliesAndNotifications() =>
        await LifecycleAsync(await Fixture.CreateAsync());

    [Test]
    [Explicit]
    public async Task DashboardGuessing_PostgreSql_StartStopDeclare_PreservesNativePayoutAndQueries() =>
        await LifecycleAsync(await Fixture.CreateAsync(postgres: true));

    private static async Task LifecycleAsync(Fixture fixture)
    {
        await using var f = fixture;
        await using (var db = f.Database.CreateDbContext())
        {
            _ = db.ReplyPinPolicies.Add(
                new()
                {
                    HostId = f.Host.Value,
                    Feature = "guessing",
                    ReplyKey = GuessingReplyKeys.RoundStarted,
                    DurationSeconds = 180,
                    UnpinOnOwnerCompletion = true,
                }
            );
            _ = db.ReplyDeliverySettings.Add(
                new()
                {
                    HostId = f.Host.Value,
                    Feature = ReplyFeature.Guessing,
                    ScopeId = f.Profile,
                    ReplyKey = GuessingReplyKeys.InvalidGuess,
                    Target = ReplyDeliveryTarget.Whisper,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var changes = new List<GuessingControlState>();
        using var refresh = f.Events.Subscribe(
            AppEventKind.GuessingChanged,
            ObserverIdentity.Named("guessing-widget"),
            async (_, ct) =>
                changes.Add(Ready(await f.Controls.ReadAsync(f.Owner, f.Host, f.Bound, ct)))
        );
        Ready(await f.Controls.ReadAsync(f.Owner, f.Host, f.Bound, _ct))
            .CurrentRound.ShouldBeNull();
        var start = Ready(await f.Controls.StartAsync(f.Owner, f.Host, f.Bound, _ct))
            .ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
        var opened = changes.ShouldHaveSingleItem();
        _ = opened
            .CurrentRound.ShouldNotBeNull()
            .Lifecycle.ShouldBeOfType<GuessRoundLifecycle.Open>();
        start.Pin.ShouldBe(
            new PublicChatPinIntent(
                f.Host.Value,
                opened.CurrentRound.Id,
                "guessing",
                GuessingReplyKeys.RoundStarted,
                180,
                true
            )
        );
        start.Target.ShouldBe(CommandResponseTarget.Chat);
        start.Message.ShouldNotBeNullOrWhiteSpace();
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, f.Bound, _ct))
            .ShouldBeOfType<GuessingOperationOutcome.Rejected>();
        var invalid = Ready(
                await f.Controls.DeclareWinnerAsync(
                    f.Owner,
                    f.Host,
                    f.Bound,
                    GuessName.Parse("not-an-option"),
                    _ct
                )
            )
            .ShouldBeOfType<GuessingWinnerDeclarationOutcome.Completed>()
            .Result.ShouldBeOfType<GuessingOperationOutcome.Rejected>();
        invalid.Target.ShouldBe(CommandResponseTarget.Whisper);
        changes.Count.ShouldBe(1);
        await f.WinningVotesAsync();
        _ = Ready(await f.Controls.StopAsync(f.Owner, f.Host, f.Bound, _ct))
            .ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
        _ = changes
            .Last()
            .CurrentRound.ShouldNotBeNull()
            .Lifecycle.ShouldBeOfType<GuessRoundLifecycle.Closed>();
        _ = Ready(await f.Controls.StopAsync(f.Owner, f.Host, f.Bound, _ct))
            .ShouldBeOfType<GuessingOperationOutcome.Rejected>();
        var winner = Ready(
                await f.Controls.DeclareWinnerAsync(
                    f.Owner,
                    f.Host,
                    f.Bound,
                    GuessName.Parse("Azure"),
                    _ct
                )
            )
            .ShouldBeOfType<GuessingWinnerDeclarationOutcome.Completed>()
            .Result.ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
        winner.Message.ShouldNotBeNullOrWhiteSpace();
        changes.Last().CurrentRound.ShouldBeNull();
        await using var final = f.Database.CreateDbContext();
        var round = await final.Rounds.SingleAsync();
        round.Status.ShouldBe(GuessRoundStatus.Completed);
        round.WinningName.ShouldBe("blue");
        var balances = await final.PointBalances.OrderBy(x => x.Login).ToArrayAsync();
        balances.Select(x => (x.Login, x.Amount)).ShouldBe([("one", "25"), ("two", "25")]);
        (await final.PointLedgerEntries.CountAsync()).ShouldBe(2);
        _ = f.Presenter.Presentations.ShouldHaveSingleItem();
        var count = changes.Count;
        _ = Ready(
                await f.Controls.DeclareWinnerAsync(
                    f.Owner,
                    f.Host,
                    f.Bound,
                    GuessName.Parse("blue"),
                    _ct
                )
            )
            .ShouldBeOfType<GuessingWinnerDeclarationOutcome.Completed>()
            .Result.ShouldBeOfType<GuessingOperationOutcome.Rejected>();
        (await final.PointLedgerEntries.CountAsync()).ShouldBe(2);
        changes.Count.ShouldBe(count);
    }

    [Test]
    public async Task DashboardGuessing_NextProfileDiffers_CurrentOptionsAndDirectOpenDeclarationStayNative()
    {
        await using var f = await Fixture.CreateAsync();
        _ = (
            await f.Rounds.StartRound(f.Host.Value, f.Profile).RunAsync(_ct)
        ).ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
        var next = Configuration(f.NextProfile);
        var read = Ready(await f.Controls.ReadAsync(f.Owner, f.Host, next, _ct));
        read.SelectedProfile.Id.ShouldBe(f.NextProfile);
        read.CurrentRound.ShouldNotBeNull().ProfileId.ShouldBe(f.Profile);
        read.WinnerOptions.Select(x => x.Value).ShouldBe(["blue", "red"]);
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, next, _ct))
            .ShouldBeOfType<GuessingOperationOutcome.Rejected>();
        _ = Ready(
                await f.Controls.DeclareWinnerAsync(
                    f.Owner,
                    f.Host,
                    next,
                    GuessName.Parse("green"),
                    _ct
                )
            )
            .ShouldBeOfType<GuessingWinnerDeclarationOutcome.Completed>()
            .Result.ShouldBeOfType<GuessingOperationOutcome.Rejected>();
        _ = Ready(
                await f.Controls.DeclareWinnerAsync(
                    f.Owner,
                    f.Host,
                    next,
                    GuessName.Parse("red"),
                    _ct
                )
            )
            .ShouldBeOfType<GuessingWinnerDeclarationOutcome.Completed>()
            .Result.ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, next, _ct))
            .ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
        var current = Ready(await f.Controls.ReadAsync(f.Owner, f.Host, f.Bound, _ct));
        current.CurrentRound.ShouldNotBeNull().ProfileId.ShouldBe(f.NextProfile);
        current.WinnerOptions.Select(x => x.Value).ShouldBe(["gold", "green"]);
        _ = Ready(await f.Controls.StopAsync(f.Owner, f.Host, f.Bound, _ct))
            .ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
        _ = Ready(await f.Controls.ReadAsync(f.Owner, f.Host, f.Bound, _ct))
            .CurrentRound.ShouldNotBeNull()
            .Lifecycle.ShouldBeOfType<GuessRoundLifecycle.Closed>();
    }

    [Test]
    public async Task DashboardGuessing_AuthorityChangesAfterRead_NoActionOrPayoutEffects()
    {
        await using var f = await Fixture.CreateAsync();
        var moderator = Session(f.Host, AuthRole.Moderator);
        _ = Ready(await f.Controls.StartAsync(moderator, f.Host, f.Bound, _ct))
            .ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
        await f.WinningVotesAsync();
        var before = Ready(await f.Controls.ReadAsync(moderator, f.Host, f.Bound, _ct));
        var changed = 0;
        using var subscription = f.Events.Subscribe(
            AppEventKind.GuessingChanged,
            ObserverIdentity.Named("no-effects"),
            (_, _) =>
            {
                changed++;
                return ValueTask.CompletedTask;
            }
        );
        foreach (
            var denial in new ModeratorAuthorityOutcome[]
            {
                new ModeratorAuthorityOutcome.Revoked(),
                new ModeratorAuthorityOutcome.Unavailable(),
            }
        )
        {
            f.Moderator.Outcome = denial;
            await AssertDeniedAsync(f, moderator);
        }
        foreach (var actor in new[] { Session(f.Other), Session(f.Host, AuthRole.Bot) })
        {
            await AssertDeniedAsync(f, actor);
        }
        await using var db = f.Database.CreateDbContext();
        var current = await db.Rounds.SingleAsync();
        current.Id.ShouldBe(before.CurrentRound.ShouldNotBeNull().Id);
        current.Status.ShouldBe(GuessRoundStatus.Open);
        (await db.PointBalances.CountAsync()).ShouldBe(0);
        (await db.PointLedgerEntries.CountAsync()).ShouldBe(0);
        changed.ShouldBe(0);
        f.Presenter.Presentations.ShouldBeEmpty();
    }

    private static async Task AssertDeniedAsync(Fixture f, AuthenticatedSession actor)
    {
        _ = (
            await f.Controls.ReadAsync(actor, f.Host, f.Bound, _ct)
        ).ShouldBeOfType<DashboardResult<DashboardGuessingResult<GuessingControlState>>.Rejected>();
        _ = (
            await f.Controls.StartAsync(actor, f.Host, f.Bound, _ct)
        ).ShouldBeOfType<DashboardResult<
            DashboardGuessingResult<GuessingOperationOutcome>
        >.Rejected>();
        _ = (
            await f.Controls.StopAsync(actor, f.Host, f.Bound, _ct)
        ).ShouldBeOfType<DashboardResult<
            DashboardGuessingResult<GuessingOperationOutcome>
        >.Rejected>();
        _ = (
            await f.Controls.DeclareWinnerAsync(
                actor,
                f.Host,
                f.Bound,
                GuessName.Parse("blue"),
                _ct
            )
        ).ShouldBeOfType<DashboardResult<
            DashboardGuessingResult<GuessingWinnerDeclarationOutcome>
        >.Rejected>();
    }

    [Test]
    public async Task DashboardGuessing_FeatureDisabledAfterRead_AllOperationsRetainOpenRound()
    {
        await using var f = await Fixture.CreateAsync();
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, f.Bound, _ct));
        var before = Ready(await f.Controls.ReadAsync(f.Owner, f.Host, f.Bound, _ct));
        await f.FeaturesAsync(HostFeatureFlags.Points);
        Unavailable(await f.Controls.ReadAsync(f.Owner, f.Host, f.Bound, _ct))
            .ShouldBe(DashboardBindingAvailability.FeatureDisabled);
        Unavailable(await f.Controls.StartAsync(f.Owner, f.Host, f.Bound, _ct))
            .ShouldBe(DashboardBindingAvailability.FeatureDisabled);
        Unavailable(await f.Controls.StopAsync(f.Owner, f.Host, f.Bound, _ct))
            .ShouldBe(DashboardBindingAvailability.FeatureDisabled);
        Unavailable(
                await f.Controls.DeclareWinnerAsync(
                    f.Owner,
                    f.Host,
                    f.Bound,
                    GuessName.Parse("blue"),
                    _ct
                )
            )
            .ShouldBe(DashboardBindingAvailability.FeatureDisabled);
        await using var db = f.Database.CreateDbContext();
        var round = await db.Rounds.SingleAsync();
        round.Id.ShouldBe(before.CurrentRound.ShouldNotBeNull().Id);
        round.Status.ShouldBe(GuessRoundStatus.Open);
        (await db.PointLedgerEntries.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task DashboardGuessing_LaterWinnerAtCap_PayoutFailureRollsBackWholeDeclaration()
    {
        await using var f = await Fixture.CreateAsync();
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, f.Bound, _ct));
        await f.WinningVotesAsync();
        await using (var db = f.Database.CreateDbContext())
        {
            db.PointBalances.AddRange(
                new()
                {
                    HostId = f.Host.Value,
                    Login = "one",
                    Amount = "5",
                    UpdatedAtUtc = DateTime.UtcNow,
                },
                new()
                {
                    HostId = f.Host.Value,
                    Login = "two",
                    Amount = PointAmount.MaximumValue.ToString(CultureInfo.InvariantCulture),
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var changes = 0;
        using var subscription = f.Events.Subscribe(
            AppEventKind.GuessingChanged,
            ObserverIdentity.Named("payout"),
            (_, _) =>
            {
                changes++;
                return ValueTask.CompletedTask;
            }
        );
        var failed = Ready(
                await f.Controls.DeclareWinnerAsync(
                    f.Owner,
                    f.Host,
                    f.Bound,
                    GuessName.Parse("blue"),
                    _ct
                )
            )
            .ShouldBeOfType<GuessingWinnerDeclarationOutcome.PayoutFailed>();
        _ = failed.Failure.ShouldBeOfType<PointBalanceMutationFailure.CapExceeded>();
        await using var final = f.Database.CreateDbContext();
        var round = await final.Rounds.SingleAsync();
        round.Status.ShouldBe(GuessRoundStatus.Open);
        round.ClosedAtUtc.ShouldBeNull();
        round.WinningName.ShouldBeNull();
        var balances = await final.PointBalances.OrderBy(x => x.Login).ToArrayAsync();
        balances
            .Select(x => (x.Login, x.Amount))
            .ShouldBe([
                ("one", "5"),
                ("two", PointAmount.MaximumValue.ToString(CultureInfo.InvariantCulture)),
            ]);
        (await final.PointLedgerEntries.CountAsync()).ShouldBe(0);
        changes.ShouldBe(0);
        f.Presenter.Presentations.ShouldBeEmpty();
    }
}
