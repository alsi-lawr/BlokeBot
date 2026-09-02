# PostgreSQL 18 compatibility verification: 2026-09-02

This verification used the controlled `postgres:18-alpine` major tag. Docker resolved the tag to
manifest digest `sha256:d3e1620b530c944afa6e887d22eb899824da68e19c52024bf98f5220c88a65b2`.
The server reported PostgreSQL 18.6.

The command `packaging/ci/postgresql-matrix.sh` passed against the disposable server. It verified:

- the frozen `blokebot-database-workloads-v1` protocol with SHA-256
  `b1901eb7d00a5a2c08650fd6619def775712f93db31c479559548414e4a180da`;
- all 45 reviewed SQL inventory entries;
- equal SQLite and PostgreSQL results for all nine logical outcomes;
- the PostgreSQL transaction and write authorities;
- fresh migration startup and a second startup with the current migration history;
- redacted live and ready health responses; and
- the SQLite cutover journey, pending-work delivery, and no replay.

The PostgreSQL 18 logical results were 109 automation receipts and completed runs, 120 expired
public-chat messages, 80 completed configuration activations, and 191 points and community rows.
The total point balance was 499,691. The total community progress was 49,691. The plugin revision
was 41.

The Docker Compose smoke check built the BlokeBot image, started both services, and returned
`ready` for PostgreSQL. PostgreSQL reported data directory `/var/lib/postgresql/18/docker` through
the volume mounted at `/var/lib/postgresql`. The Compose logs did not contain the disposable
database password.

The Nix packages for BlokeBot and the Site built. The PostgreSQL NixOS module example evaluated
with `pkgs.postgresql_18`, the protected systemd credential, and the `postgresql.target` dependency.
The Nix BlokeBot package returned `ready` against PostgreSQL 18.6. The Nix Site package served the
database installation and cutover procedures.

This compatibility run does not replace the PostgreSQL 17.11 timing comparison from 2026-09-01.
It makes no PostgreSQL 18 performance claim and does not authorize a production cutover.
