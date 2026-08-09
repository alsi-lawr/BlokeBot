#!/usr/bin/env python3

from __future__ import annotations

import argparse
from collections.abc import Callable
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request


OFFLINE_AUTH_MARKERS = (
    "Twitch connection unavailable",
    "This Twitch connection is not available yet.",
    "An administrator needs to check the connection settings.",
)
OFFLINE_AUTH_STATUS = 503
WINDOWS_SHARING_VIOLATION = 32
CLEANUP_RETRY_INTERVAL_SECONDS = 0.1
CLEANUP_RETRY_TIMEOUT_SECONDS = 5


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
    if status != OFFLINE_AUTH_STATUS:
        raise NativeSmokeError(f"Unexpected HTTP status {status} from {url}")
    return body.decode("utf-8")


def _wait_for_offline_auth_surface(process: subprocess.Popen[bytes], url: str) -> None:
    deadline = time.monotonic() + 30
    last_error: Exception | None = None
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise NativeSmokeError(
                f"serve exited with {process.returncode} before the offline auth surface was ready"
            )
        try:
            body = _read_http_body(url)
            missing = [marker for marker in OFFLINE_AUTH_MARKERS if marker not in body]
            if missing:
                raise NativeSmokeError(f"Offline auth surface is missing: {', '.join(missing)}")
            return
        except (OSError, urllib.error.URLError, NativeSmokeError) as error:
            last_error = error
            time.sleep(0.25)
    raise NativeSmokeError(f"Offline auth surface did not become ready: {last_error}")


def _remove_data_directory(
    data_directory: Path,
    *,
    remove: Callable[[Path], None] = shutil.rmtree,
    monotonic: Callable[[], float] = time.monotonic,
    sleep: Callable[[float], None] = time.sleep,
) -> None:
    deadline: float | None = None
    last_sharing_violation: PermissionError | None = None
    while True:
        if deadline is not None and monotonic() >= deadline:
            raise last_sharing_violation
        try:
            remove(data_directory)
            return
        except PermissionError as error:
            if getattr(error, "winerror", None) != WINDOWS_SHARING_VIOLATION:
                raise
            now = monotonic()
            if deadline is None:
                deadline = now + CLEANUP_RETRY_TIMEOUT_SECONDS
            remaining = deadline - now
            if remaining <= 0:
                raise
            last_sharing_violation = error
            sleep(min(CLEANUP_RETRY_INTERVAL_SECONDS, remaining))


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
    data_directory = Path(tempfile.mkdtemp(prefix="blokebot-native-smoke-"))
    process: subprocess.Popen[bytes] | None = None
    process_has_exited = False
    try:
        process = subprocess.Popen(
            [
                str(executable),
                "serve",
                "--host",
                "127.0.0.1",
                "--port",
                str(port),
                "--data-dir",
                str(data_directory),
            ],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            env=_offline_environment(),
        )
        try:
            _wait_for_offline_auth_surface(process, f"http://127.0.0.1:{port}/auth/login")
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
            process_has_exited = True
    finally:
        if process is None or process_has_exited:
            _remove_data_directory(data_directory)


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
