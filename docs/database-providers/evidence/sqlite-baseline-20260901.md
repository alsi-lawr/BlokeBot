# SQLite baseline evidence: 2026-09-01

This evidence was produced from source commit
`6e8af8ef6fc75df47bac7b15a283c851b2bfdc07` with protocol SHA-256
`b1901eb7d00a5a2c08650fd6619def775712f93db31c479559548414e4a180da`. The complete redacted
result is `sqlite-baseline-20260901.json` (SHA-256
`b38f7553a6c36ac5ac43adff32456a9317d7f6a7251b6678e754bf97d5734d01`).

Environment: .NET 10.0.11, SQLite 3.50.4, NixOS 26.11 x64, 16 logical processors. The runner used
one warmup and three measured fresh databases with one synthetic host, 1,000 synthetic viewers,
and a 120-message synthetic outbox. It made no external request and opened no existing database.

## Measurements

| Workload | p50 ms | p95 ms | p99 ms | operations/s | throughput variation | lock events | lock wait ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| automation admission/checkpointing | 0.401 | 150.623 | 150.682 | 15.815 | 4.125% | 152 | 22,673.916 |
| public-chat outbox claims | 0.712 | 150.780 | 151.017 | 14.117 | 4.496% | 169 | 25,375.543 |
| configuration activation | 0.319 | 150.467 | 150.513 | 13.635 | 2.090% | 117 | 17,563.857 |
| points/community writes | 0.427 | 150.572 | 153.865 | 16.036 | 2.001% | 248 | 37,233.891 |
| plugin feature state | 0.181 | 150.269 | 150.294 | 33.078 | 14.548% | 48 | 7,204.998 |
| public reads | 0.305 | 0.415 | 0.575 | 2,977.756 | 2.306% | 0 | 0.000 |

The fresh database grew to 2,838,528 bytes and its WAL to 4,136,512 bytes, for 6,975,040 bytes of
new storage. Busy/locked wait is summed transaction-admission time and may exceed wall time because
the two scheduled writers wait concurrently.

All three measured databases produced the same validated outcomes: 109 automation receipts and
completed runs, 120 terminal outbox messages, 80 completed activations, 191 atomic points/community
writes, total synthetic point balance 499,691, total community progress 49,691, and plugin revision
41. Every scheduled pre-admission cancellation wrote nothing.

## Findings

- The SQLite write workloads are bimodal: sub-millisecond medians but about 150 ms at p95 while the
  second writer waits for admission. Points/community writes have the most lock events and total
  wait in this fixture. This is a measured SQLite bottleneck.
- Public reads remain below 0.5 ms at p99 and have no lock admission. The points leaderboard plan
  uses the host/login index but builds a temporary B-tree for numeric ordering. That sort and the
  string-to-integer conversion are provider-independent bottleneck hypotheses at larger
  cardinalities.
- Configuration activation scans the bounded activation table and builds a temporary B-tree for
  ordering. The current fixture is small, so the plan is evidence to compare rather than proof of a
  user-visible bottleneck.
- Plugin feature state throughput varied by 14.548% across the three repetitions. Later provider
  comparison must retain repeated runs and must not treat a single plugin-state timing as stable.
- Outbox claims use the covering status/time/id index. The later provider plan must retain a bounded
  indexed claim rather than replace it with an unbounded scan.

No PostgreSQL application result was measured or inferred. BLOKEBOT-271 owns the later
SQLite/PostgreSQL execution and semantic comparison.
