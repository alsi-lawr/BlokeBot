#!/usr/bin/env python3

from __future__ import annotations

import argparse
from pathlib import Path
import re
import sys


DIGEST = re.compile(r"sha256:[0-9a-f]{64}")
ARCHITECTURES = ("amd64", "arm64")


class OciManifestError(RuntimeError):
    pass


def image_slug(image: str) -> str:
    slug = image.rsplit("/", 1)[-1]
    if not slug or not re.fullmatch(r"[a-z0-9][a-z0-9._-]*", slug):
        raise OciManifestError(f"Invalid image name: {image}")
    return slug


def collect_references(directory: Path, image: str) -> list[str]:
    if not directory.is_dir():
        raise OciManifestError(f"Digest directory does not exist: {directory}")
    slug = image_slug(image)
    expected_names = {f"{slug}-{architecture}.digest" for architecture in ARCHITECTURES}
    actual_names = {path.name for path in directory.iterdir() if path.is_file()}
    missing = sorted(expected_names - actual_names)
    unexpected = sorted(actual_names - expected_names)
    if missing:
        raise OciManifestError(f"Digest set is incomplete; missing: {', '.join(missing)}")
    if unexpected:
        raise OciManifestError(f"Unexpected digest files: {', '.join(unexpected)}")

    references: list[str] = []
    seen_digests: set[str] = set()
    for architecture in ARCHITECTURES:
        path = directory / f"{slug}-{architecture}.digest"
        reference = path.read_text(encoding="utf-8").strip()
        prefix = f"{image}@"
        if not reference.startswith(prefix):
            raise OciManifestError(f"Digest reference names the wrong image: {reference}")
        digest = reference.removeprefix(prefix)
        if not DIGEST.fullmatch(digest):
            raise OciManifestError(f"Invalid OCI digest: {digest}")
        if digest in seen_digests:
            raise OciManifestError(f"Duplicate architecture digest: {digest}")
        seen_digests.add(digest)
        references.append(reference)
    return references


def write_references(directory: Path, image: str, output: Path) -> None:
    if output.exists():
        raise OciManifestError(f"Refusing to overwrite OCI reference file: {output}")
    references = collect_references(directory, image)
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("x", encoding="utf-8", newline="\n") as target:
        target.write("\n".join(references) + "\n")


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate native image digests for a manifest.")
    parser.add_argument("--digest-dir", type=Path, required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(arguments)
    try:
        write_references(args.digest_dir, args.image, args.output)
    except OciManifestError as error:
        print(f"oci-manifest: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
