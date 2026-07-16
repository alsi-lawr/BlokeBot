from __future__ import annotations

import importlib.util
from pathlib import Path
import shutil
import sys
import tarfile
import tempfile
import unittest
import zipfile


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "release_artifacts", ROOT / "scripts" / "release_artifacts.py"
)
assert SPEC is not None and SPEC.loader is not None
release_artifacts = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = release_artifacts
SPEC.loader.exec_module(release_artifacts)


class ReleaseArtifactsTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.license = self.root / "LICENSE"
        self.guide = self.root / "SERVER_SETUP.md"
        self.license.write_text("licence\n", encoding="utf-8")
        self.guide.write_text("setup\n", encoding="utf-8")

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def publish_directory(self, rid: str, name: str = "publish") -> Path:
        publish = self.root / name
        publish.mkdir()
        executable = publish / ("blokebot.exe" if rid.startswith("win-") else "blokebot")
        executable.write_bytes(b"executable")
        executable.chmod(0o755)
        (publish / "blokebot.dll").write_bytes(b"assembly")
        (publish / "appsettings.json").write_text("{}\n", encoding="utf-8")
        (publish / "wwwroot").mkdir()
        (publish / "wwwroot" / "app.css").write_text("body{}\n", encoding="utf-8")
        return publish

    def create_archive(self, rid: str, output_name: str) -> Path:
        return release_artifacts.create_archive(
            self.publish_directory(rid, f"publish-{output_name}"),
            self.root / output_name,
            rid,
            self.license,
            self.guide,
        )

    def test_tar_archive_is_deterministic_and_normalized(self) -> None:
        first = self.create_archive("linux-x64", "first")
        second = self.create_archive("linux-x64", "second")

        self.assertEqual(first.read_bytes(), second.read_bytes())
        with tarfile.open(first, "r:gz") as archive:
            members = archive.getmembers()
            names = [member.name for member in members]
            self.assertEqual(names[0:2], ["blokebot", "blokebot/wwwroot"])
            self.assertEqual(names[2:], sorted(names[2:]))
            self.assertIn("blokebot/LICENSE", names)
            self.assertIn("blokebot/SERVER_SETUP.md", names)
            for member in members:
                self.assertEqual(member.mtime, 0)
                self.assertEqual(member.uid, 0)
                self.assertEqual(member.gid, 0)
            executable = next(member for member in members if member.name == "blokebot/blokebot")
            self.assertEqual(executable.mode, 0o755)

    def test_zip_archive_is_deterministic_and_normalized(self) -> None:
        first = self.create_archive("win-arm64", "first")
        second = self.create_archive("win-arm64", "second")

        self.assertEqual(first.read_bytes(), second.read_bytes())
        with zipfile.ZipFile(first) as archive:
            names = archive.namelist()
            self.assertIn("blokebot/LICENSE", names)
            self.assertIn("blokebot/SERVER_SETUP.md", names)
            self.assertIn("blokebot/blokebot.exe", names)
            self.assertTrue(all(member.date_time == release_artifacts.ZIP_TIMESTAMP for member in archive.infolist()))

    def test_prohibited_publish_content_is_rejected(self) -> None:
        prohibited = (
            "appsettings.Development.json",
            "package-lock.json",
            "blokebot.db",
            "twitch.tokens.json",
            "BlokeBot.Site.dll",
            "blokebot.pdb",
        )
        for index, name in enumerate(prohibited):
            with self.subTest(name=name):
                publish = self.publish_directory("linux-x64", f"publish-{index}")
                (publish / name).write_bytes(b"prohibited")
                with self.assertRaises(release_artifacts.ReleaseArtifactError):
                    release_artifacts.create_archive(
                        publish,
                        self.root / f"output-{index}",
                        "linux-x64",
                        self.license,
                        self.guide,
                    )

    def test_prohibited_and_symlink_directories_are_rejected(self) -> None:
        publish = self.publish_directory("linux-x64", "prohibited-directory")
        (publish / "node_modules").mkdir()
        with self.assertRaises(release_artifacts.ReleaseArtifactError):
            release_artifacts.collect_entries(publish, "linux-x64", self.license, self.guide)

        shutil.rmtree(publish / "node_modules")
        (publish / "linked").symlink_to(publish / "wwwroot", target_is_directory=True)
        with self.assertRaises(release_artifacts.ReleaseArtifactError):
            release_artifacts.collect_entries(publish, "linux-x64", self.license, self.guide)

    def test_case_insensitive_duplicate_archive_paths_are_rejected(self) -> None:
        publish = self.publish_directory("linux-x64", "duplicate")
        (publish / "Data").mkdir()
        (publish / "data").mkdir()
        (publish / "Data" / "value.dll").write_bytes(b"first")
        (publish / "data" / "value.dll").write_bytes(b"second")

        with self.assertRaisesRegex(release_artifacts.ReleaseArtifactError, "Duplicate"):
            release_artifacts.collect_entries(publish, "linux-x64", self.license, self.guide)

    def test_existing_archive_is_never_overwritten(self) -> None:
        publish = self.publish_directory("linux-x64", "overwrite")
        output = self.root / "output"
        first = release_artifacts.create_archive(
            publish, output, "linux-x64", self.license, self.guide
        )
        original = first.read_bytes()

        with self.assertRaisesRegex(release_artifacts.ReleaseArtifactError, "overwrite"):
            release_artifacts.create_archive(
                publish, output, "linux-x64", self.license, self.guide
            )
        self.assertEqual(first.read_bytes(), original)

    def test_checksums_require_exact_set_and_detect_tampering(self) -> None:
        release_directory = self.root / "release"
        for index, (rid, _) in enumerate(release_artifacts.RID_FORMATS):
            archive = self.create_archive(rid, f"archive-{index}")
            release_directory.mkdir(exist_ok=True)
            shutil.copyfile(archive, release_directory / archive.name)

        manifest = release_directory / release_artifacts.CHECKSUM_FILE_NAME
        release_artifacts.generate_checksums(release_directory, manifest)
        first_manifest = manifest.read_bytes()
        release_artifacts.verify_checksums(release_directory, manifest)

        copied_directory = self.root / "release-copy"
        shutil.copytree(release_directory, copied_directory)
        copied_manifest = copied_directory / release_artifacts.CHECKSUM_FILE_NAME
        copied_manifest.unlink()
        release_artifacts.generate_checksums(copied_directory, copied_manifest)
        self.assertEqual(first_manifest, copied_manifest.read_bytes())

        tampered = release_directory / release_artifacts.archive_name("linux-x64")
        tampered.write_bytes(tampered.read_bytes() + b"tampered")
        with self.assertRaisesRegex(release_artifacts.ReleaseArtifactError, "hash mismatch"):
            release_artifacts.verify_checksums(release_directory, manifest)

    def test_checksums_reject_missing_unexpected_and_overwrite(self) -> None:
        release_directory = self.root / "incomplete"
        release_directory.mkdir()
        with self.assertRaisesRegex(release_artifacts.ReleaseArtifactError, "incomplete"):
            release_artifacts.generate_checksums(
                release_directory, release_directory / release_artifacts.CHECKSUM_FILE_NAME
            )

        for index, (rid, _) in enumerate(release_artifacts.RID_FORMATS):
            archive = self.create_archive(rid, f"complete-{index}")
            shutil.copyfile(archive, release_directory / archive.name)
        (release_directory / "unexpected.txt").write_text("unexpected", encoding="utf-8")
        with self.assertRaisesRegex(release_artifacts.ReleaseArtifactError, "Unexpected"):
            release_artifacts.generate_checksums(
                release_directory, release_directory / release_artifacts.CHECKSUM_FILE_NAME
            )

        (release_directory / "unexpected.txt").unlink()
        manifest = release_directory / release_artifacts.CHECKSUM_FILE_NAME
        manifest.write_text("existing\n", encoding="utf-8")
        with self.assertRaisesRegex(release_artifacts.ReleaseArtifactError, "overwrite"):
            release_artifacts.generate_checksums(release_directory, manifest)


if __name__ == "__main__":
    unittest.main()
