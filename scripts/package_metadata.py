#!/usr/bin/env python3

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys
import tempfile
import tomllib
import xml.etree.ElementTree as ET
import zipfile

import release_artifacts


VERSION = release_artifacts.VERSION
RELEASE_TAG = release_artifacts.RELEASE_TAG
REPOSITORY = "alsi-lawr/BlokeBot"
RELEASE_BASE_URL = f"https://github.com/{REPOSITORY}/releases/download/{RELEASE_TAG}"
WINGET_IDENTIFIER = "alsi-lawr.BlokeBot"
ZIP_TIMESTAMP = release_artifacts.ZIP_TIMESTAMP

CHANNEL_FILES = (
    "STATUS.md",
    "release-assets/checksums.toml",
    "homebrew/Formula/blokebot.rb",
    "scoop/blokebot.json",
    "chocolatey/blokebot.nuspec",
    "chocolatey/tools/chocolateyinstall.ps1",
    "chocolatey/tools/chocolateyuninstall.ps1",
    "winget/README.md",
    "winget/manifests/a/alsi-lawr/BlokeBot/0.1.0/alsi-lawr.BlokeBot.yaml",
    "winget/manifests/a/alsi-lawr/BlokeBot/0.1.0/alsi-lawr.BlokeBot.installer.yaml",
    "winget/manifests/a/alsi-lawr/BlokeBot/0.1.0/alsi-lawr.BlokeBot.locale.en-US.yaml",
    "winget/winget-pr-v0.1.0.zip",
    "jreleaser/README.md",
    "jreleaser/jreleaser.yml",
)


class PackageMetadataError(RuntimeError):
    pass


def _archive_url(name: str) -> str:
    return f"{RELEASE_BASE_URL}/{name}"


def _load_artifacts(release_directory: Path, checksums_path: Path) -> dict[str, dict[str, object]]:
    try:
        release_artifacts.verify_checksums(release_directory, checksums_path)
    except release_artifacts.ReleaseArtifactError as error:
        raise PackageMetadataError(str(error)) from error

    with checksums_path.open("rb") as source:
        manifest = tomllib.load(source)

    artifacts = manifest.get("artifact")
    if not isinstance(artifacts, list):
        raise PackageMetadataError("checksums.toml has no artifact entries")

    by_rid: dict[str, dict[str, object]] = {}
    for artifact in artifacts:
        if not isinstance(artifact, dict) or not isinstance(artifact.get("rid"), str):
            raise PackageMetadataError("checksums.toml contains an invalid artifact entry")
        rid = artifact["rid"]
        if rid in by_rid:
            raise PackageMetadataError(f"checksums.toml contains duplicate RID: {rid}")
        by_rid[rid] = artifact

    expected_rids = {rid for rid, _ in release_artifacts.RID_FORMATS}
    if set(by_rid) != expected_rids:
        raise PackageMetadataError("checksums.toml does not describe the exact release RID set")
    return by_rid


def _write_text(root: Path, relative: str, content: str) -> None:
    path = root / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    try:
        with path.open("x", encoding="utf-8", newline="\n") as output:
            output.write(content.rstrip() + "\n")
    except FileExistsError as error:
        raise PackageMetadataError(f"Refusing to overwrite package metadata: {path}") from error


def _artifact(artifacts: dict[str, dict[str, object]], rid: str) -> tuple[str, str]:
    artifact = artifacts[rid]
    name = artifact.get("name")
    digest = artifact.get("sha256")
    if not isinstance(name, str) or not isinstance(digest, str):
        raise PackageMetadataError(f"checksums.toml has incomplete metadata for {rid}")
    return name, digest


