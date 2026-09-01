namespace BlokeBot.DatabaseWorkloads;

public sealed record BaselineResult(
    int SchemaVersion,
    string ProtocolId,
    string SourceCommit,
    string ProtocolSha256,
    string Provider,
    string ProviderVersion,
    ExecutionEnvironment Environment,
    int Seed,
    int Repetitions,
    FixtureCardinalities Fixture,
    IReadOnlyList<WorkloadResult> Workloads,
    StorageResult Storage,
    IReadOnlyList<QueryPlanResult> QueryPlans,
    IReadOnlyDictionary<string, long> LogicalOutcomes,
    bool Redacted
);

public sealed record ExecutionEnvironment(
    string RuntimeVersion,
    string OperatingSystem,
    string ProcessArchitecture,
    int ProcessorCount
);

public sealed record WorkloadResult(
    WorkloadId Id,
    long AttemptedOperations,
    long CommittedOperations,
    long ExpectedConflicts,
    long Cancellations,
    double MedianLatencyMilliseconds,
    double P95LatencyMilliseconds,
    double P99LatencyMilliseconds,
    double ThroughputPerSecond,
    double ThroughputVariationPercent,
    long BusyLockedEvents,
    double BusyLockedWaitMilliseconds
);

public sealed record StorageResult(long DatabaseBytes, long WalBytes, long TotalBytes);

public sealed record QueryPlanResult(string Workload, IReadOnlyList<string> Steps);

internal sealed class WorkloadMeasurements
{
    private readonly object _gate = new();
    private readonly List<double> _latencies = [];
    private readonly List<double> _repetitionElapsedSeconds = [];

    public long Attempted { get; private set; }
    public long Committed { get; private set; }
    public long ExpectedConflicts { get; private set; }
    public long Cancellations { get; private set; }
    public long BusyEvents { get; private set; }
    public double BusyWaitMilliseconds { get; private set; }
    public double ElapsedSeconds { get; set; }

    public void AddElapsed(double seconds)
    {
        lock (_gate)
        {
            ElapsedSeconds += seconds;
            _repetitionElapsedSeconds.Add(seconds);
        }
    }

    public void Record(
        double latencyMilliseconds,
        OperationOutcome outcome,
        long busyEvents,
        double busyWaitMilliseconds
    )
    {
        lock (_gate)
        {
            Attempted++;
            _latencies.Add(latencyMilliseconds);
            BusyEvents += busyEvents;
            BusyWaitMilliseconds += busyWaitMilliseconds;
            switch (outcome)
            {
                case OperationOutcome.Committed:
                    Committed++;
                    break;
                case OperationOutcome.ExpectedConflict:
                    ExpectedConflicts++;
                    break;
                case OperationOutcome.Cancelled:
                    Cancellations++;
                    break;
            }
        }
    }

    public WorkloadResult ToResult(WorkloadId id, int repetitions)
    {
        var ordered = _latencies.Order().ToArray();
        if (_repetitionElapsedSeconds.Count != repetitions)
        {
            throw new InvalidDataException("The workload repetition count is incomplete.");
        }
        var operationsPerRepetition = (Attempted - Cancellations) / (double)repetitions;
        var repetitionThroughput = _repetitionElapsedSeconds
            .Select(seconds => seconds == 0 ? 0 : operationsPerRepetition / seconds)
            .ToArray();
        return new(
            id,
            Attempted,
            Committed,
            ExpectedConflicts,
            Cancellations,
            Percentile(ordered, 0.50),
            Percentile(ordered, 0.95),
            Percentile(ordered, 0.99),
            ElapsedSeconds == 0 ? 0 : (Attempted - Cancellations) / ElapsedSeconds,
            CoefficientOfVariation(repetitionThroughput),
            BusyEvents,
            BusyWaitMilliseconds
        );
    }

    private static double Percentile(double[] ordered, double percentile)
    {
        if (ordered.Length == 0)
        {
            return 0;
        }
        var index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
        return Math.Round(ordered[Math.Clamp(index, 0, ordered.Length - 1)], 3);
    }

    private static double CoefficientOfVariation(double[] values)
    {
        var mean = values.Average();
        if (mean == 0)
        {
            return 0;
        }
        var variance = values.Average(value => Math.Pow(value - mean, 2));
        return Math.Round(Math.Sqrt(variance) / mean * 100, 3);
    }
}

internal enum OperationOutcome
{
    Committed,
    ExpectedConflict,
    Cancelled,
}
