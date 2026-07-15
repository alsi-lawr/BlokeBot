#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
requested_target="$repo_root/assets/simulation"
requested_matrix="$repo_root/scripts/simulation-capture-matrix.json"

usage() {
  printf 'usage: %s [TARGET] [--matrix MATRIX]\n' "${0##*/}"
}

if [[ "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi
if (($#)) && [[ "$1" != "--matrix" ]]; then
  requested_target="$1"
  shift
fi
if (($#)); then
  if [[ "$1" != "--matrix" || "$#" -ne 2 ]]; then
    usage >&2
    exit 2
  fi
  requested_matrix="$2"
fi

if [[ "$requested_target" != /* ]]; then
  requested_target="$repo_root/$requested_target"
fi
target="$(realpath -m "$requested_target")"
target_marker="$target/.blokebot-simulation-output"
if [[ "$requested_matrix" != /* ]]; then
  requested_matrix="$repo_root/$requested_matrix"
fi
matrix="$(realpath -m "$requested_matrix")"

if [[ -e "$target" && ! -d "$target" ]]; then
  printf 'Simulation output target is not a directory: %s\n' "$target" >&2
  exit 2
fi
if [[ -d "$target" && ! -f "$target_marker" ]] \
  && [[ -n "$(find "$target" -mindepth 1 -maxdepth 1 -print -quit)" ]]; then
  printf 'Simulation output target is not empty or owned by this workflow: %s\n' "$target" >&2
  exit 2
fi

browser="${BLOKEBOT_SIMULATION_BROWSER:-chromium}"
port="${BLOKEBOT_SIMULATION_PORT:-43217}"
base_url="http://127.0.0.1:$port"
runtime="$target/runtime"
raw="$target/raw"
output="$target/output"
animation_frames="$target/animation-frames"
animations="$target/animations"
server_log="$target/blokebot.log"
browser_log="$target/chromium.log"
browser_profile="$runtime/chromium-profile"
frame_template="$repo_root/scripts/simulation-frame.html"
animation_script="$repo_root/scripts/capture-simulation-animations.mjs"
matrix_script="$repo_root/scripts/simulation-capture-matrix.mjs"
server_pid=""

[[ -f "$matrix" ]] || {
  printf 'Capture matrix not found: %s\n' "$matrix" >&2
  exit 2
}
for required_command in "$browser" magick img2webp webpmux node; do
  command -v "$required_command" >/dev/null || {
    printf 'Required command not found: %s\n' "$required_command" >&2
    printf 'Run this command from nix develop .#simulation.\n' >&2
    exit 2
  }
done

screenshot_rows="$(node "$matrix_script" screenshots "$matrix")"
animation_rows="$(node "$matrix_script" animations "$matrix")"
mapfile -t screenshot_matrix <<<"$screenshot_rows"
mapfile -t animation_matrix <<<"$animation_rows"

cleanup_server() {
  if [[ -z "$server_pid" ]] || ! kill -0 "$server_pid" 2>/dev/null; then
    server_pid=""
    return 0
  fi

  kill -TERM "$server_pid"
  wait "$server_pid"
  server_pid=""
}

cleanup_on_exit() {
  if [[ -n "$server_pid" ]] && kill -0 "$server_pid" 2>/dev/null; then
    kill -TERM "$server_pid" 2>/dev/null || true
    wait "$server_pid" 2>/dev/null || true
  fi
}
trap cleanup_on_exit EXIT

mkdir -p "$target"
touch "$target_marker"
rm -rf -- "$runtime" "$raw" "$output" "$animation_frames" "$animations"
mkdir -p "$runtime/home" "$browser_profile" "$raw" "$output" "$animation_frames" "$animations"
: >"$server_log"
: >"$browser_log"

cd "$repo_root"
dotnet restore BlokeBot.slnx --disable-parallel
dotnet build BlokeBot.slnx --no-restore --disable-parallel -warnaserror

export ASPNETCORE_ENVIRONMENT=Simulation
export DOTNET_ENVIRONMENT=Simulation
export ASPNETCORE_URLS="$base_url"
export BlokeBot__DatabasePath="$runtime/blokebot.db"
export TwitchBot__Identity__BotUsername=""
export TwitchBot__Identity__ClientId=""
export TwitchBot__Identity__ClientSecret=""
export TwitchBot__Identity__TokenCachePath="$runtime/twitch.tokens.json"
export HOME="$runtime/home"
export TZ=UTC

dotnet run \
  --project src/BlokeBot/BlokeBot.csproj \
  --configuration Debug \
  --no-build \
  --no-launch-profile \
  >"$server_log" 2>&1 &
server_pid=$!

ready=false
for _ in {1..120}; do
  if curl --fail --silent --show-error "$base_url/simulation/ready" >/dev/null 2>&1; then
    ready=true
    break
  fi
  if ! kill -0 "$server_pid" 2>/dev/null; then
    cat "$server_log" >&2
    exit 1
  fi
  sleep 0.25
done
if [[ "$ready" != true ]]; then
  cat "$server_log" >&2
  printf 'BlokeBot did not become ready at %s\n' "$base_url" >&2
  exit 1
fi

chromium_flags=(
  --headless=new
  --disable-background-networking
  --disable-background-mode
  --disable-component-update
  --disable-default-apps
  --disable-sync
  --force-device-scale-factor=1
  --force-prefers-reduced-motion
  --host-resolver-rules="MAP * 0.0.0.0, EXCLUDE 127.0.0.1"
  --hide-scrollbars
  --metrics-recording-only
  --no-first-run
  --no-sandbox
  --password-store=basic
  --use-mock-keychain
)

"$browser" \
  "${chromium_flags[@]}" \
  --user-data-dir="$browser_profile/login" \
  --virtual-time-budget=3000 \
  --dump-dom \
  "$base_url/simulation/login" \
  >"$target/login.html" 2>>"$browser_log"

if ! grep -q "Sample Channel" "$target/login.html"; then
  cat "$server_log" >&2
  printf 'Simulation sign-in did not reach the seeded channel.\n' >&2
  exit 1
fi

capture_screenshot() {
  local name device theme view viewport_width viewport_height frame_width frame_height
  IFS=$'\t' read -r \
    name device theme view viewport_width viewport_height frame_width frame_height <<<"$1"

  local raw_image="$raw/$name.png"
  local capture_profile="$browser_profile/raw-$name"
  "$browser" \
    "${chromium_flags[@]}" \
    --user-data-dir="$capture_profile" \
    --virtual-time-budget=4000 \
    --window-size="$viewport_width,$viewport_height" \
    --screenshot="$raw_image" \
    "$base_url/simulation/login?view=$view&theme=$theme" \
    >>"$browser_log" 2>&1
  test -s "$raw_image"

  local screenshot="$output/$name.png"
  capture_profile="$browser_profile/frame-$name"
  local frame_url="file://$frame_template?device=$device&image=file://$raw_image"
  "$browser" \
    "${chromium_flags[@]}" \
    --allow-file-access-from-files \
    --default-background-color=00000000 \
    --user-data-dir="$capture_profile" \
    --virtual-time-budget=1000 \
    --window-size="$frame_width,$frame_height" \
    --screenshot="$screenshot" \
    "$frame_url" \
    >>"$browser_log" 2>&1
  test -s "$screenshot"
}

for screenshot_case in "${screenshot_matrix[@]}"; do
  capture_screenshot "$screenshot_case"
done

expected_count=${#screenshot_matrix[@]}
raw_count=$(find "$raw" -maxdepth 1 -type f -name '*.png' -size +0c | wc -l)
actual_count=$(find "$output" -maxdepth 1 -type f -name '*.png' -size +0c | wc -l)
if [[ "$raw_count" -ne "$expected_count" || "$actual_count" -ne "$expected_count" ]]; then
  printf 'Expected %d raw and framed screenshots, found %d and %d.\n' \
    "$expected_count" "$raw_count" "$actual_count" >&2
  exit 1
fi

unique_count=$(sha256sum "$output"/*.png | cut -d' ' -f1 | sort -u | wc -l)
if [[ "$unique_count" -ne "$expected_count" ]]; then
  printf 'Expected every framed route capture to be distinct.\n' >&2
  exit 1
fi

for screenshot in "$output"/*.png; do
  channels=$(magick identify -format '%[channels]' "$screenshot")
  corner_alpha=$(
    magick "$screenshot" \
      -format '%[fx:p{0,0}.a],%[fx:p{w-1,0}.a],%[fx:p{0,h-1}.a],%[fx:p{w-1,h-1}.a]' \
      info:
  )
  if [[ "$channels" != *a* || "$corner_alpha" != "0,0,0,0" ]]; then
    printf 'Expected transparent RGBA corners in %s, found channels=%s alpha=%s.\n' \
      "$screenshot" "$channels" "$corner_alpha" >&2
    exit 1
  fi
done

node "$animation_script" \
  --browser "$browser" \
  --base-url "$base_url" \
  --matrix "$matrix" \
  --frame-template "$frame_template" \
  --frames "$animation_frames" \
  --output "$animations" \
  --profile "$browser_profile/animations" \
  --browser-log "$browser_log"

expected_animation_count=${#animation_matrix[@]}
animation_count=$(find "$animations" -maxdepth 1 -type f -name '*.webp' -size +0c | wc -l)
if [[ "$animation_count" -ne "$expected_animation_count" ]]; then
  printf 'Expected %d WebP animations, found %d.\n' \
    "$expected_animation_count" "$animation_count" >&2
  exit 1
fi

animation_fps_count=$(grep -c '"framesPerSecond": 30' "$animations/manifest.json")
if [[ "$animation_fps_count" -ne "$expected_animation_count" ]]; then
  printf 'Expected every WebP manifest entry to declare 30 frames per second.\n' >&2
  exit 1
fi

for animation in "$animations"/*.webp; do
  frame_count=$(magick identify "$animation" | wc -l)
  channels=$(magick identify -format '%[channels]' "${animation}[0]")
  webp_info=$(webpmux -info "$animation")
  corner_alpha=$(
    magick "${animation}[0]" \
      -format '%[fx:p{0,0}.a],%[fx:p{w-1,0}.a],%[fx:p{0,h-1}.a],%[fx:p{w-1,h-1}.a]' \
      info:
  )
  if [[ "$frame_count" -le 1 \
    || "$channels" != *a* \
    || "$corner_alpha" != "0,0,0,0" \
    || "$webp_info" != *"Background color : 0x00000000"* ]]; then
    printf 'Expected an animated WebP with transparent corners in %s.\n' "$animation" >&2
    exit 1
  fi
done

if grep -Eq '^(fail|crit):' "$server_log"; then
  cat "$server_log" >&2
  printf 'BlokeBot reported a failure or critical error during capture.\n' >&2
  exit 1
fi

(
  cd "$output"
  sha256sum ./*.png >SHA256SUMS
)
(
  cd "$animations"
  sha256sum ./*.webp >SHA256SUMS
)

cleanup_server
trap - EXIT

printf 'Captured %d framed screenshots in %s\n' "$actual_count" "$output"
printf 'Captured %d framed animations in %s\n' "$animation_count" "$animations"
