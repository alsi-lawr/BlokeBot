#!/usr/bin/env python3

from __future__ import annotations

import argparse
import json
from pathlib import PurePosixPath
import subprocess
import sys
import time
import urllib.error
import urllib.request


class ContainerSmokeError(RuntimeError):
    pass


def _docker(*arguments: str) -> str:
    completed = subprocess.run(
        ["docker", *arguments],
        check=False,
        capture_output=True,
        text=True,
    )
    if completed.returncode != 0:
        raise ContainerSmokeError(f"docker {' '.join(arguments)} failed")
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
    path: str,
    markers: tuple[str, ...],
    accepted_statuses: frozenset[int],
) -> None:
    url = f"http://127.0.0.1:{port}{path}"
    deadline = time.monotonic() + 30
    last_error: Exception | None = None
    while time.monotonic() < deadline:
        try:
            body = _read_http_body(url, accepted_statuses)
            missing = [marker for marker in markers if marker not in body]
            if missing:
                raise ContainerSmokeError(f"{url} is missing: {', '.join(missing)}")
            return
        except (OSError, urllib.error.URLError, ContainerSmokeError) as error:
            last_error = error
            time.sleep(0.25)
    raise ContainerSmokeError(f"Container page did not become ready: {last_error}")


def _assert_bot_framework_script(port: str) -> None:
    url = f"http://127.0.0.1:{port}/_framework/blazor.web.js"
    try:
        response = urllib.request.urlopen(url, timeout=2)
    except urllib.error.HTTPError as error:
        with error:
            status = error.code
            content_type = error.headers.get_content_type()
            body = error.read()
    else:
        with response:
            status = response.status
            content_type = response.headers.get_content_type()
            body = response.read()

    if status != 200:
        raise ContainerSmokeError(f"Unexpected HTTP status {status} from {url}")
    if content_type not in {"application/javascript", "text/javascript"}:
        raise ContainerSmokeError(f"Unexpected content type {content_type!r} from {url}")
    if body.startswith(b"<!DOCTYPE html"):
        raise ContainerSmokeError(f"{url} returned HTML instead of the Blazor framework script")


def _run_worker_probe(image: str, executable: str) -> None:
    package_root = PurePosixPath(executable).parent.parent
    worker = package_root / "lib" / "blokebot" / "plugin-worker" / "BlokeBot.PluginWorker"
    _docker(
        "run",
        "--rm",
        "--entrypoint",
        str(worker),
        image,
        "--deployment-probe",
        "/tmp/blokebot-worker-probe",
    )


def smoke(image: str, kind: str, version: str, cli_version: str, revision: str) -> None:
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
        "org.opencontainers.image.version": version,
        "org.opencontainers.image.revision": revision,
    }
    for name, value in expected_labels.items():
        if labels.get(name) != value:
            raise ContainerSmokeError(f"OCI label {name} is not {value!r}")

    entrypoint = config.get("Entrypoint")
    if not isinstance(entrypoint, list) or not entrypoint:
        raise ContainerSmokeError("Image has no entrypoint")
    executable = entrypoint[0]
    if not isinstance(executable, str):
        raise ContainerSmokeError("Image entrypoint is invalid")

    if kind == "bot":
        expected_arguments = [
            "serve",
            "--host",
            "0.0.0.0",
            "--port",
            "8080",
            "--data-dir",
            "/data",
        ]
        if entrypoint[1:] != expected_arguments or not executable.endswith("/bin/blokebot"):
            raise ContainerSmokeError(f"Unexpected bot entrypoint: {entrypoint}")
        actual_version = _docker("run", "--rm", "--entrypoint", executable, image, "version")
        if actual_version != f"blokebot {cli_version}":
            raise ContainerSmokeError(f"Unexpected container version output: {actual_version!r}")
        _run_worker_probe(image, executable)
        internal_port = "8080"
        path = "/auth/login"
        markers = (
            "Twitch connection unavailable",
            "This Twitch connection is not available yet.",
            "An administrator needs to check the connection settings.",
        )
        accepted_statuses = frozenset({503})
    else:
        if entrypoint[1:] or not executable.endswith("/bin/blokebot-site"):
            raise ContainerSmokeError(f"Unexpected site entrypoint: {entrypoint}")
        if config.get("Volumes") not in (None, {}):
            raise ContainerSmokeError("Stateless site image declares a volume")
        internal_port = "8081"
        path = "/"
        markers = ("Your channel. Your bot. Your rules.",)
        accepted_statuses = frozenset({200})

    run_arguments = ["run", "--rm", "--detach"]
    if kind == "site":
        # Online site startup requires explicit privacy configuration; the smoke run supplies
        # throwaway values the way a real deployment supplies its own.
        run_arguments += [
            "--env",
            "BlokeBotSite__ControllerName=BlokeBot (container smoke)",
            "--env",
            "BlokeBotSite__PrivacyContact=privacy@smoke.invalid",
            "--env",
            "BlokeBotSite__PrivacyNoticeUrl=https://smoke.invalid/privacy",
        ]
    container = _docker(
        *run_arguments,
        "--publish",
        f"127.0.0.1::{internal_port}",
        image,
    )
    try:
        mapping = _docker("port", container, f"{internal_port}/tcp")
        port = mapping.rsplit(":", 1)[-1]
        _wait_for_page(port, path, markers, accepted_statuses)
        if kind == "bot":
            _assert_bot_framework_script(port)
    finally:
        subprocess.run(
            ["docker", "rm", "--force", container],
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Inspect and HTTP-smoke a Nix OCI image.")
    parser.add_argument("--image", required=True)
    parser.add_argument("--kind", required=True, choices=("bot", "site"))
    parser.add_argument("--version", required=True)
    parser.add_argument("--cli-version")
    parser.add_argument("--revision", required=True)
    args = parser.parse_args(arguments)
    try:
        smoke(
            args.image,
            args.kind,
            args.version,
            args.cli_version or args.version,
            args.revision,
        )
    except ContainerSmokeError as error:
        print(f"container-smoke: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
