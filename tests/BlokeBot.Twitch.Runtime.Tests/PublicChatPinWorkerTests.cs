using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class PublicChatPinWorkerTests
{
    [Test]
    [Arguments(false, "claim,begin,execute,complete")]
    [Arguments(true, "claim,execute,complete")]
    public async Task Worker_BeginsReadyBeforeOneExecution_AndReconcilesWithoutBeginning(
        bool reconcileOnly,
        string expectedCalls
    )
    {
        var calls = new List<string>();
        var store = new RecordingStore(WorkItem(reconcileOnly), calls);
        var provider = new RecordingProvider(calls);
        var worker = new PublicChatPinWorker(
            store,
            provider,
            TimeProvider.System,
            NullLogger<PublicChatPinWorker>.Instance
        );

        await worker.StartAsync(CancellationToken.None);
        await store.Completed.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        calls.ShouldBe(expectedCalls.Split(','));
        provider.ExecutionCount.ShouldBe(1);
        provider.ExecutedReconcileOnly.ShouldBe(reconcileOnly);
        store.BeginAttemptCount.ShouldBe(reconcileOnly ? 0 : 1);
    }

    private static PublicChatPinWorkItem WorkItem(bool reconcileOnly) =>
        new(
            1,
            reconcileOnly,
            false,
            1,
            "streamer",
            "guessing",
            "round_started",
            1,
            "message-id",
            null,
            300,
            true
        );

    private sealed class RecordingStore(PublicChatPinWorkItem item, List<string> calls)
        : IPublicChatPinStore
    {
        private readonly TaskCompletionSource _completed = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _claimCount;

        public Task Completed => _completed.Task;

        public int BeginAttemptCount { get; private set; }

        public async ValueTask<PublicChatPinWorkItem?> TryClaimAsync(
            CancellationToken cancellationToken
        )
        {
            if (Interlocked.Increment(ref _claimCount) == 1)
            {
                calls.Add("claim");
                return item;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }

        public ValueTask<bool> BeginAttemptAsync(
            PublicChatPinWorkItem workItem,
            CancellationToken cancellationToken
        )
        {
            BeginAttemptCount++;
            calls.Add("begin");
            return ValueTask.FromResult(true);
        }

        public ValueTask CompleteAsync(
            PublicChatPinWorkItem workItem,
            PublicChatPinExecutionOutcome outcome,
            CancellationToken cancellationToken
        )
        {
            calls.Add("complete");
            _completed.SetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingProvider(List<string> calls) : IPublicChatPinProvider
    {
        public int ExecutionCount { get; private set; }

        public bool ExecutedReconcileOnly { get; private set; }

        public ValueTask<PublicChatPinExecutionOutcome> ExecuteAsync(
            PublicChatPinWorkItem item,
            CancellationToken cancellationToken
        )
        {
            ExecutionCount++;
            ExecutedReconcileOnly = item.ReconcileOnly;
            calls.Add("execute");
            return ValueTask.FromResult<PublicChatPinExecutionOutcome>(
                new PublicChatPinExecutionOutcome.NoOp("recorded")
            );
        }
    }
}
