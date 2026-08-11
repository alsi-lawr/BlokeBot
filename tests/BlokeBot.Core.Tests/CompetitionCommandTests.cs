using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Competitions;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CompetitionCommandTests
{
    [Test]
    public async Task DisabledDispatch_ProducesNoReplyOrRegistrationMutation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var seed = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "host-id",
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.Competitions,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = seed.Hosts.Add(host);
            _ = await seed.SaveChangesAsync();
            hostId = host.Id;
        }
        var service = new CompetitionService(
            database,
            TestEventBus.Create<AppEventKind>(),
            new NoopGrants(),
            [],
            TimeProvider.System
        );
        _ = (
            await service.CreateAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    "Viewer Cup",
                    "Public cup",
                    CompetitionFormat.RoundRobin,
                    CompetitionEntryKind.Individual,
                    CompetitionSeeding.Random,
                    CompetitionTiebreak.ScoreDifferenceThenScoreFor,
                    8,
                    1,
                    PointAmount.Zero,
                    3,
                    1,
                    0,
                    "viewer-cup",
                    24,
                    "Reminder: {competition} round {round} at {scheduled}. {public_url}",
                    PointAmount.Zero,
                    PointAmount.Zero,
                    string.Empty,
                    string.Empty,
                    [],
                    string.Empty,
                    new("host-id", "streamer"),
                    "create"
                ),
                default
            )
        ).ShouldBeOfType<CompetitionOutcome.Succeeded>();
        var competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        _ = (
            await service.OpenRegistrationAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    competition.Id,
                    competition.Revision,
                    new("host-id", "streamer"),
                    "open"
                ),
                default
            )
        ).ShouldBeOfType<CompetitionOutcome.Succeeded>();
        int eventCount;
        await using (var disable = await database.CreateDbContextAsync())
        {
            eventCount = await disable.CompetitionEvents.CountAsync();
            var host = await disable.Hosts.SingleAsync(x => x.Id == hostId);
            host.EnabledFeatures = HostFeatureFlags.None;
            _ = await disable.SaveChangesAsync();
        }
        var services = new ServiceCollection();
        _ = services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
        _ = services.AddSingleton(service);
        _ = services.AddChatCommands().AddCommandModule<CompetitionCommandModule>();
        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ChatCommandDispatcher>();
        var replies = new List<string>();

        await DispatchAsync(dispatcher, "!competitions", replies);
        await DispatchAsync(dispatcher, "!competitionjoin", replies);

        replies.ShouldBeEmpty();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.CompetitionEntrants.CountAsync()).ShouldBe(0);
        (await verify.CompetitionEvents.CountAsync()).ShouldBe(eventCount);
    }

    private static async Task DispatchAsync(
        ChatCommandDispatcher dispatcher,
        string text,
        List<string> replies
    ) =>
        await dispatcher.DispatchResponsesAsync(
            new(
                "viewer",
                "streamer",
                text,
                "raw",
                new Dictionary<string, string>
                {
                    ["id"] = Guid.NewGuid().ToString(),
                    ["user-id"] = "viewer-id",
                    ["display-name"] = "Viewer",
                }
            ),
            (response, _) =>
            {
                replies.Add(response.Message);
                return ValueTask.CompletedTask;
            },
            default
        );

    private sealed class NoopGrants : ICommunityAchievementGrantService
    {
        public Task<CommunityExternalGrantOutcome> GrantAsync(
            CommunityExternalGrantRequest request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<CommunityExternalGrantOutcome>(
                new CommunityExternalGrantOutcome.Granted(Guid.NewGuid(), false)
            );
    }
}
