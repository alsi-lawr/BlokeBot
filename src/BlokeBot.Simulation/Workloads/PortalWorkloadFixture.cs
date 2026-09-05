using System.Globalization;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Simulation.Workloads;

internal static class PortalWorkloadFixture
{
    internal const int Viewers = 1000;
    internal const int Backlog = 120;

    internal static async Task SeedAsync(IServiceProvider services, int viewers, int destinations)
    {
        await using var db = await services
            .GetRequiredService<IDbContextFactory<BlokeBotDbContext>>()
            .CreateDbContextAsync();
        if (db.Database.IsSqlite())
        {
            _ = await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            _ = await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;");
        }
        var host = await db.Hosts.SingleAsync(value => value.Login == "samplechannel");
        var queue = await db.PlayQueues.FirstAsync(value => value.HostId == host.Id);
        var board = await db.RequestBoards.FirstAsync(value => value.HostId == host.Id);
        var now = SimulationMode.Now.UtcDateTime;
        for (var index = 0; index < viewers; index++)
        {
            var login = $"workloadviewer{index:D4}";
            var id = (900000 + index).ToString(CultureInfo.InvariantCulture);
            _ = db.PointBalances.Add(
                new PointBalance
                {
                    HostId = host.Id,
                    Login = login,
                    Amount = (index * 17).ToString(CultureInfo.InvariantCulture),
                    UpdatedAtUtc = now,
                }
            );
            _ = db.ViewerPassports.Add(
                new ViewerPassport
                {
                    HostId = host.Id,
                    TwitchUserId = id,
                    Login = login,
                    DisplayName = login,
                    Visibility = ViewerPassportVisibility.Public,
                    ProfileLine = "Synthetic workload passport",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }
            );
            _ = db.PlayQueueEntries.Add(
                new PlayQueueEntry
                {
                    HostId = host.Id,
                    QueueId = queue.Id,
                    IdentityKey = $"id:{id}",
                    TwitchUserId = id,
                    NormalizedLogin = login,
                    DisplayName = login,
                    Status = PlayQueueEntryStatus.Waiting,
                    JoinedAtUtc = now.AddSeconds(index),
                    UpdatedAtUtc = now,
                }
            );
            _ = db.RequestSubmissions.Add(
                new RequestSubmission
                {
                    HostId = host.Id,
                    BoardId = board.Id,
                    OperationId = Guid.NewGuid(),
                    SubmitterTwitchUserId = id,
                    SubmitterLogin = login,
                    Title = $"Synthetic request {index:D4}",
                    NormalizedTitle = $"synthetic request {index:D4}",
                    Status = RequestSubmissionStatus.Queued,
                    QueuePosition = index + 100,
                    CreatedAtUtc = now.AddSeconds(index),
                    UpdatedAtUtc = now,
                }
            );
        }
        for (var index = 1; index < destinations; index++)
        {
            _ = db.PlayQueues.Add(
                new PlayQueue
                {
                    HostId = host.Id,
                    Slug = $"workload-{index}",
                    Name = $"Workload queue {index}",
                    ActivityName = "Synthetic destination",
                    IsOpen = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }
            );
            _ = db.RequestBoards.Add(
                new RequestBoard
                {
                    HostId = host.Id,
                    Slug = $"workload-{index}",
                    Title = $"Workload board {index}",
                    IsOpen = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                }
            );
        }
        for (var index = 0; index < Backlog; index++)
        {
            _ = db.PublicChatOutboxMessages.Add(
                new PublicChatOutboxMessage
                {
                    Channel = host.Login,
                    Message = "Synthetic workload backlog",
                    DeduplicationKey = index.ToString("x64", CultureInfo.InvariantCulture),
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddHours(1),
                    NextAttemptAtUtc = now,
                }
            );
        }
        _ = await db.SaveChangesAsync();
    }
}
