using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime.Tests;

public abstract partial class PublicChatMessageQueueTestBase
{
    private protected sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add(new LogEntry(formatter(state, exception), exception));
    }

    private protected sealed record LogEntry(string Message, Exception? Exception);

    private protected sealed class ManualTimeProvider(DateTimeOffset initialNow) : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private readonly Channel<ManualTimer> _timerRegistrations =
            Channel.CreateUnbounded<ManualTimer>();

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _currentNowLocked;
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
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            List<ManualTimer> due;
            lock (_gate)
            {
                _currentNowLocked = _currentNowLocked.Add(delta);
                due = _timers.Where(timer => timer.IsDue(_currentNowLocked)).ToList();
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        public async ValueTask WaitForTimerRegistrationAsync()
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_timers.Count > 0)
                    {
                        return;
                    }
                }

                _ = await _timerRegistrations.Reader.ReadAsync();
            }
        }

        public async ValueTask WaitForTimerAtAsync(DateTimeOffset dueAt)
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_timers.Any(timer => timer.IsScheduledAt(dueAt)))
                    {
                        return;
                    }
                }

                _ = await _timerRegistrations.Reader.ReadAsync();
            }
        }

        private void AddTimer(ManualTimer timer)
        {
            lock (_gate)
            {
                if (!_timers.Contains(timer))
                {
                    _timers.Add(timer);
                }

                if (!_timerRegistrations.Writer.TryWrite(timer))
                {
                    throw new InvalidOperationException(
                        "The timer observer could not be notified."
                    );
                }
            }
        }

        private void RemoveTimer(ManualTimer timer)
        {
            lock (_gate)
            {
                _timers.Remove(timer);
            }
        }

        private DateTimeOffset _currentNowLocked { get; set; } = initialNow;

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state
        ) : ITimer
        {
            private TimeSpan _period;
            private DateTimeOffset _dueAt = DateTimeOffset.MaxValue;
            private bool _disposed;

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
                            : owner._currentNowLocked.Add(dueTime);
                    owner.AddTimer(this);
                }

                if (dueTime != Timeout.InfiniteTimeSpan && dueTime <= TimeSpan.Zero)
                {
                    Fire();
                }

                return true;
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
                    owner.RemoveTimer(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public bool IsDue(DateTimeOffset value)
            {
                lock (owner._gate)
                {
                    return !_disposed && _dueAt <= value;
                }
            }

            public bool IsScheduledAt(DateTimeOffset value)
            {
                lock (owner._gate)
                {
                    return !_disposed && _dueAt == value;
                }
            }

            public void Fire()
            {
                lock (owner._gate)
                {
                    if (_disposed || _dueAt > owner._currentNowLocked)
                    {
                        return;
                    }

                    if (_period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan)
                    {
                        _dueAt = owner._currentNowLocked.Add(_period);
                    }
                    else
                    {
                        _disposed = true;
                        owner.RemoveTimer(this);
                    }
                }

                callback(state);
            }
        }
    }
}
