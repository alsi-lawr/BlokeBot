using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardGuessingControlsTests
{
    private static readonly CancellationToken _ct = CancellationToken.None;

    private static DashboardWidgetConfiguration.Guessing Configuration(int profileId) =>
        new(new DashboardBinding.Configured(new DashboardTarget.Guessing(profileId)));

    private static T Ready<T>(DashboardResult<DashboardGuessingResult<T>> result) =>
        result
            .ShouldBeOfType<DashboardResult<DashboardGuessingResult<T>>.Succeeded>()
            .Value.ShouldBeOfType<DashboardGuessingResult<T>.Ready>()
            .Value;

    private static DashboardBindingAvailability Unavailable<T>(
        DashboardResult<DashboardGuessingResult<T>> result
    ) =>
        result
            .ShouldBeOfType<DashboardResult<DashboardGuessingResult<T>>.Succeeded>()
            .Value.ShouldBeOfType<DashboardGuessingResult<T>.Unavailable>()
            .Reason;

    private static AuthenticatedSession Session(
        DashboardHostId host,
        AuthRole role = AuthRole.Streamer
    )
    {
        var selected = new BotHostChoice(host.Value, "streamer", "Host", role);
        return new()
        {
            IsAuthenticated = true,
            UserId = "operator-id",
            Login = "operator",
            IsBotAccount = role == AuthRole.Bot,
            State = new AuthSessionState.Selected(new BotHostSelection(selected, [selected])),
        };
    }

    private sealed class Moderator : IModeratorAuthorityService
    {
        internal ModeratorAuthorityOutcome Outcome { get; set; } =
            new ModeratorAuthorityOutcome.Granted();

        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int hostId,
            CancellationToken ct
        ) => Task.FromResult(Outcome);
    }

    private sealed class Presenter : IOverlayEventPresenter
    {
        internal List<OverlayEventPresentation> Presentations { get; } = [];

        public Task PresentAsync(OverlayEventPresentation presentation, CancellationToken ct)
        {
            Presentations.Add(presentation);
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal PointProviderFixture Database { get; }
        internal Measurements Queries { get; }
        internal Moderator Moderator { get; } = new();
        internal EventBus<AppEventKind> Events { get; } = TestEventBus.Create<AppEventKind>();
        internal Presenter Presenter { get; } = new();
        internal GuessingChangeNotifier Notifier { get; }
        internal GuessingRoundService Rounds { get; }
        internal GuessingVoteService Votes { get; }
        internal DashboardHostId Host { get; private set; }
        internal DashboardHostId Other { get; private set; }
        internal int Profile { get; private set; }
        internal int NextProfile { get; private set; }
        internal int OtherProfile { get; private set; }
        internal AuthenticatedSession Owner => Session(Host);
        internal DashboardWidgetConfiguration.Guessing Bound => Configuration(Profile);
        internal DashboardGuessingControls Controls =>
            new(new(Database, Moderator), Database, new(Database), Rounds);

        private Fixture(PointProviderFixture database, Measurements measurements)
        {
            Database = database;
            Queries = measurements;
            Notifier = new(Events);
            Rounds = new(
                Database,
                Notifier,
                new PointBalanceService(Database),
                new PointsChangeNotifier(Events),
                [Presenter]
            );
            Votes = new(Database, Notifier);
        }

        internal static async Task<Fixture> CreateAsync(bool postgres = false)
        {
            var measurements = new Measurements();
            var f = new Fixture(
                postgres
                    ? await PointProviderFixture.PostgreSqlAsync(measurements)
                    : await PointProviderFixture.SqliteFileAsync(measurements),
                measurements
            );
            await using var db = f.Database.CreateDbContext();
            var host = HostEntity("streamer");
            var other = HostEntity("other");
            db.Hosts.AddRange(host, other);
            _ = await db.SaveChangesAsync();
            var profile = ProfileEntity(host.Id, "Current", true, "Blue, Azure", "Red");
            var next = ProfileEntity(host.Id, "Next", false, "Green", "Gold");
            var foreign = ProfileEntity(other.Id, "Other", true, "Purple", "Pink");
            db.Profiles.AddRange(profile, next, foreign);
            db.PointsSettings.AddRange(
                new PointsSettings { HostId = host.Id, PointLabel = "beans" },
                new PointsSettings { HostId = other.Id, PointLabel = "beans" }
            );
            _ = await db.SaveChangesAsync();
            f.Host = new(host.Id);
            f.Other = new(other.Id);
            f.Profile = profile.Id;
            f.NextProfile = next.Id;
            f.OtherProfile = foreign.Id;
            measurements.Reset();
            return f;
        }

        private static BotHost HostEntity(string login) =>
            new()
            {
                Login = login,
                DisplayName = login,
                TwitchUserId = $"id-{login}",
                EnabledFeatures = HostFeatureFlags.All,
                CreatedAtUtc = DateTime.UtcNow,
            };

        private static GuessRoundProfile ProfileEntity(
            int host,
            string name,
            bool isDefault,
            params string[] options
        ) =>
            new()
            {
                HostId = host,
                Name = name,
                Slug = name.ToLowerInvariant(),
                IsDefault = isDefault,
                WinningGuessPointReward = "25",
                ReplySettings = new()
                {
                    RoundStartedReply = "{round} started: {options}",
                    RoundAlreadyOpenReply = "Already open",
                    NoOpenRoundReply = "No current round",
                    GuessingStoppedReply = "Guesses closed",
                    GuessingClosedReply = "Guesses closed",
                    InvalidGuessReply = "Invalid answer: {name}",
                    WinnerReply = "{name} wins: {winners}.{reward_text}",
                    NoWinnersReply = "{name} wins without correct guesses",
                },
                Options = options.Select(x => new GuessOption { Name = x, ReplyText = x }).ToList(),
            };

        internal async Task FeaturesAsync(HostFeatureFlags features)
        {
            await using var db = Database.CreateDbContext();
            (await db.Hosts.SingleAsync(x => x.Id == Host.Value)).EnabledFeatures = features;
            _ = await db.SaveChangesAsync();
        }

        internal async Task WinningVotesAsync()
        {
            _ = (
                await Votes.RecordGuess("streamer", "one", "Azure").RunAsync(_ct)
            ).ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
            _ = (
                await Votes.RecordGuess("streamer", "two", "blue").RunAsync(_ct)
            ).ShouldBeOfType<GuessingOperationOutcome.Succeeded>();
        }

        internal async Task SeedVolumeAsync(int profiles, int history, int votes)
        {
            await using var db = Database.CreateDbContext();
            var now = DateTime.UtcNow;
            foreach (
                var (host, profile) in new[] { (Host.Value, Profile), (Other.Value, OtherProfile) }
            )
            {
                for (var index = 0; index < profiles; index++)
                {
                    _ = db.Profiles.Add(ProfileEntity(host, $"Unrelated{index}", false, "Orange"));
                }
                for (var index = 0; index < history; index++)
                {
                    _ = db.Rounds.Add(
                        new()
                        {
                            HostId = host,
                            GuessRoundProfileId = profile,
                            Status = GuessRoundStatus.Completed,
                            StartedAtUtc = now.AddHours(-2).AddSeconds(index),
                            ClosedAtUtc = now.AddHours(-1),
                            WinningName = host == Host.Value ? "blue" : "purple",
                        }
                    );
                }
            }
            var current = await db.Rounds.SingleAsync(x =>
                x.HostId == Host.Value && x.Status == GuessRoundStatus.Open
            );
            for (var index = 0; index < votes; index++)
            {
                _ = db.Votes.Add(
                    new()
                    {
                        GuessRoundId = current.Id,
                        Login = $"volume{index}",
                        GuessName = "blue",
                        GuessedAtUtc = now,
                    }
                );
            }
            _ = await db.SaveChangesAsync();
            Queries.Reset();
        }

        public async ValueTask DisposeAsync() => await Database.DisposeAsync();
    }
}
