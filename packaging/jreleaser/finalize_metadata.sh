#!/usr/bin/env bash
set -euo pipefail

repository_url=https://github.com/alsi-lawr/BlokeBot
package_identifier=alsi-lawr.BlokeBot
version=
release_directory=
metadata_directory=

while [ "$#" -gt 0 ]; do
  case "$1" in
    --version)
      version="${2:-}"
      shift 2
      ;;
    --release-directory)
      release_directory="${2:-}"
      shift 2
      ;;
    --metadata-directory)
      metadata_directory="${2:-}"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ ! "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
  echo "Version must be strict X.Y.Z: $version" >&2
  exit 2
fi
test -d "$release_directory" || { echo "Release directory does not exist: $release_directory" >&2; exit 2; }
test -d "$metadata_directory" || { echo "Metadata directory does not exist: $metadata_directory" >&2; exit 2; }

publish_scoop="${PUBLISH_SCOOP:-true}"
publish_winget="${PUBLISH_WINGET:-true}"
for enabled in "$publish_scoop" "$publish_winget"; do
  [[ "$enabled" == true || "$enabled" == false ]] || {
    echo "PUBLISH_SCOOP and PUBLISH_WINGET must be true or false." >&2
    exit 2
  }
done

declare -A architecture=(
  [win-x64]=x64
  [win-arm64]=arm64
)
declare -A scoop_architecture=(
  [win-x64]=64bit
  [win-arm64]=arm64
)
declare -A archive_root archive_hash archive_url

for rid in win-x64 win-arm64; do
  root="blokebot-v${version}-${rid}"
  path="$release_directory/${root}.zip"
  expected_entry="$root/bin/blokebot.exe"
  test -f "$path" || { echo "Required Windows archive does not exist: $path" >&2; exit 2; }

  if ! unzip -Z1 "$path" | grep -Fxq "$expected_entry"; then
    echo "$path does not contain $expected_entry" >&2
    exit 2
  fi
  while IFS= read -r entry; do
    case "$entry" in
      /* | ../* | */../* | */..)
        echo "$path contains an unsafe entry: $entry" >&2
        exit 2
        ;;
    esac
    case "$entry" in
      "$root" | "$root"/*) ;;
      *)
        echo "$path contains an entry outside $root: $entry" >&2
        exit 2
        ;;
    esac
  done < <(unzip -Z1 "$path")

  archive_root[$rid]="$root"
  archive_hash[$rid]="$(sha256sum "$path" | cut -d ' ' -f 1)"
  archive_url[$rid]="$repository_url/releases/download/v$version/${root}.zip"
done

if [ "$publish_scoop" = true ]; then
  scoop_manifest="$metadata_directory/blokebot/scoop/bucket/blokebot.json"
  test -f "$scoop_manifest" || { echo "Generated Scoop manifest does not exist: $scoop_manifest" >&2; exit 2; }
  grep -Eq "\"version\"[[:space:]]*:[[:space:]]*\"$version\"" "$scoop_manifest" || {
    echo "Generated Scoop manifest has an unexpected version: $scoop_manifest" >&2
    exit 2
  }

  temporary_manifest="$scoop_manifest.tmp"
  cat >"$temporary_manifest" <<EOF
{
    "version": "$version",
    "description": "Free, open-source Twitch bot and dashboard",
    "homepage": "$repository_url",
    "license": "MIT",
    "architecture": {
        "${scoop_architecture[win-x64]}": {
            "url": "${archive_url[win-x64]}",
            "hash": "sha256:${archive_hash[win-x64]}",
            "extract_dir": "${archive_root[win-x64]}"
        },
        "${scoop_architecture[win-arm64]}": {
            "url": "${archive_url[win-arm64]}",
            "hash": "sha256:${archive_hash[win-arm64]}",
            "extract_dir": "${archive_root[win-arm64]}"
        }
    },
    "bin": [
        ["bin/blokebot.exe", "blokebot"]
    ]
}
EOF
  mv "$temporary_manifest" "$scoop_manifest"
fi

if [ "$publish_winget" = true ]; then
  winget_directory="$metadata_directory/blokebot/winget/manifests/a/alsi-lawr/BlokeBot/$version"
  installer_manifest="$winget_directory/$package_identifier.installer.yaml"
  version_manifest="$winget_directory/$package_identifier.yaml"
  locale_manifest="$winget_directory/$package_identifier.locale.en-US.yaml"

  for manifest in "$installer_manifest" "$version_manifest" "$locale_manifest"; do
    test -f "$manifest" || { echo "Generated WinGet manifest does not exist: $manifest" >&2; exit 2; }
    grep -Fxq "PackageIdentifier: $package_identifier" "$manifest" || {
      echo "Generated WinGet manifest has an unexpected identifier: $manifest" >&2
      exit 2
    }
    grep -Fxq "PackageVersion: $version" "$manifest" || {
      echo "Generated WinGet manifest has an unexpected version: $manifest" >&2
      exit 2
    }
  done
  grep -Fxq 'ManifestType: version' "$version_manifest"
  grep -Fxq 'ManifestType: defaultLocale' "$locale_manifest"

  temporary_manifest="$installer_manifest.tmp"
  cat >"$temporary_manifest" <<EOF
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.installer.1.9.0.schema.json

PackageIdentifier: $package_identifier
PackageVersion: $version
Installers:
EOF
  for rid in win-x64 win-arm64; do
    cat >>"$temporary_manifest" <<EOF
  - Architecture: ${architecture[$rid]}
    InstallerUrl: ${archive_url[$rid]}
    InstallerSha256: ${archive_hash[$rid]}
    InstallerType: zip
    NestedInstallerType: portable
    ArchiveBinariesDependOnPath: true
    NestedInstallerFiles:
      - RelativeFilePath: ${archive_root[$rid]}\\bin\\blokebot.exe
        PortableCommandAlias: blokebot
EOF
  done
  cat >>"$temporary_manifest" <<'EOF'
ManifestType: installer
ManifestVersion: 1.9.0
EOF
  mv "$temporary_manifest" "$installer_manifest"
fi
