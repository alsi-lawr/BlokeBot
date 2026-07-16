#!/usr/bin/env python3

from __future__ import annotations

import argparse
import dataclasses
import gzip
import hashlib
import os
from pathlib import Path, PurePosixPath
import stat
import sys
import tarfile
import tomllib
import zipfile


VERSION = "0.1.0"
RELEASE_TAG = f"v{VERSION}"
ARCHIVE_ROOT = "blokebot"
CHECKSUM_FILE_NAME = "checksums.toml"
SERVER_GUIDE_NAME = "SERVER_SETUP.md"
ZIP_TIMESTAMP = (1980, 1, 1, 0, 0, 0)

RID_FORMATS: tuple[tuple[str, str], ...] = (
    ("linux-x64", "tar.gz"),
    ("linux-arm64", "tar.gz"),
    ("osx-arm64", "tar.gz"),
    ("win-x64", "zip"),
    ("win-arm64", "zip"),
)

PROHIBITED_DIRECTORIES = {
    ".agent-workspace",
    ".cache",
    ".git",
    ".github",
    "cache",
    "node_modules",
    "obj",
    "packages",
    "src",
}
PROHIBITED_FILE_NAMES = {
    ".env",
    "package-lock.json",
    "package.json",
    "twitch.tokens.json",
}
PROHIBITED_SUFFIXES = (
    ".db",
    ".db-shm",
    ".db-wal",
    ".env",
    ".nupkg",
    ".pdb",
    ".snupkg",
    ".tokens.json",
)


class ReleaseArtifactError(RuntimeError):
    pass


@dataclasses.dataclass(frozen=True)
class ArchiveEntry:
    source: Path
    name: PurePosixPath
    executable: bool


def archive_name(rid: str) -> str:
    formats = dict(RID_FORMATS)
    try:
        extension = formats[rid]
    except KeyError as error:
        raise ReleaseArtifactError(f"Unsupported release RID: {rid}") from error
    return f"blokebot-v{VERSION}-{rid}.{extension}"


def expected_archive_names() -> tuple[str, ...]:
    return tuple(archive_name(rid) for rid, _ in RID_FORMATS)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _validate_publish_path(relative: PurePosixPath) -> None:
    normalized_parts = tuple(part.casefold() for part in relative.parts)
    normalized_name = relative.name.casefold()
    normalized_path = relative.as_posix().casefold()

    if any(part in PROHIBITED_DIRECTORIES for part in normalized_parts):
        raise ReleaseArtifactError(f"Prohibited directory in publish output: {relative}")
    if normalized_name in PROHIBITED_FILE_NAMES:
        raise ReleaseArtifactError(f"Prohibited file in publish output: {relative}")
    if normalized_name.startswith("appsettings.") and normalized_name != "appsettings.json":
        raise ReleaseArtifactError(f"Environment-specific configuration is prohibited: {relative}")
    if normalized_name.endswith(PROHIBITED_SUFFIXES):
        raise ReleaseArtifactError(f"State, credential, or package file is prohibited: {relative}")
    if "blokebot.site" in normalized_path:
        raise ReleaseArtifactError(f"Public-site content is prohibited in the bot archive: {relative}")
    if normalized_name in {"license", SERVER_GUIDE_NAME.casefold()}:
        raise ReleaseArtifactError(f"Publish output collides with release documentation: {relative}")


def _add_entry(entries: list[ArchiveEntry], entry: ArchiveEntry, names: set[str]) -> None:
    normalized = entry.name.as_posix().casefold()
    if normalized in names:
        raise ReleaseArtifactError(f"Duplicate archive output path: {entry.name}")
    names.add(normalized)
    entries.append(entry)


