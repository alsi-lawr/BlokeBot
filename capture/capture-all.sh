#!/usr/bin/env bash
set -euo pipefail

# Regenerates capture definitions. Each definition gets one Simulation for its whole matrix
# instead of one per theme/device/view, and definitions run concurrently up to CAPTURE_JOBS.
# Definitions reuse a Simulation already listening on their port, so running one directly
# still works.

cd "$(dirname "$0")"

VISET_CHECKOUT="${VISET_CHECKOUT:-../../Viset}"
DOTNET="${BLOKEBOT_DOTNET:-dotnet}"
PROJECT="../src/BlokeBot.Simulation/BlokeBot.Simulation.csproj"

default_jobs=$(( $(nproc 2>/dev/null || echo 4) / 4 ))
[[ "$default_jobs" -lt 1 ]] && default_jobs=1
[[ "$default_jobs" -gt 6 ]] && default_jobs=6
JOBS="${CAPTURE_JOBS:-$default_jobs}"

# definition:port. Port 5084 is reserved for human visual signoff.
DEFINITIONS=(
  "dashboard-and-admin.lua:43217"
  "home-scroll.lua:43218"
  "channel-setup-scroll.lua:43224"
  "guessing-workflow.lua:43219"
  "custom-commands.lua:43220"
  "automations.lua:43221"
  "points-and-guessing.lua:43222"
  "native-twitch-operations.lua:43223"
  "viewer-command-catalog.lua:5334"
  "chat-tools-switches.lua:5335"
  "community-guides.lua:5460"
  "overlay-sources.lua:5461"
  "overlay-previews.lua:5462"
  "community-guide-figures.lua:5473"
  "community-progression-figures-laptop.lua:5476"
  "community-progression-figures-phone.lua:5477"
  "configuration-transfer.lua:5478"
)

only="${1:-}"
status_dir="$(mktemp -d)"
trap 'rm -rf "$status_dir"' EXIT

run_definition() {
  local definition="$1" port="$2"
  local base="http://127.0.0.1:${port}"
  local log="/tmp/capture-${definition%.lua}.log"

  "$DOTNET" run --project "$PROJECT" --configuration Release --no-build \
    --no-launch-profile -- --urls "$base" >"/tmp/simulation-${port}.log" 2>&1 &
  local simulation=$!

  local ready=0
  for _ in $(seq 1 120); do
    if curl --silent --fail --max-time 2 "${base}/simulation/started" >/dev/null 2>&1; then
      ready=1
      break
    fi
    if ! kill -0 "$simulation" 2>/dev/null; then
      break
    fi
    sleep 1
  done

  local outcome=0
  if [[ "$ready" -eq 1 ]]; then
    if ! BLOKEBOT_CAPTURE_PORT="$port" nix run "$VISET_CHECKOUT" -- capture "$definition" --force >"$log" 2>&1; then
      outcome=1
    fi
  else
    echo "Simulation never became ready on ${port}" >"$log"
    outcome=1
  fi

  kill "$simulation" 2>/dev/null || true
  wait "$simulation" 2>/dev/null || true

  if [[ "$outcome" -eq 0 ]]; then
    echo "ok" >"${status_dir}/${definition}"
    echo "    done: ${definition} ($(grep -c '^written:' "$log" 2>/dev/null || echo 0) files)"
  else
    echo "failed" >"${status_dir}/${definition}"
    echo "    FAILED: ${definition} (see ${log})"
  fi
}

selected=()
for entry in "${DEFINITIONS[@]}"; do
  [[ -n "$only" && "${entry%%:*}" != "$only" ]] && continue
  selected+=("$entry")
done

if [[ "${#selected[@]}" -eq 0 ]]; then
  echo "No capture definition matched '${only}'."
  exit 1
fi

echo "Running ${#selected[@]} definition(s), ${JOBS} at a time."

running=0
for entry in "${selected[@]}"; do
  definition="${entry%%:*}"
  port="${entry##*:}"
  echo "==> ${definition} on ${port}"
  run_definition "$definition" "$port" &
  running=$((running + 1))
  if [[ "$running" -ge "$JOBS" ]]; then
    wait -n
    running=$((running - 1))
  fi
done
wait

failures=0
for entry in "${selected[@]}"; do
  definition="${entry%%:*}"
  [[ "$(cat "${status_dir}/${definition}" 2>/dev/null)" == "ok" ]] || failures=$((failures + 1))
done

if [[ "$failures" -gt 0 ]]; then
  echo "${failures} definition(s) failed."
  exit 1
fi

echo "All capture definitions completed."
