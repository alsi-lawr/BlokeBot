using System.Threading.Channels;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public abstract partial class EventSubChannelRecoveryTestBase
{
    private protected sealed class RecordingDiagnostics : IEventSubChannelDiagnosticReporter
    {
        private readonly object _gate = new();
        private readonly List<EventSubChannelDiagnosticReport> _reports = [];
        private readonly Queue<Exception> _failures = [];
        private readonly Channel<EventSubChannelStatus> _transitions =
            Channel.CreateUnbounded<EventSubChannelStatus>();

        internal IReadOnlyList<EventSubChannelStatus> Reports
        {
            get
            {
                lock (_gate)
                {
                    return _reports.Select(report => report.Status).ToArray();
                }
            }
        }

        internal IReadOnlyList<EventSubChannelDiagnosticReport> DiagnosticReports
        {
            get
            {
                lock (_gate)
                {
                    return _reports.ToArray();
                }
            }
        }

        public void Report(EventSubChannelDiagnosticReport report)
        {
            lock (_gate)
            {
                if (_failures.TryDequeue(out var failure))
                {
                    throw failure;
                }
                _reports.Add(report);
            }
            _transitions.Writer.TryWrite(report.Status).ShouldBeTrue();
        }

        internal void EnqueueFailure(Exception failure)
        {
            lock (_gate)
            {
                _failures.Enqueue(failure);
            }
        }

        internal void Clear()
        {
            lock (_gate)
            {
                _reports.Clear();
            }
            while (_transitions.Reader.TryRead(out _)) { }
        }

        internal ValueTask<EventSubChannelStatus> NextAsync() => _transitions.Reader.ReadAsync();
    }

    private protected sealed class FixedTimeProvider(DateTimeOffset initialNow) : TimeProvider
    {
        private readonly object _gate = new();
        private readonly HashSet<ManualTimer> _timers = [];
        private DateTimeOffset _now = initialNow;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _now;
            }
        }

        public override long GetTimestamp() => GetUtcNow().UtcTicks;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            var timer = new ManualTimer(this, callback, state);
            _ = timer.Change(dueTime, period);
            return timer;
        }

        internal void Advance(TimeSpan duration)
        {
            ManualTimer[] due;
            lock (_gate)
            {
                _now = _now.Add(duration);
                due = _timers.Where(timer => timer.IsDue(_now)).ToArray();
            }
            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        private void Add(ManualTimer timer)
        {
            lock (_gate)
            {
                _ = _timers.Add(timer);
            }
        }

        private void Remove(ManualTimer timer)
        {
            lock (_gate)
            {
                _ = _timers.Remove(timer);
            }
        }

        private sealed class ManualTimer(
            FixedTimeProvider owner,
            TimerCallback callback,
            object? state
        ) : ITimer
        {
            private TimeSpan _period = Timeout.InfiniteTimeSpan;
            private DateTimeOffset _dueAt = DateTimeOffset.MaxValue;
            private bool _disposed;

            internal bool IsDue(DateTimeOffset current) => !_disposed && _dueAt <= current;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate)
                {
                    if (_disposed)
                    {
                        return false;
                    }
                    _period = period;
                    _dueAt =
                        dueTime == Timeout.InfiniteTimeSpan
                            ? DateTimeOffset.MaxValue
                            : owner._now.Add(dueTime);
                    owner.Add(this);
                    return true;
                }
            }

            internal void Fire()
            {
                lock (owner._gate)
                {
                    if (!IsDue(owner._now))
                    {
                        return;
                    }
                    _dueAt =
                        _period == Timeout.InfiniteTimeSpan
                            ? DateTimeOffset.MaxValue
                            : owner._now.Add(_period);
                }
                callback(state);
            }

            public void Dispose()
            {
                lock (owner._gate)
                {
                    if (_disposed)
                    {
                        return;
                    }
                    _disposed = true;
                    owner.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
