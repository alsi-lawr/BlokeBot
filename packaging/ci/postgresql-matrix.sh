#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${BLOKEBOT_POSTGRESQL_CONNECTION_STRING_FILE:-}" ]]; then
  echo "Set BLOKEBOT_POSTGRESQL_CONNECTION_STRING_FILE." >&2
  exit 2
fi

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
scratch="${RUNNER_TEMP:-/tmp}/blokebot-postgresql-matrix"
state="$scratch/state"
log="$scratch/blokebot.log"
protocol="$root/tools/BlokeBot.DatabaseWorkloads/protocol/blokebot-database-workloads-v1.json"
digest="$root/tools/BlokeBot.DatabaseWorkloads/protocol/blokebot-database-workloads-v1.json.sha256"
sqlite_result="$scratch/sqlite-workload.json"
postgresql_result="$scratch/postgresql-workload.json"
surface="$scratch/blokebot-surface.html"
live="$scratch/blokebot-live.json"
readiness="$scratch/blokebot-readiness.json"
host_pid=""

mkdir -p "$state"
chmod 0700 "$scratch" "$state"
sqlite_scratch="$(mktemp -d "$scratch/sqlite.XXXXXX")"
sqlite_database="$sqlite_scratch/blokebot.db"

stop_host() {
  if [[ -n "$host_pid" ]] && kill -0 "$host_pid" 2>/dev/null; then
    kill -TERM "$host_pid"
    wait "$host_pid" || true
  fi
  host_pid=""
}

cleanup() {
  stop_host
  rm -rf -- "$sqlite_scratch"
}
trap cleanup EXIT

start_and_check_host() {
  : >"$log"
  BlokeBot__DatabaseProvider=PostgreSql \
    BlokeBot__StateDirectory="$state" \
    BlokeBot__PostgreSqlConnectionStringFile="$BLOKEBOT_POSTGRESQL_CONNECTION_STRING_FILE" \
    dotnet run \
      --project "$root/src/BlokeBot/BlokeBot.csproj" \
      --configuration Release \
      --no-build \
      --no-launch-profile \
      -- serve --host 127.0.0.1 --port 18080 --data-dir "$state" \
      >"$log" 2>&1 &
  host_pid=$!

  status=""
  for _ in $(seq 1 90); do
    if ! kill -0 "$host_pid" 2>/dev/null; then
      cat "$log" >&2
      return 1
    fi
    status="$(
      curl \
        --silent \
        --output "$surface" \
        --write-out '%{http_code}' \
        http://127.0.0.1:18080/auth/login 2>/dev/null || true
    )"
    if [[ "$status" == "503" ]]; then
      break
    fi
    sleep 1
  done
  if [[ "$status" != "503" ]]; then
    cat "$log" >&2
    return 1
  fi

  grep --fixed-strings "Twitch connection unavailable" "$surface" >/dev/null
  grep --fixed-strings "This Twitch connection is not available yet." "$surface" >/dev/null

  live_status="$(
    curl \
      --silent \
      --output "$live" \
      --write-out '%{http_code}' \
      http://127.0.0.1:18080/health/live
  )"
  readiness_status="$(
    curl \
      --silent \
      --output "$readiness" \
      --write-out '%{http_code}' \
      http://127.0.0.1:18080/health/ready
  )"
  [[ "$live_status" == "200" ]]
  [[ "$readiness_status" == "200" ]]
  LIVE="$live" READINESS="$readiness" python - <<'PY'
import json
import os
from pathlib import Path

live = json.loads(Path(os.environ["LIVE"]).read_text())
readiness = json.loads(Path(os.environ["READINESS"]).read_text())
assert live == {"status": "live"}
assert readiness == {
    "status": "ready",
    "database": {"provider": "PostgreSql", "category": "ready"},
}
PY

  if grep --fixed-strings --file "$BLOKEBOT_POSTGRESQL_CONNECTION_STRING_FILE" "$log"; then
    echo "The connection string appeared in the host log." >&2
    return 1
  fi
  stop_host
}

dotnet run \
  --project "$root/tools/BlokeBot.DatabaseWorkloads/BlokeBot.DatabaseWorkloads.csproj" \
  --configuration Release \
  --no-build \
  -- verify-protocol --protocol "$protocol" --digest "$digest"

dotnet run \
  --project "$root/tools/BlokeBot.DatabaseWorkloads/BlokeBot.DatabaseWorkloads.csproj" \
  --configuration Release \
  --no-build \
  -- verify-inventory \
  --repo-root "$root" \
  --inventory "$root/docs/database-providers/main-database-raw-sql-v1.json"

dotnet run \
  --project "$root/tools/BlokeBot.DatabaseWorkloads/BlokeBot.DatabaseWorkloads.csproj" \
  --configuration Release \
  --no-build \
  -- run-sqlite \
  --protocol "$protocol" \
  --digest "$digest" \
  --database "$sqlite_database" \
  --output "$sqlite_result"

dotnet run \
  --project "$root/tools/BlokeBot.DatabaseWorkloads/BlokeBot.DatabaseWorkloads.csproj" \
  --configuration Release \
  --no-build \
  -- run-postgresql \
  --protocol "$protocol" \
  --digest "$digest" \
  --connection-string-file "$BLOKEBOT_POSTGRESQL_CONNECTION_STRING_FILE" \
  --output "$postgresql_result"

SQLITE_RESULT="$sqlite_result" \
POSTGRESQL_RESULT="$postgresql_result" \
PROTOCOL="$protocol" \
DIGEST="$digest" \
  python - <<'PY'
import json
import os
from pathlib import Path

protocol = json.loads(Path(os.environ["PROTOCOL"]).read_text())
digest = Path(os.environ["DIGEST"]).read_text().strip()
results = [
    json.loads(Path(os.environ["SQLITE_RESULT"]).read_text()),
    json.loads(Path(os.environ["POSTGRESQL_RESULT"]).read_text()),
]

for result in results:
    assert result["schema_version"] == protocol["schema_version"]
    assert result["protocol_id"] == protocol["protocol_id"]
    assert result["source_commit"] == protocol["source_commit"]
    assert result["protocol_sha256"] == digest
    assert result["redacted"] is True

assert results[0]["logical_outcomes"] == results[1]["logical_outcomes"]
print(
    f"{protocol['protocol_id']} {digest} "
    f"logical_outcomes={len(results[0]['logical_outcomes'])}"
)
PY

dotnet run \
  --project "$root/tools/BlokeBot.DatabaseWorkloads/BlokeBot.DatabaseWorkloads.csproj" \
  --configuration Release \
  --no-build \
  -- verify-postgresql-authorities \
  --connection-string-file "$BLOKEBOT_POSTGRESQL_CONNECTION_STRING_FILE"

start_and_check_host
start_and_check_host

BLOKEBOT_RUN_DATABASE_CUTOVER_INTEGRATION=1 \
  dotnet test "$root/tests/BlokeBot.Tests/BlokeBot.Tests.csproj" \
    --configuration Release \
    --no-build \
    -- \
    --treenode-filter '/*/*/*/Cutover_PreparesTargetAndResumesAcrossInjectedFailures' \
    --no-ansi \
    --no-progress \
    --output Normal
