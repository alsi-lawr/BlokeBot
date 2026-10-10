using System.Collections.Immutable;
using System.Data.Common;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardConfigurationTests
{
    private static readonly CancellationToken _ct = CancellationToken.None;

    private static T Value<T>(DashboardResult<T> outcome) =>
        outcome.ShouldBeOfType<DashboardResult<T>.Succeeded>().Value;

    private static DashboardSnapshot Saved(DashboardSaveOutcome outcome) =>
        outcome.ShouldBeOfType<DashboardSaveOutcome.Saved>().Dashboard;

    private static SaveDashboardCommand Save(
        Fixture fixture,
        DashboardSnapshot snapshot,
        DashboardDocument document,
        string name = "Edited"
    ) => new(fixture.Host, snapshot.Id, snapshot.Revision, name, document);

    private static DashboardWidget Widget(
        DashboardWidgetConfiguration configuration,
        int order = 0
    ) => new(new(Guid.NewGuid()), "Control", new(0, order, 4, 2, order), configuration);

    private static DashboardDocument Document(
        params DashboardWidgetConfiguration[] configurations
    ) => new(12, configurations.Select(Widget).ToImmutableArray());

    private static DashboardBinding Configured(DashboardTarget target) =>
        new DashboardBinding.Configured(target);

    private static DashboardBinding _unconfigured =>
        new DashboardBinding.Unconfigured(DashboardBindingIssue.NotSelected);

    private static AuthenticatedSession Session(
        DashboardHostId hostId,
        AuthRole role = AuthRole.Streamer
    )
    {
        var host = new BotHostChoice(hostId.Value, $"host-{hostId.Value}", "Host", role);
        return new()
        {
            IsAuthenticated = true,
            UserId = "operator-id",
            Login = "operator",
            IsBotAccount = role == AuthRole.Bot,
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
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

    private sealed class Fixture(PointProviderFixture database) : IAsyncDisposable
    {
        internal PointProviderFixture Database { get; } = database;
        internal Moderator Moderator { get; } = new();
        internal DashboardHostId Host { get; private set; }
        internal DashboardHostId Other { get; private set; }
        internal DashboardConfigurationService Service => new(Database, new(Database, Moderator));
        internal DashboardBindingReader Bindings => new(Database, new(Database, Moderator));
        internal AuthenticatedSession Owner => Session(Host);

        internal static async Task<Fixture> CreateAsync(PointProviderFixture database)
        {
            var fixture = new Fixture(database);
            await using var db = database.CreateDbContext();
            var host = new BotHost
            {
                Login = "dashboard-owner",
                DisplayName = "Owner",
                EnabledFeatures = HostFeatureFlags.All,
            };
            var other = new BotHost
            {
                Login = "other-dashboard-owner",
                DisplayName = "Other",
                EnabledFeatures = HostFeatureFlags.All,
            };
            _ = db.Hosts.Add(host);
            _ = db.Hosts.Add(other);
            _ = await db.SaveChangesAsync();
            fixture.Host = new(host.Id);
            fixture.Other = new(other.Id);
            return fixture;
        }

        internal async Task<DashboardSnapshot> CreateAsync(string name = "Stream controls") =>
            Value(await Service.CreateAsync(Owner, Host, name, _ct));

        internal async Task<DashboardSnapshot> ReadAsync(DashboardId id) =>
            Value(await Service.GetAsync(Owner, Host, id, _ct));

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class ReadWindow : DbCommandInterceptor
    {
        internal bool Pause { get; set; }
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Continue { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData data,
            InterceptionResult<DbDataReader> result,
            CancellationToken ct = default
        )
        {
            if (
                Pause
                && command.CommandText.Contains("channel_dashboards", StringComparison.Ordinal)
            )
            {
                Entered.SetResult();
                await Continue.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            }
            return result;
        }
    }

    private sealed class SaveInterleaving : DbCommandInterceptor
    {
        internal bool Coordinate { get; set; }
        internal bool FailAfterUpdate { get; set; }
        private readonly TaskCompletionSource _arrived = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _updates;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData data,
            InterceptionResult<DbDataReader> result,
            CancellationToken ct = default
        )
        {
            if (
                Coordinate
                && command.CommandText.Contains("hosts", StringComparison.Ordinal)
                && command.CommandText.StartsWith("SELECT EXISTS", StringComparison.Ordinal)
            )
            {
                var arrivals = Interlocked.Increment(ref _updates);
                if (arrivals == 2)
                {
                    _arrived.SetResult();
                }
                if (arrivals <= 2)
                {
                    await _arrived.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
                }
            }
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData data,
            int result,
            CancellationToken ct = default
        ) =>
            FailAfterUpdate
            && command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal)
            && command.CommandText.Contains("channel_dashboards", StringComparison.Ordinal)
                ? throw new IOException(
                    "Injected storage failure after dashboard CAS, before commit."
                )
                : ValueTask.FromResult(result);
    }
}
