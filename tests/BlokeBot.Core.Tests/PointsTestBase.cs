using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Tests;

public abstract class PointsTestBase
{
    private protected static string DescribeParse(
        Result<PointAmount, PointAmountParseError> result
    ) => result.Match(static amount => $"Amount:{amount}", static error => $"Error:{error}");

    private protected static PointOperationOutcome.Succeeded Success(
        PointOperationOutcome outcome
    ) =>
        outcome.Match(
            static succeeded => succeeded,
            static _ =>
                throw new InvalidOperationException("Expected a successful point operation.")
        );

    private protected static PointOperationOutcome.Failed Failure(PointOperationOutcome outcome) =>
        outcome.Match(
            static _ => throw new InvalidOperationException("Expected a failed point operation."),
            static failed => failed
        );

    private protected static PointBalanceMutation Mutation(
        Result<PointBalanceMutation, PointBalanceMutationFailure> result
    ) =>
        result.Match(
            static mutation => mutation,
            static _ =>
                throw new InvalidOperationException("Expected a successful balance mutation.")
        );

    private protected static CommandStrategyContext<
        PointsCommandKind,
        AppCommandRouteState
    > CommandContext(
        int hostId,
        string login,
        string channel,
        string commandName,
        IReadOnlyList<string> args,
        List<string> replies,
        PointsCommandKind kind = PointsCommandKind.AddPoints
    )
    {
        var command = TestCommandContext.Create(
            login,
            channel,
            commandName,
            args,
            (CommandResponse response, CancellationToken _) =>
            {
                replies.Add(response.Message);
                return ValueTask.CompletedTask;
            }
        );

        return new CommandStrategyContext<PointsCommandKind, AppCommandRouteState>(
            kind,
            new AppCommandRouteState.Host(hostId),
            command,
            args
        );
    }

    private protected static GambleCommandStrategy CreateGambleStrategy(
        SqliteBlokeBotDbFactory dbFactory,
        TimeProvider clock,
        int minimumGamblingCooldownSeconds = 0
    ) =>
        new(
            new PointsCommandService(dbFactory),
            new PointBalanceService(dbFactory),
            new FixedPointsRandom(),
            new PointsGamblingCooldownStore(clock),
            Options.Create(
                new BlokeBotOptions
                {
                    Points = new BlokeBotPointsOptions
                    {
                        MinimumGamblingCooldownSeconds = minimumGamblingCooldownSeconds,
                    },
                }
            )
        );

    private protected static PointsConfigurationService CreateConfigurationService(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        var events = TestEventBus.Create<AppEventKind>();
        return new PointsConfigurationService(dbFactory, new PointsChangeNotifier(events));
    }

    private protected static PointsConfigurationSaveCommand ValidConfiguration(
        PointsConfiguration draft
    ) =>
        PointsConfigurationValidator
            .Validate(draft)
            .Match(
                command => command,
                errors =>
                    throw new InvalidOperationException(
                        string.Join(" ", errors.Select(error => error.Message))
                    )
            );

    private protected static async Task AddBalanceAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        string login,
        string amount
    )
    {
        var result = await new PointBalanceService(dbFactory)
            .Add(hostId, login, PointAmount.ParseAbsolute(amount), "streamer", "test")
            .ExecuteAsync(CancellationToken.None);
        _ = Mutation(result);
    }

    private protected static async Task SeedPointsSettingsAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        Action<PointsSettings> configure
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = new PointsSettings { HostId = hostId };
        configure(settings);
        _ = db.PointsSettings.Add(settings);
        _ = await db.SaveChangesAsync();
    }

    private protected static CommandStrategyContext<
        PointsCommandKind,
        AppCommandRouteState
    > TypedCommandContext(
        int hostId,
        string login,
        string channel,
        string commandName,
        IReadOnlyList<string> args,
        List<CommandResponse> responses,
        PointsCommandKind kind
    )
    {
        var command = TestCommandContext.Create(
            login,
            channel,
            commandName,
            args,
            (CommandResponse response, CancellationToken _) =>
            {
                responses.Add(response);
                return ValueTask.CompletedTask;
            }
        );

        return new CommandStrategyContext<PointsCommandKind, AppCommandRouteState>(
            kind,
            new AppCommandRouteState.Host(hostId),
            command,
            args
        );
    }

    private protected static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private protected sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private protected sealed class FixedPointTargetUserLookup(IEnumerable<string> existingUsers)
        : IPointTargetUserLookup
    {
        private readonly HashSet<string> _users = existingUsers
            .Select(Login.Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        public Task<bool> ExistsAsync(string login, CancellationToken ct) =>
            Task.FromResult(_users.Contains(Login.Normalize(login)));
    }

    private protected sealed class FixedPointsRandom : IPointsRandom
    {
        public double NextDouble() => 0;

        public int Next(int minValue, int maxValue) => minValue;
    }

    private protected sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _current = now;

        public override DateTimeOffset GetUtcNow() => _current;

        public void Advance(TimeSpan interval) => _current += interval;
    }
}
