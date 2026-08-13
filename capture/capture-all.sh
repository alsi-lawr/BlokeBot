#!/usr/bin/env bash
set -euo pipefail

# Runs every capture definition with one Simulation per definition instead of one per
# matrix item. Each definition reuses an already-listening Simulation on its port and
# only starts its own when none is running, so they still work when invoked directly.

cd "$(dirname "$0")"

VISET_CHECKOUT="${VISET_CHECKOUT:-../../Viset}"
DOTNET="${BLOKEBOT_DOTNET:-dotnet}"
PROJECT="../src/BlokeBot.Simulation/BlokeBot.Simulation.csproj"

# definition:port. Port 5084 is reserved for human visual signoff.
DEFINITIONS=(
  "dashboard-and-admin.lua:43217"
  "home-scroll.lua:43218"
  "guessing-workflow.lua:43219"
  "custom-commands.lua:43220"
  "automation-events.lua:43221"
  "points-and-guessing.lua:43222"
  "native-twitch-operations.lua:43223"
  "viewer-command-catalog.lua:5334"
  "chat-tools-switches.lua:5335"
  "community-guides.lua:5460"
  "overlay-sources.lua:5461"
  "overlay-previews.lua:5462"
  "v010-guide-figures-phone.lua:5473"
  "v010-guide-figures-laptop.lua:5475"
  "community-progression-figures-laptop.lua:5476"
  "community-progression-figures-phone.lua:5477"
)

only="${1:-}"
failures=0

for entry in "${DEFINITIONS[@]}"; do
  definition="${entry%%:*}"
  port="${entry##*:}"

  if [[ -n "$only" && "$definition" != "$only" ]]; then
    continue
  fi

  base="http://127.0.0.1:${port}"
  echo "==> ${definition} on ${port}"

  "$DOTNET" run --project "$PROJECT" --configuration Release --no-build \
    --no-launch-profile -- --urls "$base" >"/tmp/simulation-${port}.log" 2>&1 &
  simulation=$!

  ready=0
  for _ in $(seq 1 90); do
    if curl --silent --fail --max-time 2 "${base}/simulation/ready" >/dev/null 2>&1; then
      ready=1
      break
    fi
    sleep 1
  done

  if [[ "$ready" -eq 1 ]]; then
    if ! nix run "$VISET_CHECKOUT" -- capture "$definition" --force; then
      echo "    FAILED: ${definition}"
      failures=$((failures + 1))
    fi
  else
    echo "    FAILED: Simulation never became ready on ${port}"
    failures=$((failures + 1))
  fi

  kill "$simulation" 2>/dev/null || true
  wait "$simulation" 2>/dev/null || true
done

if [[ "$failures" -gt 0 ]]; then
  echo "${failures} definition(s) failed."
  exit 1
fi

echo "All capture definitions completed."