def _homebrew_formula(artifacts: dict[str, dict[str, object]]) -> str:
    name, digest = _artifact(artifacts, "osx-arm64")
    return f'''class Blokebot < Formula
  desc "Free, open-source Twitch bot and dashboard"
  homepage "https://github.com/{REPOSITORY}"
  url "{_archive_url(name)}"
  sha256 "{digest}"
  license "MIT"
  version "{VERSION}"

  depends_on arch: :arm64

  def install
    libexec.install Dir["blokebot/*"]
    bin.install_symlink libexec/"blokebot"
  end

  def caveats
    <<~EOS
      BlokeBot does not start automatically. Run `blokebot help` before serving.
      Persistent state belongs in the platform data directory or an explicit --data-dir,
      never in Homebrew's installation directory.
    EOS
  end

  test do
    assert_match "blokebot {VERSION}", shell_output("#{{bin}}/blokebot version")
  end
end'''


def _scoop_manifest(artifacts: dict[str, dict[str, object]]) -> str:
    x64_name, x64_digest = _artifact(artifacts, "win-x64")
    arm64_name, arm64_digest = _artifact(artifacts, "win-arm64")
    manifest = {
        "version": VERSION,
        "description": "Free, open-source Twitch bot and dashboard",
        "homepage": f"https://github.com/{REPOSITORY}",
        "license": "MIT",
        "architecture": {
            "64bit": {"url": _archive_url(x64_name), "hash": x64_digest},
            "arm64": {"url": _archive_url(arm64_name), "hash": arm64_digest},
        },
        "extract_dir": "blokebot",
        "bin": [["blokebot.exe", "blokebot"]],
        "notes": [
            "BlokeBot does not start automatically.",
            "Run 'blokebot help' before serving.",
            "Persistent state stays in the platform data directory or an explicit --data-dir.",
        ],
    }
    return json.dumps(manifest, indent=2, sort_keys=True) + "\n"


def _chocolatey_nuspec() -> str:
    return f'''<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd">
  <metadata>
    <id>blokebot</id>
    <version>{VERSION}</version>
    <title>BlokeBot</title>
    <authors>BlokeBot contributors</authors>
    <owners>alsi-lawr</owners>
    <projectUrl>https://github.com/{REPOSITORY}</projectUrl>
    <licenseUrl>https://github.com/{REPOSITORY}/blob/{RELEASE_TAG}/LICENSE</licenseUrl>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <description>Free, open-source Twitch bot and dashboard.</description>
    <summary>Lowercase BlokeBot command-line application.</summary>
    <releaseNotes>Run `blokebot help` before serving. BlokeBot does not start automatically and stores no persistent state in the package installation directory.</releaseNotes>
    <tags>blokebot twitch bot cli</tags>
  </metadata>
  <files>
    <file src="tools\\**" target="tools" />
  </files>
</package>'''


def _chocolatey_install(artifacts: dict[str, dict[str, object]]) -> str:
    x64_name, x64_digest = _artifact(artifacts, "win-x64")
    arm64_name, arm64_digest = _artifact(artifacts, "win-arm64")
    return f'''$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
switch ($architecture) {{
    'X64' {{
        $url = '{_archive_url(x64_name)}'
        $checksum = '{x64_digest}'
    }}
    'Arm64' {{
        $url = '{_archive_url(arm64_name)}'
        $checksum = '{arm64_digest}'
    }}
    default {{ throw "BlokeBot {VERSION} supports Windows x64 and ARM64 only; received $architecture." }}
}}

$installDirectory = Join-Path $toolsDir 'install'
Install-ChocolateyZipPackage `
    -PackageName 'blokebot' `
    -Url $url `
    -UnzipLocation $installDirectory `
    -Checksum $checksum `
    -ChecksumType 'sha256'

$executable = Join-Path $installDirectory 'blokebot\blokebot.exe'
Install-BinFile -Name 'blokebot' -Path $executable
Write-Host 'BlokeBot does not start automatically. Run `blokebot help` before serving.'
Write-Host 'Persistent state stays in the platform data directory or an explicit --data-dir.' '''


def _chocolatey_uninstall() -> str:
    return '''$ErrorActionPreference = 'Stop'
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

Uninstall-BinFile -Name 'blokebot'
$installDirectory = Join-Path $toolsDir 'install'
if (Test-Path $installDirectory) {
    Remove-Item $installDirectory -Recurse -Force
}

Write-Host 'BlokeBot user data is outside the package directory and has been preserved.' '''


