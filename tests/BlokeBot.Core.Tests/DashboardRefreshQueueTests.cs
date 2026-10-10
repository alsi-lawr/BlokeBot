using BlokeBot.Core.Features.Dashboards;
using Shouldly;

namespace BlokeBot.Core.Tests;

public class DashboardRefreshQueueTests
{
    [Test]
    public async Task StalledRead_DoesNotBlockSignal_Coalesces_AndResumesHiddenOnce()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var failures = new List<Exception>();
        await using var queue = new DashboardRefreshQueue(
            async () =>
            {
                if (Interlocked.Increment(ref reads) == 1)
                {
                    entered.SetResult();
                    await release.Task;
                }
            },
            failures.Add
        );
        queue.Request();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 100; i++)
        {
            queue.Request();
        }
        reads.ShouldBe(1);
        release.SetResult();
        await queue.WhenIdleAsync();
        reads.ShouldBe(2);
        queue.SetVisible(false);
        for (var i = 0; i < 100; i++)
        {
            queue.Request();
        }
        reads.ShouldBe(2);
        queue.SetVisible(true);
        await queue.WhenIdleAsync();
        reads.ShouldBe(3);
        await Task.Delay(50);
        reads.ShouldBe(3);
        failures.ShouldBeEmpty();
        await queue.DisposeAsync();
        queue.Request();
        await queue.WhenIdleAsync();
        reads.ShouldBe(3);
    }
}
