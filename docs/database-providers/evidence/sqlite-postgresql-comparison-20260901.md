# SQLite and PostgreSQL workload comparison: 2026-09-01

The comparison used the unchanged `blokebot-database-workloads-v1` document with SHA-256
`b1901eb7d00a5a2c08650fd6619def775712f93db31c479559548414e4a180da`. Each provider ran one
warmup and three measured repetitions with the same seed, operation order, two-writer barriers,
cancellation points, fixture, and logical outcome checks.

The redacted result files are:

- `sqlite-comparison-20260901.json` (SHA-256
  `3da5ec278feb630e2893e14e891330e7652612133b3b044db7259726fbd1b10c`)
- `postgresql-comparison-20260901.json` (SHA-256
  `1e47982c8b54a67d6513b325f73baf6593443ce02acd53ef7639f8b6ab0d63c5`)

Environment: .NET 10.0.11, NixOS 26.11 x64, 16 logical processors, SQLite 3.50.4, and disposable
PostgreSQL 17.11. The PostgreSQL runner created only `blokebot_workload_v1`, refused an existing
schema with that name, and removed the owned schema after the run. Neither result contains a path,
SQL parameter, identity, or connection string.

## Results

| Workload | SQLite p95 ms | PostgreSQL p95 ms | SQLite operations/s | PostgreSQL operations/s |
| --- | ---: | ---: | ---: | ---: |
| automation admission and checkpointing | 150.496 | 21.471 | 16.681 | 112.292 |
| public-chat outbox claims | 150.764 | 24.496 | 14.603 | 83.877 |
| configuration activation | 150.444 | 23.709 | 14.371 | 86.248 |
| points and community writes | 150.570 | 25.785 | 15.911 | 95.005 |
| plugin feature state | 150.266 | 16.639 | 39.476 | 90.164 |
| public reads | 0.500 | 7.625 | 2,931.868 | 552.856 |

SQLite used 2,830,336 database bytes plus 4,136,512 WAL bytes. The PostgreSQL schema used 6,094,848
relation and index bytes. PostgreSQL WAL is server-wide and is therefore reported as zero rather
than attributed incorrectly to this schema.

The frozen `busy_locked` fields measure observed transaction-admission delay and retry events under
the deterministic schedule. They are comparable runner signals, not PostgreSQL server lock-view
telemetry. PostgreSQL recorded 116 to 186 write admission/retry events depending on the workload;
public reads recorded none.

## Semantic result

Both providers produced the same outcomes in every measured repetition:

- 109 automation receipts and 109 completed runs;
- 120 expired public-chat messages;
- 80 completed configuration activations;
- 191 points/community receipts and ledger rows;
- total point balance 499,691 and total community progress 49,691;
- plugin feature revision 41; and
- one pre-admission cancellation per workload with no database change.

The separate PostgreSQL authority verifier also passed deterministic duplicate receipt admission,
bounded immediate-write admission, cancellation before admission, automation/community/command/
raid/viewer idempotent writes, guarded raid collaboration, and PostgreSQL JSON flow selection. It
verified that malformed provenance is ignored while valid provenance and ledger-owned flows are
removed, and left no workload schema behind.

These measurements describe this synthetic fixture. They show that the measured SQLite write
admission bottleneck does not reproduce on PostgreSQL in the same form. They do not establish a
production capacity limit or authorize a production provider switch.