def _winget_version_manifest() -> str:
    return f'''PackageIdentifier: {WINGET_IDENTIFIER}
PackageVersion: {VERSION}
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.10.0'''


def _winget_installer_manifest(artifacts: dict[str, dict[str, object]]) -> str:
    x64_name, x64_digest = _artifact(artifacts, "win-x64")
    arm64_name, arm64_digest = _artifact(artifacts, "win-arm64")
    return f'''PackageIdentifier: {WINGET_IDENTIFIER}
PackageVersion: {VERSION}
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
  - RelativeFilePath: blokebot/blokebot.exe
    PortableCommandAlias: blokebot
Installers:
  - Architecture: x64
    InstallerUrl: {_archive_url(x64_name)}
    InstallerSha256: {x64_digest.upper()}
  - Architecture: arm64
    InstallerUrl: {_archive_url(arm64_name)}
    InstallerSha256: {arm64_digest.upper()}
ManifestType: installer
ManifestVersion: 1.10.0'''


def _winget_locale_manifest() -> str:
    return f'''PackageIdentifier: {WINGET_IDENTIFIER}
PackageVersion: {VERSION}
PackageLocale: en-US
Publisher: alsi-lawr
PublisherUrl: https://github.com/alsi-lawr
PublisherSupportUrl: https://github.com/{REPOSITORY}/issues
Author: BlokeBot contributors
PackageName: BlokeBot
PackageUrl: https://github.com/{REPOSITORY}
License: MIT
LicenseUrl: https://github.com/{REPOSITORY}/blob/{RELEASE_TAG}/LICENSE
ShortDescription: Free, open-source Twitch bot and dashboard.
Description: BlokeBot exposes the lowercase blokebot command, does not start automatically, and keeps persistent state outside its installation directory. Run blokebot help before serving.
Moniker: blokebot
Tags:
  - bot
  - cli
  - twitch
ManifestType: defaultLocale
ManifestVersion: 1.10.0'''


def _status() -> str:
    return f'''# BlokeBot {RELEASE_TAG} package-channel status

These files are generated and validated from the immutable `{RELEASE_TAG}` release archives and `checksums.toml`. They do not rebuild or repack the application archives.

- **GitHub release:** release-ready; `{RELEASE_TAG}` has not been published by this repository change.
- **Homebrew:** formula ready for `alsi-lawr/homebrew-tap`; the tap repository does not yet exist, so `brew install alsi-lawr/tap/blokebot` is not live.
- **Scoop:** manifest ready for `alsi-lawr/scoop-bucket`; the bucket repository does not yet exist, so the bucket/install commands are not live.
- **Chocolatey:** metadata is always generated. Publication requires the Chocolatey API-key secret and package availability remains pending moderation after upload.
- **WinGet:** manifests and a deterministic manual-PR bundle are ready. No upstream pull request is created automatically and the package remains pending upstream review.

Every package exposes lowercase `blokebot`, starts no service, and leaves persistent user data outside the installation directory. Run `blokebot help` after installation.'''


def _winget_readme() -> str:
    return f'''# WinGet manual submission bundle

The manifests below target `{WINGET_IDENTIFIER}` {VERSION}. They consume the original immutable Windows ZIP release assets and their SHA-256 values.

1. Validate the manifests with `winget validate --manifest manifests/a/alsi-lawr/BlokeBot/{VERSION}`.
2. Sandbox-test install, `blokebot version`, `blokebot help`, temporary serve, upgrade and uninstall while confirming external user data remains.
3. Copy the version directory into a fork of `microsoft/winget-pkgs` and open the upstream pull request manually.

This repository workflow deliberately does not create the upstream pull request.'''


