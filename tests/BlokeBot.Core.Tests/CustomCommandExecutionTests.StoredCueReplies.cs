using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class CustomCommandExecutionTests
{
    [Test]
    [Arguments(OverlayCueReplyOrder.After, true)]
    [Arguments(OverlayCueReplyOrder.After, false)]
    [Arguments(OverlayCueReplyOrder.Before, false)]
    public async Task StoredCueReply_ReplayPreservesOriginalEligibilityWithoutReadmission(
        OverlayCueReplyOrder order,
        bool accepted
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(
            database,
            "streamer",
            HostFeatureFlags.CustomCommands | HostFeatureFlags.Overlays
        );
        var values = new CustomStoredValueService(database);
        var total = await DeclareAsync(
            values,
            hostId,
            "total",
            CustomValueScope.Global,
            CustomValueKind.Number,
            "0"
        );
        _ = await SeedCueCommandAsync(database, hostId, "cue", order);
        await using (var seed = database.CreateDbContext())
        {
            (await seed.CustomMessageVariants.SingleAsync()).Text =
                "{var_inc|global|total|1}:{random_between|1|100}";
            _ = await seed.SaveChangesAsync();
        }
        List<string> events = [];
        var admissions = new RecordingCueAdmissions(events);
        admissions.Outcomes.Enqueue(
            accepted
                ? new OverlayCueAdmissionOutcome.Running(Guid.NewGuid())
                : new OverlayCueAdmissionOutcome.ParentDisabledOrCancelled()
        );
        var message = Identified("viewer", "streamer", "!cue", "cue-invocation", "stable");
        await using (
            var first = BuildServices(
                database,
                overlayCues: admissions,
                random: new ReplayRandom(17)
            )
        )
        {
            var dispatcher = first.GetRequiredService<ChatCommandDispatcher>();
            if (accepted)
            {
                _ = await Should.ThrowAsync<IOException>(async () =>
                    await dispatcher.DispatchResponsesAsync(
                        message,
                        static (_, _) =>
                            ValueTask.FromException(
                                new IOException("injected post-cue chat failure")
                            ),
                        default
                    )
                );
            }
            else
            {
                await dispatcher.DispatchResponsesAsync(
                    message,
                    (response, _) =>
                    {
                        events.Add(response.Message);
                        return ValueTask.CompletedTask;
                    },
                    default
                );
            }
        }
        (
            await values.ValueAsync(hostId, new(total.Id, string.Empty, string.Empty), default)
        )!.Value.ShouldBe("1");
        List<string> replay = [];
        await using (
            var restart = BuildServices(
                database,
                overlayCues: admissions,
                random: new ReplayRandom(92)
            )
        )
        {
            await restart
                .GetRequiredService<ChatCommandDispatcher>()
                .DispatchResponsesAsync(message, RecordMessages(replay), default);
        }
        replay.ShouldBe(accepted || order == OverlayCueReplyOrder.Before ? ["1:17"] : []);
        _ = admissions.Requests.ShouldHaveSingleItem();
        events.ShouldBe(order == OverlayCueReplyOrder.Before ? ["1:17", "admit"] : ["admit"]);
        (
            await values.ValueAsync(hostId, new(total.Id, string.Empty, string.Empty), default)
        )!.Value.ShouldBe("1");
    }
}
