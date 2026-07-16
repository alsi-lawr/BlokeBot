#!/usr/bin/env python3

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
import urllib.error
import urllib.request


class ContainerSmokeError(RuntimeError):
    pass


def _docker(*arguments: str, capture: bool = True) -> str:
    completed = subprocess.run(
        ["docker", *arguments],
        check=False,
        capture_output=capture,
        text=True,
    )
    if completed.returncode != 0:
        raise ContainerSmokeError(
            f"docker {' '.join(arguments)} failed: {completed.stderr.strip()}"
        )
    return completed.stdout.strip()


def _inspect(image: str) -> dict[str, object]:
    result = json.loads(_docker("image", "inspect", image))
    if not isinstance(result, list) or len(result) != 1:
        raise ContainerSmokeError(f"Could not inspect exactly one image: {image}")
    return result[0]


def _read_http_body(url: str, accepted_statuses: frozenset[int]) -> str:
    try:
        response = urllib.request.urlopen(url, timeout=2)
    except urllib.error.HTTPError as error:
        with error:
            status = error.code
            body = error.read()
    else:
        with response:
            status = response.status
            body = response.read()
    if status not in accepted_statuses:
        raise ContainerSmokeError(f"Unexpected HTTP status {status} from {url}")
    return body.decode("utf-8")


def _wait_for_page(
    port: str,
    markers: tuple[str, ...],
    accepted_statuses: frozenset[int],
    timeout: float = 30,
) -> None:
    url = f"http://127.0.0.1:{port}/"
    deadline = time.monotonic() + timeout
    last_error: Exception | None = None
    while time.monotonic() < deadline:
        try:
            body = _read_http_body(url, accepted_statuses)
            missing = [marker for marker in markers if marker not in body]
            if missing:
                raise ContainerSmokeError(f"{url} is missing: {', '.join(missing)}")
            return
        except (OSError, urllib.error.URLError) as error:
            last_error = error
            time.sleep(0.25)
    raise ContainerSmokeError(f"Container page did not become ready: {last_error}")


def smoke(image: str, kind: str, expected_revision: str | None) -> None:
    inspected = _inspect(image)
    config = inspected.get("Config")
    if not isinstance(config, dict):
        raise ContainerSmokeError("Image has no configuration")
    user = config.get("User")
    if not isinstance(user, str) or not user or user in {"0", "root", "0:0"}:
        raise ContainerSmokeError(f"Image does not have a non-root user: {user!r}")
    labels = config.get("Labels")
    if not isinstance(labels, dict):
        raise ContainerSmokeError("Image has no OCI labels")
    expected_labels = {
        "org.opencontainers.image.source": "https://github.com/alsi-lawr/BlokeBot",
        "org.opencontainers.image.version": "0.1.0",
    }
    for name, value in expected_labels.items():
        if labels.get(name) != value:
            raise ContainerSmokeError(f"OCI label {name} is not {value!r}")
    revision = labels.get("org.opencontainers.image.revision")
    if not isinstance(revision, str) or not revision:
        raise ContainerSmokeError("OCI revision label is empty")
    if expected_revision is not None and revision != expected_revision:
        raise ContainerSmokeError(f"OCI revision is {revision!r}, expected {expected_revision!r}")

    entrypoint = config.get("Entrypoint")
    if not isinstance(entrypoint, list) or not entrypoint:
        raise ContainerSmokeError("Image has no entrypoint")
    executable = entrypoint[0]
    if not isinstance(executable, str):
        raise ContainerSmokeError("Image entrypoint is invalid")

    if kind == "bot":
        if entrypoint[1:] != ["serve"] or not executable.endswith("/bin/blokebot"):
            raise ContainerSmokeError(f"Unexpected bot entrypoint: {entrypoint}")
        version = _docker("run", "--rm", "--entrypoint", executable, image, "version")
        if version != "blokebot 0.1.0":
            raise ContainerSmokeError(f"Unexpected container version output: {version!r}")
        internal_port = "8080"
        markers = ("Sign in to BlokeBot", "Continue with Twitch", "Public leaderboard")
        accepted_statuses = frozenset({200, 503})
    else:
        if entrypoint[1:] or not executable.endswith("/bin/blokebot-site"):
            raise ContainerSmokeError(f"Unexpected site entrypoint: {entrypoint}")
        if config.get("Volumes") not in (None, {}):
            raise ContainerSmokeError("Stateless site image declares a volume")
        internal_port = "8081"
        markers = ("Own your channel tools.",)
        accepted_statuses = frozenset({200})

    container = _docker(
        "run",
        "--rm",
        "--detach",
        "--publish",
        f"127.0.0.1::{internal_port}",
        image,
    )
    try:
        mapping = _docker("port", container, f"{internal_port}/tcp")
        port = mapping.rsplit(":", 1)[-1]
        _wait_for_page(port, markers, accepted_statuses)
    finally:
        subprocess.run(
            ["docker", "rm", "--force", container],
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Inspect and HTTP-smoke a BlokeBot OCI image.")
    parser.add_argument("--image", required=True)
    parser.add_argument("--kind", required=True, choices=("bot", "site"))
    parser.add_argument("--revision")
    args = parser.parse_args(arguments)
    try:
        smoke(args.image, args.kind, args.revision)
    except ContainerSmokeError as error:
        print(f"container-smoke: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