def _jreleaser_readme() -> str:
    return '''# JReleaser package configuration

The pinned JReleaser 1.25.0 runner validates the ZIP-based Scoop, Chocolatey and WinGet configuration. Final reviewable channel files are generated by `scripts/package_metadata.py` after it verifies `checksums.toml` and every release archive.

Homebrew is intentionally generated by the same repository-owned script instead: JReleaser 1.25.0 Homebrew formula generation cannot consume the required unchanged Apple-Silicon `.tar.gz` release artifact. The archive is never repacked.'''


def _jreleaser_config() -> str:
    return f'''project:
  name: blokebot
  version: {VERSION}
  description: Free, open-source Twitch bot and dashboard
  copyright: Copyright (c) 2026 BlokeBot contributors
  links:
    homepage: https://github.com/{REPOSITORY}
  license: MIT
  authors:
    - BlokeBot contributors
  tags:
    - twitch
    - bot
    - cli
  stereotype: CLI

release:
  github:
    owner: alsi-lawr
    name: BlokeBot
    skipRelease: true

distributions:
  blokebot:
    type: BINARY
    stereotype: CLI
    executable:
      name: blokebot
      windowsExtension: exe
    artifacts:
      - path: ../../release/{release_artifacts.archive_name("win-x64")}
        platform: windows-x86_64
      - path: ../../release/{release_artifacts.archive_name("win-arm64")}
        platform: windows-aarch_64
        extraProperties:
          skipScoop: true
          skipChocolatey: true
          skipWinget: true

packagers:
  scoop:
    active: ALWAYS
    skipPublishing: true
    packageName: blokebot
    downloadUrl: {_archive_url(release_artifacts.archive_name("win-x64"))}
    repository:
      owner: alsi-lawr
      name: scoop-bucket
  chocolatey:
    active: ALWAYS
    skipPublishing: true
    remoteBuild: true
    packageName: blokebot
    downloadUrl: {_archive_url(release_artifacts.archive_name("win-x64"))}
  winget:
    active: ALWAYS
    skipPublishing: true
    downloadUrl: {_archive_url(release_artifacts.archive_name("win-x64"))}
    package:
      identifier: {WINGET_IDENTIFIER}
      name: BlokeBot
      version: {VERSION}
      url: https://github.com/{REPOSITORY}
    publisher:
      name: alsi-lawr
      url: https://github.com/alsi-lawr
      supportUrl: https://github.com/{REPOSITORY}/issues
    installer:
      type: ZIP
      command: blokebot'''


