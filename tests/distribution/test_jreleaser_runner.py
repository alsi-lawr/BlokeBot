from __future__ import annotations

import hashlib
import importlib.util
from pathlib import Path
import stat
from unittest.mock import patch
import tempfile
import unittest
import zipfile


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "run_jreleaser", ROOT / "scripts" / "run_jreleaser.py"
)
assert SPEC is not None and SPEC.loader is not None
run_jreleaser = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(run_jreleaser)


class JReleaserRunnerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def create_archive(self, entries: dict[str, bytes]) -> tuple[Path, str]:
        archive = self.root / "jreleaser.zip"
        with zipfile.ZipFile(archive, "w") as output:
            for name, content in entries.items():
                info = zipfile.ZipInfo(name)
                info.create_system = 3
                info.external_attr = (stat.S_IFREG | 0o755) << 16
                output.writestr(info, content)
        return archive, hashlib.sha256(archive.read_bytes()).hexdigest()

    def test_version_url_and_hash_are_immutably_pinned(self) -> None:
        self.assertEqual(run_jreleaser.JRELEASER_VERSION, "1.25.0")
        self.assertEqual(
            run_jreleaser.JRELEASER_SHA256,
            "7c086a384e509ae30ad12ce2f10946601c0798e746d06a5538afc267e398644b",
        )
        self.assertIn("jreleaser-1.25.0.zip", run_jreleaser.JRELEASER_URL)

    def test_verified_archive_installs_exact_launcher(self) -> None:
        launcher_name = "jreleaser.bat" if run_jreleaser.os.name == "nt" else "jreleaser"
        archive, digest = self.create_archive(
            {f"jreleaser-1.25.0/bin/{launcher_name}": b"launcher"}
        )

        launcher = run_jreleaser.install_archive(archive, self.root / "install", digest)

        self.assertEqual(launcher.name, launcher_name)
        self.assertEqual(launcher.read_bytes(), b"launcher")
        self.assertEqual(
            run_jreleaser.install_archive(archive, self.root / "install", digest), launcher
        )

    def test_hash_mismatch_is_rejected(self) -> None:
        archive, _ = self.create_archive({"jreleaser-1.25.0/bin/jreleaser": b"launcher"})
        with self.assertRaisesRegex(run_jreleaser.JReleaserInstallError, "hash mismatch"):
            run_jreleaser.verify_archive(archive, "0" * 64)

    def test_unsafe_or_duplicate_entries_are_rejected(self) -> None:
        archive, digest = self.create_archive(
            {
                "../escape": b"unsafe",
                "jreleaser-1.25.0/bin/jreleaser": b"launcher",
            }
        )
        with self.assertRaisesRegex(run_jreleaser.JReleaserInstallError, "Unsafe"):
            run_jreleaser.install_archive(archive, self.root / "unsafe", digest)

    def test_argument_separator_is_not_forwarded_to_jreleaser(self) -> None:
        launcher = self.root / "jreleaser"
        launcher.write_bytes(b"launcher")
        completed = run_jreleaser.subprocess.CompletedProcess([], 0)

        with (
            patch.object(run_jreleaser, "download_archive", return_value=self.root / "archive"),
            patch.object(run_jreleaser, "install_archive", return_value=launcher),
            patch.object(run_jreleaser.subprocess, "run", return_value=completed) as run,
        ):
            result = run_jreleaser.main(
                ["--cache-dir", str(self.root / "cache"), "--", "--version"]
            )

        self.assertEqual(result, 0)
        run.assert_called_once_with([str(launcher), "--version"], check=False)


if __name__ == "__main__":
    unittest.main()
