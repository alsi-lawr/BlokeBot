using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BlokeBot.Simulation.Workloads;

// Measurement-only: labels describe the synthetic journey, never SQL, parameters or identities.
internal sealed class PortalWorkloadProbe
    : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>,
        IDisposable
{
    private readonly AsyncLocal<Sample?> _current = new();
    private readonly ConcurrentDictionary<Guid, Sample[]> _commands = new();
    private readonly ConcurrentQueue<Sample> _samples = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly IDisposable _listeners;
    private readonly ActivityListener _activities;
    private readonly Sample _unattributed;
    private readonly ConcurrentDictionary<Activity, Sample> _backgroundOwners = new();

    public PortalWorkloadProbe()
    {
        _unattributed = new Sample("process.unattributed", null, this);
        _samples.Enqueue(_unattributed);
        _listeners = DiagnosticListener.AllListeners.Subscribe(this);
        _activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "BlokeBot.ViewerPortal",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                var sample = new Sample("owner.read", null, this);
                _samples.Enqueue(sample);
                _backgroundOwners[activity] = sample;
            },
            ActivityStopped = activity =>
            {
                if (_backgroundOwners.TryRemove(activity, out var sample))
                {
                    sample.Outcome =
                        $"{activity.GetTagItem("portal.owner")}:{activity.GetTagItem("portal.audience")}:{activity.GetTagItem("portal.outcome")}";
                    sample.Complete();
                }
            },
        };
        ActivitySource.AddActivityListener(_activities);
    }

    private Sample[] CurrentSamples()
    {
        var result = new List<Sample>();
        if (_current.Value is { } current)
        {
            result.Add(current);
        }
        else
        {
            result.Add(_unattributed);
        }
        for (var activity = Activity.Current; activity is not null; activity = activity.Parent)
        {
            if (_backgroundOwners.TryGetValue(activity, out var sample))
            {
                result.Add(sample);
                break;
            }
        }
        return result.ToArray();
    }

    internal Sample Begin(string name)
    {
        var sample = new Sample(name, _current.Value, this);
        _samples.Enqueue(sample);
        _current.Value = sample;
        return sample;
    }

    internal object[] Snapshot() => _samples.Select(sample => sample.Snapshot()).ToArray();

    public void OnNext(DiagnosticListener value)
    {
        if (value.Name == "Microsoft.EntityFrameworkCore")
        {
            _subscriptions.Add(value.Subscribe(this));
        }
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        switch (value.Value)
        {
            case CommandEventData command when value.Key == RelationalEventId.CommandExecuting.Name:
                var samples = CurrentSamples();
                if (_commands.TryAdd(command.CommandId, samples))
                {
                    foreach (var sample in samples)
                    {
                        sample.StartCommand();
                    }
                }
                break;
            case CommandExecutedEventData executed:
                if (_commands.TryGetValue(executed.CommandId, out var completedSample))
                {
                    foreach (var sample in completedSample)
                    {
                        sample.CommandCompleted(executed.Duration);
                    }
                    if (
                        executed.ExecuteMethod != DbCommandMethod.ExecuteReader
                        && _commands.TryRemove(executed.CommandId, out _)
                    )
                    {
                        foreach (var sample in completedSample)
                        {
                            sample.FinishCommand();
                        }
                    }
                }
                break;
            case CommandEndEventData cancelled
                when value.Key == RelationalEventId.CommandCanceled.Name:
                if (_commands.TryRemove(cancelled.CommandId, out var cancellations))
                {
                    foreach (var sample in cancellations)
                    {
                        sample.CommandCancelled();
                    }
                }
                break;
            case CommandErrorEventData error:
                if (_commands.TryRemove(error.CommandId, out var failed))
                {
                    foreach (var sample in failed)
                    {
                        sample.CommandFailed();
                    }
                }
                break;
            case DataReaderDisposingEventData reader:
                if (_commands.TryRemove(reader.CommandId, out var read))
                {
                    foreach (var sample in read)
                    {
                        sample.ReaderDisposed(reader.ReadCount, reader.Duration);
                    }
                }
                break;
        }
    }

    public void OnError(Exception error) { }

    public void OnCompleted() { }

    public void Dispose()
    {
        _listeners.Dispose();
        _activities.Dispose();
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    internal sealed class Sample(string name, Sample? previous, PortalWorkloadProbe owner)
        : IDisposable
    {
        private readonly object _gate = new();
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private int _started;
        private int _completed;
        private int _errors;
        private int _cancellations;
        private int _active;
        private int _peak;
        private long _readerReadOperations;
        private double _commandMilliseconds;
        private double _readerMilliseconds;

        internal bool IsComplete => !_elapsed.IsRunning;

        internal void Complete() => _elapsed.Stop();

        internal string Outcome { get; set; } = "completed";
        internal int SummaryJsonBytes { get; set; }

        internal void StartCommand()
        {
            lock (_gate)
            {
                _started++;
                _peak = Math.Max(_peak, ++_active);
            }
        }

        internal void CommandCompleted(TimeSpan duration)
        {
            lock (_gate)
            {
                _completed++;
                _commandMilliseconds += duration.TotalMilliseconds;
            }
        }

        internal void FinishCommand()
        {
            lock (_gate)
            {
                _active--;
            }
        }

        internal void CommandCancelled()
        {
            lock (_gate)
            {
                _cancellations++;
                _active--;
            }
        }

        internal void CommandFailed()
        {
            lock (_gate)
            {
                _errors++;
                _active--;
            }
        }

        internal void ReaderDisposed(int reads, TimeSpan duration)
        {
            lock (_gate)
            {
                _readerReadOperations += reads;
                _readerMilliseconds += duration.TotalMilliseconds;
                _active--;
            }
        }

        internal object Snapshot()
        {
            lock (_gate)
            {
                return new
                {
                    Name = name,
                    IsComplete,
                    ElapsedMilliseconds = _elapsed.Elapsed.TotalMilliseconds,
                    CommandsStarted = _started,
                    CommandsCompleted = _completed,
                    CommandErrors = _errors,
                    CommandCancellations = _cancellations,
                    OutstandingCommandsOrReaders = _active,
                    PeakCommandsOrReaders = _peak,
                    ReaderReadOperations = _readerReadOperations,
                    CommandMilliseconds = _commandMilliseconds,
                    ReaderMilliseconds = _readerMilliseconds,
                    Outcome,
                    SummaryJsonBytes,
                };
            }
        }

        public void Dispose()
        {
            _elapsed.Stop();
            owner._current.Value = previous;
        }
    }
}

internal sealed class PortalWorkloadCircuitProbe(PortalWorkloadProbe probe) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next
    ) =>
        async context =>
        {
            using var sample = probe.Begin("circuit.inbound-activity");
            await next(context);
        };
}