def collect_entries(
    publish_directory: Path,
    rid: str,
    license_path: Path,
    server_guide_path: Path,
) -> list[ArchiveEntry]:
    archive_name(rid)
    if not publish_directory.is_dir():
        raise ReleaseArtifactError(f"Publish directory does not exist: {publish_directory}")
    if not license_path.is_file():
        raise ReleaseArtifactError(f"Licence file does not exist: {license_path}")
    if not server_guide_path.is_file():
        raise ReleaseArtifactError(f"Server setup guide does not exist: {server_guide_path}")

    entries: list[ArchiveEntry] = []
    names: set[str] = set()
    for root, directory_names, file_names in os.walk(publish_directory):
        root_path = Path(root)
        for directory_name in directory_names:
            directory_path = root_path / directory_name
            if directory_path.is_symlink():
                raise ReleaseArtifactError(f"Symbolic links are prohibited: {directory_path}")
            relative_directory = PurePosixPath(
                directory_path.relative_to(publish_directory).as_posix()
            )
            if directory_name.casefold() in PROHIBITED_DIRECTORIES:
                raise ReleaseArtifactError(
                    f"Prohibited directory in publish output: {relative_directory}"
                )

        for file_name in file_names:
            source_path = root_path / file_name
            if source_path.is_symlink() or not source_path.is_file():
                raise ReleaseArtifactError(f"Only regular publish files are permitted: {source_path}")
            relative = PurePosixPath(source_path.relative_to(publish_directory).as_posix())
            _validate_publish_path(relative)
            mode = source_path.stat().st_mode
            _add_entry(
                entries,
                ArchiveEntry(
                    source=source_path,
                    name=PurePosixPath(ARCHIVE_ROOT) / relative,
                    executable=bool(mode & 0o111) or relative.name.casefold() == "blokebot.exe",
                ),
                names,
            )

    required_executable = "blokebot.exe" if rid.startswith("win-") else "blokebot"
    publish_names = {entry.name.name.casefold() for entry in entries}
    for required in (required_executable, "blokebot.dll"):
        if required.casefold() not in publish_names:
            raise ReleaseArtifactError(f"Publish output is incomplete; missing {required}")

    _add_entry(
        entries,
        ArchiveEntry(license_path, PurePosixPath(ARCHIVE_ROOT) / "LICENSE", False),
        names,
    )
    _add_entry(
        entries,
        ArchiveEntry(
            server_guide_path,
            PurePosixPath(ARCHIVE_ROOT) / SERVER_GUIDE_NAME,
            False,
        ),
        names,
    )
    return sorted(entries, key=lambda entry: entry.name.as_posix())


def _directory_names(entries: list[ArchiveEntry]) -> list[PurePosixPath]:
    directories = {PurePosixPath(ARCHIVE_ROOT)}
    for entry in entries:
        directories.update(entry.name.parents)
    directories.discard(PurePosixPath("."))
    return sorted(directories, key=lambda path: (len(path.parts), path.as_posix()))


def _write_tar_gz(output_path: Path, entries: list[ArchiveEntry]) -> None:
    with output_path.open("xb") as raw_output:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw_output, mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode="w", format=tarfile.PAX_FORMAT) as archive:
                for directory in _directory_names(entries):
                    info = tarfile.TarInfo(f"{directory.as_posix()}/")
                    info.type = tarfile.DIRTYPE
                    info.mode = 0o755
                    info.mtime = 0
                    info.uid = 0
                    info.gid = 0
                    info.uname = "root"
                    info.gname = "root"
                    archive.addfile(info)

                for entry in entries:
                    info = archive.gettarinfo(str(entry.source), arcname=entry.name.as_posix())
                    info.mode = 0o755 if entry.executable else 0o644
                    info.mtime = 0
                    info.uid = 0
                    info.gid = 0
                    info.uname = "root"
                    info.gname = "root"
                    info.pax_headers = {}
                    with entry.source.open("rb") as source:
                        archive.addfile(info, source)


def _zip_info(name: str, mode: int, is_directory: bool) -> zipfile.ZipInfo:
    info = zipfile.ZipInfo(name, ZIP_TIMESTAMP)
    info.create_system = 3
    info.compress_type = zipfile.ZIP_DEFLATED
    file_type = stat.S_IFDIR if is_directory else stat.S_IFREG
    info.external_attr = (file_type | mode) << 16
    if is_directory:
        info.external_attr |= 0x10
    return info


