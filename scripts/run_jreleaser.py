#!/usr/bin/env python3

from __future__ import annotations

import argparse
import hashlib
import os
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
import zipfile


JRELEASER_VERSION = "1.25.0"
JRELEASER_SHA256 = "7c086a384e509ae30ad12ce2f10946601c0798e746d06a5538afc267e398644b"
JRELEASER_URL = (
    "https://github.com/jreleaser/jreleaser/releases/download/"
    f"v{JRELEASER_VERSION}/jreleaser-{JRELEASER_VERSION}.zip"
)


class JReleaserInstallError(RuntimeError):
    pass


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def verify_archive(path: Path, expected_sha256: str = JRELEASER_SHA256) -> None:
    actual = sha256_file(path)
    if actual != expected_sha256:
        raise JReleaserInstallError(
            f"JReleaser archive hash mismatch: expected {expected_sha256}, received {actual}"
        )


def _safe_member_path(destination: Path, member_name: str) -> Path:
    member = PurePosixPath(member_name)
    if member.is_absolute() or ".." in member.parts:
        raise JReleaserInstallError(f"Unsafe JReleaser archive entry: {member_name}")
    output = destination.joinpath(*member.parts)
    if destination.resolve() not in output.resolve().parents and output.resolve() != destination.resolve():
        raise JReleaserInstallError(f"JReleaser archive entry escapes destination: {member_name}")
    return output


def extract_archive(archive_path: Path, destination: Path) -> None:
    with zipfile.ZipFile(archive_path) as archive:
        seen: set[str] = set()
        for member in archive.infolist():
            normalized = member.filename.casefold()
            if normalized in seen:
                raise JReleaserInstallError(f"Duplicate JReleaser archive entry: {member.filename}")
            seen.add(normalized)
            mode = member.external_attr >> 16
            if (mode & 0o170000) == 0o120000:
                raise JReleaserInstallError(f"JReleaser archive contains a symlink: {member.filename}")
            output = _safe_member_path(destination, member.filename)
            if member.is_dir():
                output.mkdir(parents=True, exist_ok=True)
                continue
            output.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(member) as source, output.open("xb") as target:
                shutil.copyfileobj(source, target)
            if mode & 0o111:
                output.chmod(0o755)


def install_archive(
    archive_path: Path,
    installation_root: Path,
    expected_sha256: str = JRELEASER_SHA256,
) -> Path:
    verify_archive(archive_path, expected_sha256)
    destination = installation_root / f"jreleaser-{JRELEASER_VERSION}"
    launcher_name = "jreleaser.bat" if os.name == "nt" else "jreleaser"
    launcher = destination / "bin" / launcher_name
    if destination.exists():
        if launcher.is_file():
            return launcher
        raise JReleaserInstallError(f"Incomplete JReleaser installation exists: {destination}")

    installation_root.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix="jreleaser-install-", dir=installation_root))
    try:
        extract_archive(archive_path, temporary)
        candidates = list(temporary.rglob(launcher_name))
        candidates = [candidate for candidate in candidates if candidate.parent.name == "bin"]
        if len(candidates) != 1:
            raise JReleaserInstallError("JReleaser launcher was not found exactly once")
        extracted_root = candidates[0].parent.parent
        extracted_root.rename(destination)
    finally:
        shutil.rmtree(temporary, ignore_errors=True)
    if not launcher.is_file():
        raise JReleaserInstallError(f"Installed JReleaser launcher is missing: {launcher}")
    return launcher


def download_archive(cache_path: Path) -> Path:
    if cache_path.exists():
        verify_archive(cache_path)
        return cache_path
    cache_path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=cache_path.parent, delete=False) as temporary_file:
        temporary_path = Path(temporary_file.name)
    try:
        urllib.request.urlretrieve(JRELEASER_URL, temporary_path)
        verify_archive(temporary_path)
        temporary_path.replace(cache_path)
    except Exception:
        temporary_path.unlink(missing_ok=True)
        raise
    return cache_path


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=f"Run checksum-pinned JReleaser {JRELEASER_VERSION}."
    )
    parser.add_argument(
        "--cache-dir",
        type=Path,
        default=Path.home() / ".cache" / "blokebot" / "jreleaser",
    )
    parser.add_argument("arguments", nargs=argparse.REMAINDER)
    return parser


def main(arguments: list[str] | None = None) -> int:
    args = _parser().parse_args(arguments)
    archive = args.cache_dir / f"jreleaser-{JRELEASER_VERSION}.zip"
    installation_root = args.cache_dir / "installations"
    try:
        launcher = install_archive(download_archive(archive), installation_root)
        return subprocess.run([str(launcher), *args.arguments], check=False).returncode
    except (JReleaserInstallError, OSError, urllib.error.URLError) as error:
        print(f"run-jreleaser: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
