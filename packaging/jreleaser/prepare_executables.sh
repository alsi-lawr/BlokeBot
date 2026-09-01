#!/usr/bin/env bash
set -euo pipefail

publish_directory="${1:-}"
test -d "$publish_directory" || {
  echo "Publish directory does not exist: $publish_directory" >&2
  exit 2
}

for rid in linux-x64 linux-arm64 osx-arm64; do
  worker="$publish_directory/$rid/plugin-worker/BlokeBot.PluginWorker"
  test -f "$worker" || {
    echo "Plugin worker does not exist: $worker" >&2
    exit 2
  }
  chmod 0755 "$worker"
done