def _write_zip(output_path: Path, entries: list[ArchiveEntry]) -> None:
    with zipfile.ZipFile(output_path, mode="x", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for directory in _directory_names(entries):
            archive.writestr(_zip_info(f"{directory.as_posix()}/", 0o755, True), b"")
        for entry in entries:
            mode = 0o755 if entry.executable else 0o644
            archive.writestr(_zip_info(entry.name.as_posix(), mode, False), entry.source.read_bytes())


def create_archive(
    publish_directory: Path,
    output_directory: Path,
    rid: str,
    license_path: Path,
    server_guide_path: Path,
) -> Path:
    output_directory.mkdir(parents=True, exist_ok=True)
    output_path = output_directory / archive_name(rid)
    if output_path.exists():
        raise ReleaseArtifactError(f"Refusing to overwrite release archive: {output_path}")

    entries = collect_entries(publish_directory, rid, license_path, server_guide_path)
    try:
        if output_path.name.endswith(".tar.gz"):
            _write_tar_gz(output_path, entries)
        else:
            _write_zip(output_path, entries)
        validate_archive(output_path)
    except Exception:
        output_path.unlink(missing_ok=True)
        raise
    return output_path


def _validate_member_name(name: str, seen: set[str]) -> None:
    path = PurePosixPath(name.rstrip("/"))
    if not path.parts or path.is_absolute() or ".." in path.parts:
        raise ReleaseArtifactError(f"Unsafe archive path: {name}")
    if path.parts[0] != ARCHIVE_ROOT:
        raise ReleaseArtifactError(f"Archive entry is outside {ARCHIVE_ROOT}/: {name}")
    normalized = name.casefold()
    if normalized in seen:
        raise ReleaseArtifactError(f"Duplicate archive entry: {name}")
    seen.add(normalized)
    if len(path.parts) > 1 and path.name.casefold() not in {
        "license",
        SERVER_GUIDE_NAME.casefold(),
    }:
        _validate_publish_path(PurePosixPath(*path.parts[1:]))


def validate_archive(path: Path) -> None:
    if not path.is_file():
        raise ReleaseArtifactError(f"Archive does not exist: {path}")
    seen: set[str] = set()
    regular_files: set[str] = set()

    if path.name.endswith(".tar.gz"):
        with tarfile.open(path, mode="r:gz") as archive:
            for member in archive.getmembers():
                _validate_member_name(member.name, seen)
                if member.isfile():
                    regular_files.add(member.name.casefold())
                elif not member.isdir():
                    raise ReleaseArtifactError(f"Archive contains a non-file entry: {member.name}")
    elif path.name.endswith(".zip"):
        with zipfile.ZipFile(path) as archive:
            for member in archive.infolist():
                _validate_member_name(member.filename, seen)
                mode = member.external_attr >> 16
                if stat.S_ISLNK(mode):
                    raise ReleaseArtifactError(f"Archive contains a symbolic link: {member.filename}")
                if not member.is_dir():
                    regular_files.add(member.filename.casefold())
    else:
        raise ReleaseArtifactError(f"Unsupported archive format: {path.name}")

    required = {
        f"{ARCHIVE_ROOT}/license".casefold(),
        f"{ARCHIVE_ROOT}/{SERVER_GUIDE_NAME}".casefold(),
        f"{ARCHIVE_ROOT}/blokebot.dll".casefold(),
    }
    if path.name.endswith(".zip"):
        required.add(f"{ARCHIVE_ROOT}/blokebot.exe".casefold())
    else:
        required.add(f"{ARCHIVE_ROOT}/blokebot".casefold())
    missing = sorted(required - regular_files)
    if missing:
        raise ReleaseArtifactError(f"Archive is incomplete; missing: {', '.join(missing)}")


def _release_files(directory: Path, allowed_names: set[str] | None = None) -> dict[str, Path]:
    if not directory.is_dir():
        raise ReleaseArtifactError(f"Release directory does not exist: {directory}")
    files = {path.name: path for path in directory.iterdir() if path.is_file()}
    expected = set(expected_archive_names())
    actual_archives = {
        name
        for name in files
        if name.startswith("blokebot-v") and (name.endswith(".tar.gz") or name.endswith(".zip"))
    }
    missing = sorted(expected - actual_archives)
    unexpected = sorted(actual_archives - expected)
    if missing:
        raise ReleaseArtifactError(f"Release artifact set is incomplete; missing: {', '.join(missing)}")
    if unexpected:
        raise ReleaseArtifactError(f"Unexpected release archives: {', '.join(unexpected)}")
    allowed = expected | (allowed_names or set())
    unexpected_files = sorted(set(files) - allowed)
    if unexpected_files:
        raise ReleaseArtifactError(f"Unexpected release files: {', '.join(unexpected_files)}")
    return {name: files[name] for name in expected}


def generate_checksums(directory: Path, output_path: Path) -> None:
    if output_path.exists():
        raise ReleaseArtifactError(f"Refusing to overwrite checksum manifest: {output_path}")
    release_files = _release_files(directory)
    lines = ["version = 1", f'release = "{RELEASE_TAG}"', ""]
    for rid, _ in RID_FORMATS:
        name = archive_name(rid)
        path = release_files[name]
        validate_archive(path)
        lines.extend(
            [
                "[[artifact]]",
                f'name = "{name}"',
                f'rid = "{rid}"',
                f'sha256 = "{sha256_file(path)}"',
                f"size = {path.stat().st_size}",
                "",
            ]
        )
    output_path.parent.mkdir(parents=True, exist_ok=True)
    try:
        with output_path.open("x", encoding="utf-8", newline="\n") as output:
            output.write("\n".join(lines))
    except Exception:
        output_path.unlink(missing_ok=True)
        raise


def verify_checksums(directory: Path, manifest_path: Path) -> None:
    allowed_names = {manifest_path.name} if manifest_path.parent.resolve() == directory.resolve() else set()
    release_files = _release_files(directory, allowed_names)
    if not manifest_path.is_file():
        raise ReleaseArtifactError(f"Checksum manifest does not exist: {manifest_path}")
    with manifest_path.open("rb") as manifest_file:
        manifest = tomllib.load(manifest_file)
    if manifest.get("version") != 1 or manifest.get("release") != RELEASE_TAG:
        raise ReleaseArtifactError("Checksum manifest version or release is incorrect")

    artifacts = manifest.get("artifact")
    if not isinstance(artifacts, list):
        raise ReleaseArtifactError("Checksum manifest has no artifact entries")
    by_name: dict[str, dict[str, object]] = {}
    for artifact in artifacts:
        if not isinstance(artifact, dict) or not isinstance(artifact.get("name"), str):
            raise ReleaseArtifactError("Checksum manifest contains an invalid artifact entry")
        name = artifact["name"]
        if name in by_name:
            raise ReleaseArtifactError(f"Checksum manifest contains duplicate artifact: {name}")
        by_name[name] = artifact

    expected_names = set(expected_archive_names())
    if set(by_name) != expected_names:
        raise ReleaseArtifactError("Checksum manifest does not describe the exact release artifact set")

    for rid, _ in RID_FORMATS:
        name = archive_name(rid)
        path = release_files[name]
        artifact = by_name[name]
        if artifact.get("rid") != rid:
            raise ReleaseArtifactError(f"Checksum RID mismatch for {name}")
        if artifact.get("sha256") != sha256_file(path):
            raise ReleaseArtifactError(f"Checksum hash mismatch for {name}")
        if artifact.get("size") != path.stat().st_size:
            raise ReleaseArtifactError(f"Checksum size mismatch for {name}")
        validate_archive(path)


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Create and validate immutable BlokeBot release artifacts.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    archive_parser = subparsers.add_parser("archive", help="Create one deterministic RID archive.")
    archive_parser.add_argument("--rid", required=True, choices=[rid for rid, _ in RID_FORMATS])
    archive_parser.add_argument("--publish-dir", required=True, type=Path)
    archive_parser.add_argument("--output-dir", required=True, type=Path)
    archive_parser.add_argument("--license", type=Path, default=Path("LICENSE"))
    archive_parser.add_argument(
        "--server-guide", type=Path, default=Path("distribution") / SERVER_GUIDE_NAME
    )

    validate_parser = subparsers.add_parser("validate", help="Validate one final archive.")
    validate_parser.add_argument("archive", type=Path)

    checksums_parser = subparsers.add_parser(
        "checksums", help="Create checksums.toml for the exact five-archive set."
    )
    checksums_parser.add_argument("--archive-dir", required=True, type=Path)
    checksums_parser.add_argument("--output", required=True, type=Path)

    verify_parser = subparsers.add_parser("verify", help="Verify the exact archive set and checksums.")
    verify_parser.add_argument("--archive-dir", required=True, type=Path)
    verify_parser.add_argument("--checksums", required=True, type=Path)
    return parser


def main(arguments: list[str] | None = None) -> int:
    args = _parser().parse_args(arguments)
    try:
        if args.command == "archive":
            result = create_archive(
                args.publish_dir,
                args.output_dir,
                args.rid,
                args.license,
                args.server_guide,
            )
            print(result)
        elif args.command == "validate":
            validate_archive(args.archive)
        elif args.command == "checksums":
            generate_checksums(args.archive_dir, args.output)
        elif args.command == "verify":
            verify_checksums(args.archive_dir, args.checksums)
        else:
            raise ReleaseArtifactError(f"Unknown command: {args.command}")
    except ReleaseArtifactError as error:
        print(f"release-artifacts: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