def _write_winget_bundle(root: Path) -> None:
    bundle_path = root / "winget/winget-pr-v0.1.0.zip"
    bundle_path.parent.mkdir(parents=True, exist_ok=True)
    included = [
        "winget/README.md",
        "winget/manifests/a/alsi-lawr/BlokeBot/0.1.0/alsi-lawr.BlokeBot.yaml",
        "winget/manifests/a/alsi-lawr/BlokeBot/0.1.0/alsi-lawr.BlokeBot.installer.yaml",
        "winget/manifests/a/alsi-lawr/BlokeBot/0.1.0/alsi-lawr.BlokeBot.locale.en-US.yaml",
    ]
    with zipfile.ZipFile(bundle_path, "x", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for relative in sorted(included):
            member_name = relative.removeprefix("winget/")
            info = zipfile.ZipInfo(member_name, ZIP_TIMESTAMP)
            info.create_system = 3
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (0o100644 << 16)
            archive.writestr(info, (root / relative).read_bytes())


def generate(release_directory: Path, checksums_path: Path, output_directory: Path) -> None:
    if output_directory.exists():
        raise PackageMetadataError(f"Refusing to overwrite package metadata directory: {output_directory}")
    artifacts = _load_artifacts(release_directory, checksums_path)
    output_directory.parent.mkdir(parents=True, exist_ok=True)
    temporary = Path(
        tempfile.mkdtemp(prefix=f".{output_directory.name}-", dir=output_directory.parent)
    )
    try:
        _write_text(temporary, "STATUS.md", _status())
        checksums_copy = temporary / "release-assets/checksums.toml"
        checksums_copy.parent.mkdir(parents=True, exist_ok=True)
        checksums_copy.write_bytes(checksums_path.read_bytes())
        _write_text(temporary, "homebrew/Formula/blokebot.rb", _homebrew_formula(artifacts))
        _write_text(temporary, "scoop/blokebot.json", _scoop_manifest(artifacts))
        _write_text(temporary, "chocolatey/blokebot.nuspec", _chocolatey_nuspec())
        _write_text(
            temporary,
            "chocolatey/tools/chocolateyinstall.ps1",
            _chocolatey_install(artifacts),
        )
        _write_text(
            temporary,
            "chocolatey/tools/chocolateyuninstall.ps1",
            _chocolatey_uninstall(),
        )
        _write_text(temporary, "winget/README.md", _winget_readme())
        winget_root = "winget/manifests/a/alsi-lawr/BlokeBot/0.1.0"
        _write_text(temporary, f"{winget_root}/alsi-lawr.BlokeBot.yaml", _winget_version_manifest())
        _write_text(
            temporary,
            f"{winget_root}/alsi-lawr.BlokeBot.installer.yaml",
            _winget_installer_manifest(artifacts),
        )
        _write_text(
            temporary,
            f"{winget_root}/alsi-lawr.BlokeBot.locale.en-US.yaml",
            _winget_locale_manifest(),
        )
        _write_winget_bundle(temporary)
        _write_text(temporary, "jreleaser/README.md", _jreleaser_readme())
        _write_text(temporary, "jreleaser/jreleaser.yml", _jreleaser_config())
        validate(temporary, release_directory, checksums_path)
        temporary.rename(output_directory)
    except Exception:
        shutil.rmtree(temporary, ignore_errors=True)
        raise


def _validate_url_and_hash(text: str, name: str, digest: str) -> None:
    if _archive_url(name) not in text or digest.casefold() not in text.casefold():
        raise PackageMetadataError(f"Package metadata does not reference {name} and its SHA-256")


def validate(output_directory: Path, release_directory: Path, checksums_path: Path) -> None:
    artifacts = _load_artifacts(release_directory, checksums_path)
    actual = {
        path.relative_to(output_directory).as_posix()
        for path in output_directory.rglob("*")
        if path.is_file()
    }
    if actual != set(CHANNEL_FILES):
        missing = sorted(set(CHANNEL_FILES) - actual)
        unexpected = sorted(actual - set(CHANNEL_FILES))
        raise PackageMetadataError(
            f"Package metadata file set is incorrect; missing={missing}, unexpected={unexpected}"
        )

    if (output_directory / "release-assets/checksums.toml").read_bytes() != checksums_path.read_bytes():
        raise PackageMetadataError("Generated package metadata does not retain checksums.toml exactly")

    formula = (output_directory / "homebrew/Formula/blokebot.rb").read_text(encoding="utf-8")
    osx_name, osx_digest = _artifact(artifacts, "osx-arm64")
    _validate_url_and_hash(formula, osx_name, osx_digest)
    if f'sha256 "{osx_digest}"' not in formula:
        raise PackageMetadataError("Homebrew formula does not declare the release SHA-256")
    if (
        'libexec.install Dir["blokebot/*"]' not in formula
        or 'bin.install_symlink libexec/"blokebot"' not in formula
        or "depends_on arch: :arm64" not in formula
    ):
        raise PackageMetadataError(
            "Homebrew formula does not retain the self-contained tree behind the blokebot symlink"
        )

    scoop_path = output_directory / "scoop/blokebot.json"
    scoop = json.loads(scoop_path.read_text(encoding="utf-8"))
    if set(scoop.get("architecture", {})) != {"64bit", "arm64"}:
        raise PackageMetadataError("Scoop manifest does not contain exactly x64 and ARM64")
    if scoop.get("bin") != [["blokebot.exe", "blokebot"]] or "persist" in scoop:
        raise PackageMetadataError("Scoop manifest does not expose only the lowercase executable")
    for rid in ("win-x64", "win-arm64"):
        name, digest = _artifact(artifacts, rid)
        _validate_url_and_hash(scoop_path.read_text(encoding="utf-8"), name, digest)

    nuspec = output_directory / "chocolatey/blokebot.nuspec"
    try:
        root = ET.parse(nuspec).getroot()
    except ET.ParseError as error:
        raise PackageMetadataError(f"Chocolatey nuspec is invalid XML: {error}") from error
    namespace = {"n": "http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd"}
    if root.findtext("n:metadata/n:id", namespaces=namespace) != "blokebot":
        raise PackageMetadataError("Chocolatey package ID is not blokebot")
    install = (output_directory / "chocolatey/tools/chocolateyinstall.ps1").read_text(
        encoding="utf-8"
    )
    uninstall = (output_directory / "chocolatey/tools/chocolateyuninstall.ps1").read_text(
        encoding="utf-8"
    )
    tools_directory_definition = (
        "$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition"
    )
    for name, script in (("install", install), ("uninstall", uninstall)):
        if script.count(tools_directory_definition) != 1 or script.index(
            tools_directory_definition
        ) != script.index("$toolsDir"):
            raise PackageMetadataError(
                f"Chocolatey {name} script does not define $toolsDir before first use"
            )
    for rid in ("win-x64", "win-arm64"):
        name, digest = _artifact(artifacts, rid)
        _validate_url_and_hash(install, name, digest)
    if "Install-BinFile -Name 'blokebot'" not in install:
        raise PackageMetadataError("Chocolatey does not expose the lowercase blokebot shim")

    installer = (
        output_directory
        / "winget/manifests/a/alsi-lawr/BlokeBot/0.1.0/alsi-lawr.BlokeBot.installer.yaml"
    ).read_text(encoding="utf-8")
    if "NestedInstallerType: portable" not in installer:
        raise PackageMetadataError("WinGet does not use a nested portable installer")
    if installer.count("RelativeFilePath: blokebot/blokebot.exe") != 1:
        raise PackageMetadataError("WinGet does not target the nested lowercase executable")
    for rid in ("win-x64", "win-arm64"):
        name, digest = _artifact(artifacts, rid)
        _validate_url_and_hash(installer, name, digest)

    bundle = output_directory / "winget/winget-pr-v0.1.0.zip"
    with zipfile.ZipFile(bundle) as archive:
        if archive.namelist() != sorted(archive.namelist()):
            raise PackageMetadataError("WinGet manual-PR bundle order is not deterministic")
        if any(member.date_time != ZIP_TIMESTAMP for member in archive.infolist()):
            raise PackageMetadataError("WinGet manual-PR bundle timestamps are not deterministic")

    source = "\n".join(
        path.read_text(encoding="utf-8")
        for path in output_directory.rglob("*")
        if path.is_file() and path.suffix != ".zip"
    )
    for required in ("blokebot help", "does not start automatically", "persistent state"):
        if required.casefold() not in source.casefold():
            raise PackageMetadataError(f"Package metadata is missing required operator guidance: {required}")
    for prohibited in ("Start-Service", "New-Service", "systemctl enable", "AppData\\Local\\BlokeBot"):
        if prohibited.casefold() in source.casefold():
            raise PackageMetadataError(f"Package metadata contains prohibited behaviour: {prohibited}")


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Generate package-channel metadata from verified immutable release assets."
    )
    subparsers = parser.add_subparsers(dest="command", required=True)
    for command in ("generate", "validate"):
        command_parser = subparsers.add_parser(command)
        command_parser.add_argument("--release-dir", required=True, type=Path)
        command_parser.add_argument("--checksums", required=True, type=Path)
        command_parser.add_argument("--output-dir", required=True, type=Path)
    return parser


def main(arguments: list[str] | None = None) -> int:
    args = _parser().parse_args(arguments)
    try:
        if args.command == "generate":
            generate(args.release_dir, args.checksums, args.output_dir)
        else:
            validate(args.output_dir, args.release_dir, args.checksums)
    except (OSError, PackageMetadataError, json.JSONDecodeError) as error:
        print(f"package-metadata: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
