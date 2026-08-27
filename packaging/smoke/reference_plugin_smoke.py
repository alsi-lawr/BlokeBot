#!/usr/bin/env python3

from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess
import sys


class ReferencePluginSmokeError(RuntimeError):
    pass


def _require_file(path: Path, purpose: str) -> None:
    if not path.is_file():
        raise ReferencePluginSmokeError(f"{purpose} was not found: {path}")


def _run(arguments: list[str], environment: dict[str, str]) -> None:
    completed = subprocess.run(arguments, check=False, env=environment)
    if completed.returncode != 0:
        raise ReferencePluginSmokeError(
            f"{' '.join(arguments)} failed with exit code {completed.returncode}"
        )


def smoke(package: Path, configuration: str) -> None:
    package = package.resolve()
    _require_file(package / "plugin.toml", "Reference package manifest")
    _require_file(package / "tests.toml", "Reference package scenarios")
    _require_file(package.parent.parent / "catalog.json", "Reference plugin catalogue")

    repository = Path(__file__).resolve().parents[2]
    harness = (
        repository
        / "tools"
        / "BlokeBot.PluginHarness"
        / "bin"
        / configuration
        / "net10.0"
        / "BlokeBot.PluginHarness.dll"
    )
    worker = harness.parent / "plugin-worker" / "BlokeBot.PluginWorker.dll"
    core_tests = repository / "tests" / "BlokeBot.Core.Tests" / "BlokeBot.Core.Tests.csproj"
    _require_file(harness, f"{configuration} PluginHarness build")
    _require_file(worker, f"{configuration} PluginWorker build")
    _require_file(core_tests, "Core lifecycle test project")

    environment = os.environ.copy()
    environment["DOTNET_PROCESSOR_COUNT"] = "2"
    environment["BLOKEBOT_REFERENCE_PLUGIN_PATH"] = str(package)
    environment["BLOKEBOT_PLUGIN_WORKER_PATH"] = str(worker)
    _run(["dotnet", str(harness), "validate", str(package)], environment)
    _run(["dotnet", str(harness), "test", str(package)], environment)
    _run(
        [
            "dotnet",
            "test",
            str(core_tests),
            "-c",
            configuration,
            "--no-build",
            "--",
            "--treenode-filter",
            "/*/*/ReferencePluginLifecycleSmokeTests/ExactLocalPackage_ComposesTheReferenceLifecycleWithoutExternalCalls",
            "--maximum-parallel-tests",
            "1",
        ],
        environment,
    )


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Run the deterministic author-harness and explicit lifecycle smoke against one "
            "exact local community-link-queue package. Build the PluginHarness and Core.Tests "
            "first; this command never calls Twitch or the configured metadata endpoint."
        )
    )
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--configuration", default="Release", choices=("Debug", "Release"))
    args = parser.parse_args(arguments)
    try:
        smoke(args.package, args.configuration)
    except ReferencePluginSmokeError as error:
        print(f"reference-plugin-smoke: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
