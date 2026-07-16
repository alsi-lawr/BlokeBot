#!/usr/bin/env python3

from __future__ import annotations

import argparse
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request


LOGIN_MARKERS = ("Sign in to BlokeBot", "Continue with Twitch", "Public leaderboard")
ACCEPTED_LOGIN_STATUSES = frozenset({200, 503})


class NativeSmokeError(RuntimeError):
    pass


def _offline_environment() -> dict[str, str]:
    environment = {
        key: value
        for key, value in os.environ.items()
        if not key.casefold().startswith(("twitchbot__", "twitchwebauth__"))
    }
    environment.pop("BlokeBot__DatabasePath", None)
    return environment


def _run_cli(executable: Path, command: str) -> str:
    completed = subprocess.run(
        [str(executable), command],
        check=False,
        capture_output=True,
        text=True,
        env=_offline_environment(),
    )
    if completed.returncode != 0:
        raise NativeSmokeError(f"{command} failed with exit code {completed.returncode}")
    return completed.stdout


def _available_port() -> int:
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]


def _read_http_body(url: str) -> str:
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
    if status not in ACCEPTED_LOGIN_STATUSES:
        raise NativeSmokeError(f"Unexpected HTTP status {status} from {url}")
    return body.decode("utf-8")


def _wait_for_login_surface(process: subprocess.Popen[bytes], url: str) -> None:
    deadline = time.monotonic() + 30
    last_error: Exception | None = None
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise NativeSmokeError(
                f"serve exited with {process.returncode} before the login surface was ready"
            )
        try:
            body = _read_http_body(url)
            missing = [marker for marker in LOGIN_MARKERS if marker not in body]
            if missing:
                raise NativeSmokeError(f"Login surface is missing: {', '.join(missing)}")
            return
        except (OSError, urllib.error.URLError, NativeSmokeError) as error:
            last_error = error
            time.sleep(0.25)
    raise NativeSmokeError(f"Login surface did not become ready: {last_error}")


def smoke(executable: Path, version: str) -> None:
    if not executable.is_file():
        raise NativeSmokeError(f"Published executable does not exist: {executable}")
    actual_version = _run_cli(executable, "version").strip()
    expected_version = f"blokebot {version}"
    if actual_version != expected_version:
        raise NativeSmokeError(
            f"Unexpected version output: {actual_version!r}; expected {expected_version!r}"
        )
    help_output = _run_cli(executable, "help")
    for marker in ("Usage:", "Required Twitch configuration", "Server Owner Guide"):
        if marker not in help_output:
            raise NativeSmokeError(f"Help output is missing {marker!r}")

    port = _available_port()
    with tempfile.TemporaryDirectory(prefix="blokebot-native-smoke-") as data_directory:
        process = subprocess.Popen(
            [
                str(executable),
                "serve",
                "--host",
                "127.0.0.1",
                "--port",
                str(port),
                "--data-dir",
                data_directory,
            ],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            env=_offline_environment(),
        )
        try:
            _wait_for_login_surface(process, f"http://127.0.0.1:{port}/")
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Smoke a native BlokeBot publish directory.")
    parser.add_argument("executable", type=Path)
    parser.add_argument("--version", required=True)
    args = parser.parse_args(arguments)
    try:
        smoke(args.executable, args.version)
    except NativeSmokeError as error:
        print(f"native-smoke: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
