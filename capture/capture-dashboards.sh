#!/usr/bin/env bash
set -euo pipefail
repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
viset=${VISET_CHECKOUT:-"$repo/../Viset"}
browser=${BLOKEBOT_CAPTURE_BROWSER:-$(command -v google-chrome)}
work_root=${BLOKEBOT_CAPTURE_WORK_ROOT:-"$repo/.agent-workspace"}
mkdir -p "$work_root"
work=$(mktemp -d "$work_root/dashboard-video.XXXXXXXX")
port=$(python3 - <<'PY'
import socket
with socket.socket() as s:
    s.bind(('127.0.0.1', 0))
    print(s.getsockname()[1])
PY
)
# Never reuse an existing service or a human preview. The selected port must still be free.
python3 - "$port" <<'PY'
import socket, sys
port = int(sys.argv[1])
assert port not in range(5084, 5089)
with socket.socket() as s:
    s.bind(('127.0.0.1', port))
PY
pid=
cleanup() {
  if [[ -n "$pid" ]]; then kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi
  echo "Capture logs and raw recordings: $work"
}
trap cleanup EXIT
{
  printf 'source=%s\nviset=%s\nport=%s\n' "$(git -C "$repo" rev-parse HEAD)" "$(git -C "$viset" rev-parse HEAD)" "$port"
  git -C "$repo" diff -- capture/dashboards.lua capture/capture-dashboards.sh
  sha256sum "$repo/capture/dashboards.lua" "$repo/capture/capture-dashboards.sh"
  git -C "$viset" status --short
  git -C "$viset" diff HEAD
  "$browser" --version
  ffmpeg -version | head -1
} > "$work/provenance.txt"
BlokeBot__StateDirectory="$work/state" TZ=UTC \
  dotnet "$repo/src/BlokeBot.Simulation/bin/Release/net10.0/BlokeBot.Simulation.dll" \
  --urls "http://127.0.0.1:$port" > "$work/simulation.log" 2>&1 &
pid=$!
BLOKEBOT_CAPTURE_PORT="$port" nix run "$viset" -- capture "$repo/capture/dashboards.lua" \
  --browser "$browser" --output "$work/raw" --force 2>&1 | tee "$work/viset.log"
output="$repo/src/BlokeBot.Site/wwwroot/media/dashboards"
mkdir -p "$output"
for device in laptop phone; do
  for theme in light dark; do
    for operation in editor control; do
      test -s "$work/raw/$device-$theme-$operation.webp"
    done
  done
done
for raw in "$work"/raw/*.webp; do
  name=$(basename "$raw" .webp)
  frames=$(ffprobe -v error -select_streams v:0 -count_frames \
    -show_entries stream=nb_read_frames -of csv=p=0 "$raw")
  if [[ ! "$frames" =~ ^[0-9]+$ ]] || (( frames < 2 )); then
    echo "Not a genuine motion recording: $raw ($frames frames)" >&2
    exit 1
  fi
  ffmpeg -hide_banner -y -i "$raw" -map 0:v:0 -an \
    -vf 'pad=ceil(iw/2)*2:ceil(ih/2)*2' -c:v libvpx-vp9 -pix_fmt yuv420p \
    -crf 30 -b:v 0 "$output/$name.webm" > "$work/$name-encode.log" 2>&1
  ffmpeg -hide_banner -y -i "$output/$name.webm" -frames:v 1 \
    "$output/$name.png" > "$work/$name-still.log" 2>&1
  ffmpeg -v error -i "$output/$name.webm" -f null -
  ffprobe -v error -show_streams -show_format -of json "$output/$name.webm" > "$work/$name-probe.json"
done
printf 'Generated %s WebM clips. Review motion and guide playback before committing.\n' "$(find "$output" -name '*.webm' | wc -l)"
