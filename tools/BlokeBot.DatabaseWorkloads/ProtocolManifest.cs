using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.DatabaseWorkloads;

public sealed record WorkloadProtocol(
    int SchemaVersion,
    string ProtocolId,
    string SourceCommit,
    int Seed,
    int Repetitions,
    int WarmupRepetitions,
    FixtureCardinalities Fixture,
    SqliteExecutionProtocol SqliteExecution,
    ConcurrencyProtocol Concurrency,
    IReadOnlyList<WorkloadDefinition> Workloads,
    IReadOnlyList<string> ResultFields,
    RedactionProtocol Redaction
);

public sealed record FixtureCardinalities(int Hosts, int Viewers, int PublicChatBacklog);

public sealed record SqliteExecutionProtocol(
    string JournalMode,
    string Synchronous,
    int BusyTimeoutMilliseconds,
    bool Pooling,
    string Cache
);

public sealed record ConcurrencyProtocol(
    string Schedule,
    int Writers,
    int Readers,
    int MaxBusyRetries,
    int BusyRetryDelayMilliseconds
);

public sealed record WorkloadDefinition(
    WorkloadId Id,
    int Operations,
    int DuplicateEvery,
    string CancellationPoint,
    IReadOnlyList<string> Invariants,
    IReadOnlyList<string> Metrics
);

[JsonConverter(typeof(JsonStringEnumConverter<WorkloadId>))]
public enum WorkloadId
{
    AutomationAdmissionCheckpointing,
    PublicChatOutboxClaims,
    ConfigurationActivation,
    PointsCommunityWrites,
    PluginFeatureState,
    PublicReads,
}

public sealed record RedactionProtocol(
    string IdentityShape,
    bool IncludeSqlParameters,
    bool IncludeAbsolutePaths,
    bool IncludeConnectionStrings
);

public sealed class ProtocolDriftException(string message) : Exception(message);

public enum FrozenProtocolVersion
{
    V1,
}

public static class FrozenProtocol
{
    private const string _v1Sha256 =
        "b1901eb7d00a5a2c08650fd6619def775712f93db31c479559548414e4a180da";
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static WorkloadProtocol Load(
        FrozenProtocolVersion version,
        string protocolPath,
        string digestPath
    )
    {
        var protocolBytes = File.ReadAllBytes(protocolPath);
        var sidecarDigest = File.ReadAllText(digestPath).Trim();
        var canonicalDigest = CanonicalDigest(version);
        var actualDigest = Convert.ToHexStringLower(SHA256.HashData(protocolBytes));
        if (
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(sidecarDigest),
                Convert.FromHexString(actualDigest)
            )
        )
        {
            throw new ProtocolDriftException(
                $"Protocol sidecar mismatch. Expected {sidecarDigest}; actual {actualDigest}."
            );
        }
        if (
            !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(canonicalDigest),
                Convert.FromHexString(actualDigest)
            )
        )
        {
            throw new ProtocolDriftException(
                $"Protocol version {version} is not the canonical frozen document."
            );
        }

        var protocol =
            JsonSerializer.Deserialize<WorkloadProtocol>(protocolBytes, _jsonOptions)
            ?? throw new ProtocolDriftException("The protocol document is empty.");
        Validate(protocol);
        return protocol;
    }

    public static string Digest(string protocolPath) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(protocolPath)));

    public static string CanonicalDigest(FrozenProtocolVersion version) =>
        version switch
        {
            FrozenProtocolVersion.V1 => _v1Sha256,
        };

    private static void Validate(WorkloadProtocol protocol)
    {
        if (
            protocol.SchemaVersion != 1
            || protocol.ProtocolId != "blokebot-database-workloads-v1"
            || protocol.SourceCommit != "6e8af8ef6fc75df47bac7b15a283c851b2bfdc07"
        )
        {
            throw new ProtocolDriftException("Unsupported workload protocol identity.");
        }
        if (protocol.Seed <= 0 || protocol.Repetitions < 3 || protocol.WarmupRepetitions < 1)
        {
            throw new ProtocolDriftException("The repeatability settings are not valid.");
        }
        if (
            protocol.Fixture.Hosts != 1
            || protocol.Fixture.Viewers < 100
            || protocol.Fixture.PublicChatBacklog < 10
        )
        {
            throw new ProtocolDriftException("The fixture is not production-shaped.");
        }
        if (
            protocol.SqliteExecution.JournalMode != "wal"
            || protocol.SqliteExecution.Synchronous != "normal"
            || protocol.SqliteExecution.BusyTimeoutMilliseconds != 0
            || protocol.SqliteExecution.Pooling
            || protocol.SqliteExecution.Cache != "shared"
        )
        {
            throw new ProtocolDriftException("The SQLite execution settings are invalid.");
        }
        if (
            protocol.Concurrency.Schedule != "round_barrier"
            || protocol.Concurrency.Writers != 2
            || protocol.Concurrency.Readers < 1
            || protocol.Concurrency.MaxBusyRetries < 1
            || protocol.Concurrency.BusyRetryDelayMilliseconds < 0
        )
        {
            throw new ProtocolDriftException("The deterministic concurrency schedule is invalid.");
        }

        var expected = new HashSet<WorkloadId>([
            WorkloadId.AutomationAdmissionCheckpointing,
            WorkloadId.PublicChatOutboxClaims,
            WorkloadId.ConfigurationActivation,
            WorkloadId.PointsCommunityWrites,
            WorkloadId.PluginFeatureState,
            WorkloadId.PublicReads,
        ]);
        if (
            protocol.Workloads.Count != expected.Count
            || !expected.SetEquals(protocol.Workloads.Select(static workload => workload.Id))
            || protocol.Workloads.Any(static workload =>
                workload.Operations < 10
                || workload.Invariants.Count == 0
                || workload.Metrics.Count == 0
                || string.IsNullOrWhiteSpace(workload.CancellationPoint)
            )
        )
        {
            throw new ProtocolDriftException("The required workload contract is incomplete.");
        }
        if (
            protocol.Redaction.IdentityShape != "synthetic-seed-derived"
            || protocol.Redaction.IncludeSqlParameters
            || protocol.Redaction.IncludeAbsolutePaths
            || protocol.Redaction.IncludeConnectionStrings
        )
        {
            throw new ProtocolDriftException("The protocol would disclose sensitive evidence.");
        }
    }
}
