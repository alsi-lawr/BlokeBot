# Frozen main-database workload protocol

The executable protocol is
`tools/BlokeBot.DatabaseWorkloads/protocol/blokebot-database-workloads-v1.json`. Its adjacent
`.sha256` file records the exact bytes, and the executable independently binds
`FrozenProtocolVersion.V1` to the same canonical SHA-256. Replacing both mutable files cannot
redefine v1; a future protocol requires a new explicit version binding. The protocol separates
deterministic inputs and logical outcomes from naturally variable provider timing.

## Execute

```sh
dotnet run -c Release --project tools/BlokeBot.DatabaseWorkloads -- verify-protocol \
  --protocol tools/BlokeBot.DatabaseWorkloads/protocol/blokebot-database-workloads-v1.json \
  --digest tools/BlokeBot.DatabaseWorkloads/protocol/blokebot-database-workloads-v1.json.sha256

dotnet run -c Release --project tools/BlokeBot.DatabaseWorkloads -- run-sqlite \
  --protocol tools/BlokeBot.DatabaseWorkloads/protocol/blokebot-database-workloads-v1.json \
  --digest tools/BlokeBot.DatabaseWorkloads/protocol/blokebot-database-workloads-v1.json.sha256 \
  --database .agent-workspace/sqlite-baseline/blokebot.db \
  --output .agent-workspace/sqlite-baseline/result.json
```

The database path must not exist. The tool never infers BlokeBot's production state path, never
opens an existing database, and contains no Twitch, HTTP, public-effect, or plugin-private database
adapter. It creates one synthetic database per repetition, retaining only the last database, and
writes aggregate evidence without SQL parameters, connection strings, absolute paths, or real
identities.

The SQLite executor uses the frozen WAL, synchronous, busy-timeout, pooling, and shared-cache
settings in the protocol. A zero SQLite busy timeout exposes write-admission waits to the harness;
the result records transaction admission events and their elapsed wait rather than treating the
provider's internal retry delay as application work.

## Comparison rules

BLOKEBOT-271 must run the unchanged protocol on SQLite and PostgreSQL with bounded host concurrency
and the same build. Correctness is a hard gate: every logical outcome and invariant must match, no
double claim or lost update is allowed, and every pre-admission cancellation must leave state
unchanged.

Timing is diagnostic rather than deterministic. Report each provider separately and flag:

- p95 latency worse by more than 20% and at least 5 ms, or p99 worse by more than 25% and 10 ms;
- throughput worse by more than 10%;
- provider wait/lock time above 5% of workload elapsed time or 20% above the SQLite baseline;
- main database plus WAL-equivalent growth more than 25% above the SQLite baseline;
- a claim or bounded public-read plan that changes to an unbounded scan at the frozen cardinality.

Treat a provider change as a material improvement only when it improves p95 by at least 20%,
throughput by at least 20%, or removes a measured wait bottleneck without violating correctness.
These thresholds do not by themselves authorize a provider switch.

## Hypotheses retained for comparison

SQLite serialization and its single-writer admission are provider-specific hypotheses. Context
creation, EF change tracking, JSON serialization, string-to-number conversions in points ordering,
large application transactions, and missing/ineffective indexes are provider-independent
hypotheses. A faster server provider will not necessarily improve those paths; query plans and
logical schedules must be reviewed with latency results.
