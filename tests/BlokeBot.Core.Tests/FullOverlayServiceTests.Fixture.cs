using System.Text.Json;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    private static readonly CancellationToken _ct = CancellationToken.None;

    private static T Value<T>(FullOverlayResult<T> result) =>
        result.ShouldBeOfType<FullOverlayResult<T>.Succeeded>().Value;

    private static void Rejected<T>(FullOverlayResult<T> result, FullOverlayRejectionKind kind) =>
        result.ShouldBeOfType<FullOverlayResult<T>.Rejected>().Reason.Kind.ShouldBe(kind);

    private static FullOverlayDocument Document(
        string html = "<future-box strange='kept'><span>",
        string css =
            "@layer custom { future-box { --unknown: 7; color: color(display-p3 1 0 0); } }"
    ) =>
        new(
            Guid.NewGuid(),
            html,
            css,
            [
                new(
                    new(Guid.NewGuid()),
                    new("cue-player"),
                    JsonSerializer.SerializeToElement(
                        new { unexpectedExtension = new[] { "preserved", "also" } }
                    ),
                    FullOverlayAuthoringMetadata.Default with
                    {
                        IsLocked = true,
                        X = "calc(100vw - 20px)",
                        ScaleX = 1.3,
                    },
                    new(false, 0.75)
                ),
            ],
            [new("incomplete-html", "Unclosed element", FullOverlayDiagnosticSeverity.Warning)]
        );

    private static SaveFullOverlayCommand Save(FullOverlayView overlay, string html) =>
        new(overlay.Id, overlay.Revision, overlay.Name, overlay.Draft with { Html = html });

    private static AuthenticatedSession Session(int hostId, AuthRole role = AuthRole.Streamer)
    {
        var host = new BotHostChoice(hostId, $"host-{hostId}", "Host", role);
        return new()
        {
            IsAuthenticated = true,
            UserId = "author-id",
            Login = "author",
            IsBotAccount = role == AuthRole.Bot,
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal SqliteBlokeBotDbFactory Database { get; }
        internal FullOverlayService Service { get; }
        internal FullOverlayPublishedReader Reader { get; }
        internal Admission Admission { get; } = new();
        internal Moderator Moderator { get; } = new();
        internal SaveFailure Failure { get; } = new();
        internal int HostId { get; private set; }
        internal int OtherHostId { get; private set; }
        internal AuthenticatedSession Owner => Session(HostId);
        internal EventBus<AppEventKind> Events { get; } = TestEventBus.Create<AppEventKind>();

        private Fixture(SqliteBlokeBotDbFactory database, SaveFailure failure)
        {
            Database = database;
            Failure = failure;
            Service = NewService(Admission);
            Reader = new(Database);
        }

        internal FullOverlayService NewService(IFullOverlayPublicationAdmission admission) =>
            new(
                Database,
                new(Database, Moderator),
                new CryptographicOverlayAccessKeyGenerator(),
                admission,
                Events,
                TimeProvider.System
            );

        internal static async Task<Fixture> CreateAsync()
        {
            var failure = new SaveFailure();
            var database = await SqliteBlokeBotDbFactory.CreateAsync(failure);
            var fixture = new Fixture(database, failure);
            await using var db = database.CreateDbContext();
            var host = new BotHost
            {
                Login = "host",
                DisplayName = "Host",
                EnabledFeatures = HostFeatureFlags.All,
                CreatedAtUtc = DateTime.UtcNow,
            };
            var other = new BotHost
            {
                Login = "other",
                DisplayName = "Other",
                EnabledFeatures = HostFeatureFlags.All,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Hosts.AddRange(host, other);
            _ = await db.SaveChangesAsync();
            fixture.HostId = host.Id;
            fixture.OtherHostId = other.Id;
            return fixture;
        }

        internal async Task<FullOverlayCreation> CreateOverlayAsync() =>
            Value(await Service.CreateAsync(Owner, new("Composition", Document()), _ct));

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class Admission : IFullOverlayPublicationAdmission
    {
        internal Func<
            FullOverlayPublicationCandidate,
            CancellationToken,
            Task<FullOverlayAdmission>
        > Admit { get; set; } =
            (_, _) => Task.FromResult<FullOverlayAdmission>(new FullOverlayAdmission.Admitted([]));

        public Task<FullOverlayAdmission> AdmitAsync(
            FullOverlayPublicationCandidate candidate,
            CancellationToken ct
        ) => Admit(candidate, ct);
    }

    private sealed class Moderator : IModeratorAuthorityService
    {
        internal ModeratorAuthorityOutcome Outcome { get; set; } =
            new ModeratorAuthorityOutcome.Granted();

        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        ) => Task.FromResult(Outcome);
    }

    private sealed class SaveFailure : SaveChangesInterceptor
    {
        internal bool FailPublication { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) =>
            (FailPublication && data.Context!.ChangeTracker.Entries<FullOverlayPublication>().Any())
                ? throw new IOException("Injected persistence failure after selection CAS.")
                : ValueTask.FromResult(result);
    }
}
