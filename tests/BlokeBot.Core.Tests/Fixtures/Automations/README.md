# Pre-latest frozen execution fixture

`pre-latest-frozen.sqlite.gz` was produced by the actual pre-amendment services and runtime at
`aff0a78106e408fdb5342821e569a5ef0d2c0ba3`, not by the new parser or by hand-editing a frozen document.
All data comes from the existing synthetic `AutomationRuntimeTests.RuntimeFixture`.

The adjacent generator publishes a leaf and a nested caller, saves an ordinary caller, and admits it.
The runtime acknowledges `acknowledged-before-upgrade`, checkpoints a one-second delay, and remains
Waiting with `frozen-after-upgrade` still to execute. The database includes the original schema-1
Invoke documents, immutable closure, run references, checkpoints and emitted trace facts.

Generation recipe (in an isolated checkout/archive of the baseline):

1. Copy `pre-latest-frozen.generator.cs.txt` to
   `tests/BlokeBot.Core.Tests/LatestBaselineFixtureGeneration.cs`.
2. Set `BLOKEBOT_BASELINE_FIXTURE_OUTPUT` to an owned absolute SQLite output path.
3. Run `dotnet test --project tests/BlokeBot.Core.Tests --treenode-filter
   '/*/*/AutomationRuntimeTests/GeneratePreLatestFrozenFixture'`.
4. Compress with Python `gzip.compress(database_bytes, mtime=0)`.

Recorded fixture SHA-256:

- uncompressed SQLite: `ad0def9589ad8ff41fd304576b82bddc47fc9af7ca19618ad81f044c435e1c91`
- gzip: `2d63733b45218c3a02a0efd617e4d3817b1ad38d32b84d63a56c8d7a0cba37fe`

Regeneration uses new synthetic GUIDs and event timestamps, so its byte hash will differ. The
behavioral migration/restart test verifies rollback, unchanged historical bytes, idempotent current
conversion, and exactly-once remaining execution after publication. It uses only an owned temporary
database; it never reads or mutates a running Simulation or production installation.
