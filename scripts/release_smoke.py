#!/usr/bin/env python3

from __future__ import annotations

import argparse
import os
from pathlib import Path
import queue
import re
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request


VERSION_OUTPUT = "blokebot 0.1.0"
LOGIN_MARKERS = ("Sign in to BlokeBot", "Continue with Twitch", "Public leaderboard")
ACCEPTED_LOGIN_STATUSES = frozenset({200, 503})
LISTENING_URL = re.compile(r"http://127\.0\.0\.1:(\d+)")


class SmokeError(RuntimeError):
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
        raise SmokeError(
            f"{command} failed with {completed.returncode}: {completed.stderr.strip()}"
        )
    return completed.stdout


def _capture_lines(process: subprocess.Popen[str], lines: queue.Queue[str]) -> None:
    assert process.stdout is not None
    for line in process.stdout:
        lines.put(line)


def _wait_for_url(process: subprocess.Popen[str], lines: queue.Queue[str], timeout: float) -> str:
    deadline = time.monotonic() + timeout
    output: list[str] = []
    while time.monotonic() < deadline:
        if process.poll() is not None:
            while not lines.empty():
                output.append(lines.get_nowait())
            raise SmokeError(
                f"serve exited with {process.returncode} before binding:\n{''.join(output)}"
            )
        try:
            line = lines.get(timeout=0.2)
        except queue.Empty:
            continue
        output.append(line)
        match = LISTENING_URL.search(line)
        if match:
            return f"http://127.0.0.1:{match.group(1)}/"
    raise SmokeError(f"serve did not report an ephemeral local URL:\n{''.join(output)}")


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
        raise SmokeError(f"Unexpected HTTP status {status} from {url}")
    return body.decode("utf-8")


def _wait_for_login_surface(url: str, timeout: float) -> None:
    deadline = time.monotonic() + timeout
    last_error: Exception | None = None
    while time.monotonic() < deadline:
        try:
            body = _read_http_body(url, ACCEPTED_LOGIN_STATUSES)
            missing = [marker for marker in LOGIN_MARKERS if marker not in body]
            if missing:
                raise SmokeError(f"Login surface is missing: {', '.join(missing)}")
            return
        except (OSError, urllib.error.URLError) as error:
            last_error = error
            time.sleep(0.2)
    raise SmokeError(f"Login surface did not become available: {last_error}")


def smoke(executable: Path) -> None:
    if not executable.is_file():
        raise SmokeError(f"Published executable does not exist: {executable}")
    version = _run_cli(executable, "version").strip()
    if version != VERSION_OUTPUT:
        raise SmokeError(f"Unexpected version output: {version!r}")
    help_output = _run_cli(executable, "help")
    for marker in ("Usage:", "Required Twitch configuration", "Server Owner Guide"):
        if marker not in help_output:
            raise SmokeError(f"Help output is missing {marker!r}")

    with tempfile.TemporaryDirectory(prefix="blokebot-release-smoke-") as data_directory:
        process = subprocess.Popen(
            [
                str(executable),
                "serve",
                "--data-dir",
                data_directory,
                "--urls",
                "http://127.0.0.1:0",
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            env=_offline_environment(),
            bufsize=1,
        )
        lines: queue.Queue[str] = queue.Queue()
        reader = threading.Thread(target=_capture_lines, args=(process, lines), daemon=True)
        reader.start()
        try:
            url = _wait_for_url(process, lines, 30)
            _wait_for_login_surface(url, 30)
        finally:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Smoke a native BlokeBot release publish.")
    parser.add_argument("executable", type=Path)
    args = parser.parse_args(arguments)
    try:
        smoke(args.executable)
    except SmokeError as error:
        print(f"release-smoke: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
