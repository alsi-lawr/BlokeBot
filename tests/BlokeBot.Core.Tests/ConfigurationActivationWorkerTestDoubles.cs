using System.Collections.Concurrent;
using System.Data.Common;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationActivationTests
{
    private sealed class BlockingObserver : IHostFeatureActivationObserver
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
            HostFeatureActivationChange change,
            CancellationToken cancellationToken
        )
        {
            _ = Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HostFeatureAutomaticWorkResult.Complete();
        }
    }

    private sealed class GateObserver : IHostFeatureActivationObserver
    {
        internal TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseFirst { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<HostFeatureActivationChange> Changes { get; } = [];

        public async ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
            HostFeatureActivationChange change,
            CancellationToken cancellationToken
        )
        {
            Changes.Add(change);
            if (Changes.Count == 1)
            {
                _ = FirstStarted.TrySetResult();
                await ReleaseFirst.Task.WaitAsync(cancellationToken);
            }
            return new HostFeatureAutomaticWorkResult.Complete();
        }
    }

    private sealed class TerminalOutcomePersistenceBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _paused = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _armed;
        private int _intercepted;

        internal void Arm() => _ = Interlocked.Exchange(ref _armed, 1);

        internal async Task WaitUntilPausedAsync() =>
            await _paused.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Volatile.Read(ref _armed) == 1
                && command.CommandText.Contains(
                    "UPDATE \"configuration_activations\"",
                    StringComparison.Ordinal
                )
                && command.CommandText.Contains("\"CompletedAtUtc\"", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0
            )
            {
                _ = _paused.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return result;
        }
    }

    private sealed class ClaimSelectionBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _paused = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _released = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _armed;
        private int _candidateSelectCount;

        internal int CandidateSelectCount => Volatile.Read(ref _candidateSelectCount);

        internal void Arm() => _ = Interlocked.Exchange(ref _armed, 1);

        internal async Task WaitUntilPausedAsync() =>
            await _paused.Task.WaitAsync(TimeSpan.FromSeconds(5));

        internal void Release() => _ = _released.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Volatile.Read(ref _armed) == 1
                && command.CommandText.Contains(
                    "FROM \"configuration_activations\" AS \"c\"",
                    StringComparison.Ordinal
                )
                && command.CommandText.Contains("LIMIT 1", StringComparison.Ordinal)
            )
            {
                var count = Interlocked.Increment(ref _candidateSelectCount);
                if (count == 1)
                {
                    _ = _paused.TrySetResult();
                    await _released.Task.WaitAsync(cancellationToken);
                }
            }

            return result;
        }
    }

    private sealed class ClaimTransactionProbe : DbTransactionInterceptor
    {
        // Observe the signal before this callback enters SQLite's blocking BEGIN;
        // an asynchronously queued handoff can time out while the first claim is paused.
        private readonly TaskCompletionSource _secondStarted = new();
        private int _armed;
        private int _started;

        internal void Arm() => _ = Interlocked.Exchange(ref _armed, 1);

        internal async Task WaitForSecondStartAsync() =>
            await _secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Volatile.Read(ref _armed) == 1 && Interlocked.Increment(ref _started) == 2)
            {
                _ = _secondStarted.TrySetResult();
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class HostLeaseObserver(int serializedHostId, HostFeatureFlags blockedFeature)
        : IHostFeatureActivationObserver
    {
        private readonly TaskCompletionSource _releaseBlockedChange = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _serializedHostConcurrency;
        private int _maximumSerializedHostConcurrency;

        internal TaskCompletionSource BlockedChangeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConcurrentQueue<HostFeatureActivationChange> Changes { get; } = [];
        internal int MaximumSerializedHostConcurrency =>
            Volatile.Read(ref _maximumSerializedHostConcurrency);

        internal void ReleaseBlockedChange() => _ = _releaseBlockedChange.TrySetResult();

        public async ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
            HostFeatureActivationChange change,
            CancellationToken cancellationToken
        )
        {
            Changes.Enqueue(change);
            if (change.HostId != serializedHostId)
            {
                return new HostFeatureAutomaticWorkResult.Complete();
            }

            var concurrency = Interlocked.Increment(ref _serializedHostConcurrency);
            _ = Interlocked.Exchange(
                ref _maximumSerializedHostConcurrency,
                Math.Max(MaximumSerializedHostConcurrency, concurrency)
            );
            try
            {
                if (change.Feature == blockedFeature)
                {
                    _ = BlockedChangeStarted.TrySetResult();
                    await _releaseBlockedChange.Task.WaitAsync(cancellationToken);
                }
            }
            finally
            {
                _ = Interlocked.Decrement(ref _serializedHostConcurrency);
            }

            return new HostFeatureAutomaticWorkResult.Complete();
        }
    }
}
