from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
import zipfile


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
sys.path.insert(0, str(SCRIPTS))

RELEASE_SPEC = importlib.util.spec_from_file_location(
    "release_artifacts", SCRIPTS / "release_artifacts.py"
)
assert RELEASE_SPEC is not None and RELEASE_SPEC.loader is not None
release_artifacts = importlib.util.module_from_spec(RELEASE_SPEC)
sys.modules[RELEASE_SPEC.name] = release_artifacts
RELEASE_SPEC.loader.exec_module(release_artifacts)

PACKAGE_SPEC = importlib.util.spec_from_file_location(
    "package_metadata", SCRIPTS / "package_metadata.py"
)
assert PACKAGE_SPEC is not None and PACKAGE_SPEC.loader is not None
package_metadata = importlib.util.module_from_spec(PACKAGE_SPEC)
sys.modules[PACKAGE_SPEC.name] = package_metadata
PACKAGE_SPEC.loader.exec_module(package_metadata)


class PackageMetadataTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.release = self.root / "release"
        self.release.mkdir()
        self.license = self.root / "LICENSE"
        self.guide = self.root / "SERVER_SETUP.md"
        self.license.write_text("license\n", encoding="utf-8")
        self.guide.write_text("guide\n", encoding="utf-8")

        for index, (rid, _) in enumerate(release_artifacts.RID_FORMATS):
            publish = self.root / f"publish-{index}"
            publish.mkdir()
            executable = publish / ("blokebot.exe" if rid.startswith("win-") else "blokebot")
            executable.write_bytes(f"executable-{rid}".encode())
            executable.chmod(0o755)
            (publish / "blokebot.dll").write_bytes(b"assembly")
            release_artifacts.create_archive(
                publish,
                self.release,
                rid,
                self.license,
                self.guide,
            )

        self.checksums = self.release / release_artifacts.CHECKSUM_FILE_NAME
        release_artifacts.generate_checksums(self.release, self.checksums)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def generate(self, name: str = "metadata") -> Path:
        output = self.root / name
        package_metadata.generate(self.release, self.checksums, output)
        return output

    def test_generation_is_deterministic_and_retains_exact_checksums(self) -> None:
        first = self.generate("first")
        second = self.generate("second")

        for relative in package_metadata.CHANNEL_FILES:
            self.assertEqual((first / relative).read_bytes(), (second / relative).read_bytes())
        self.assertEqual(
            (first / "release-assets/checksums.toml").read_bytes(), self.checksums.read_bytes()
        )

    def test_every_channel_uses_immutable_release_urls_and_hashes(self) -> None:
        output = self.generate()
        with self.checksums.open("rb") as source:
            manifest = package_metadata.tomllib.load(source)
        artifacts = {artifact["rid"]: artifact for artifact in manifest["artifact"]}

        formula = (output / "homebrew/Formula/blokebot.rb").read_text(encoding="utf-8")
        self.assertIn(artifacts["osx-arm64"]["name"], formula)
        self.assertIn(artifacts["osx-arm64"]["sha256"], formula)

        scoop = (output / "scoop/blokebot.json").read_text(encoding="utf-8")
        chocolatey = (output / "chocolatey/tools/chocolateyinstall.ps1").read_text(
            encoding="utf-8"
        )
        winget = (
            output
            / "winget/manifests/a/alsi-lawr/BlokeBot/0.1.0/alsi-lawr.BlokeBot.installer.yaml"
        ).read_text(encoding="utf-8")
        for rid in ("win-x64", "win-arm64"):
            for content in (scoop, chocolatey, winget):
                self.assertIn(artifacts[rid]["name"], content)
                self.assertIn(artifacts[rid]["sha256"].casefold(), content.casefold())

    def test_homebrew_is_apple_silicon_only_and_does_not_repack_archive(self) -> None:
        formula = (self.generate() / "homebrew/Formula/blokebot.rb").read_text(
            encoding="utf-8"
        )

        self.assertIn("osx-arm64.tar.gz", formula)
        self.assertIn("depends_on arch: :arm64", formula)
        self.assertIn('libexec.install Dir["blokebot/*"]', formula)
        self.assertIn('bin.install_symlink libexec/"blokebot"', formula)
        self.assertNotIn('bin.install "blokebot/blokebot"', formula)
        self.assertNotIn(".zip", formula)

    def test_scoop_exposes_lowercase_shim_for_x64_and_arm64_without_persistence(self) -> None:
        manifest = json.loads(
            (self.generate() / "scoop/blokebot.json").read_text(encoding="utf-8")
        )

        self.assertEqual(set(manifest["architecture"]), {"64bit", "arm64"})
        self.assertEqual(manifest["bin"], [["blokebot.exe", "blokebot"]])
        self.assertNotIn("persist", manifest)
        self.assertIn("blokebot help", " ".join(manifest["notes"]))

    def test_chocolatey_selects_x64_or_arm64_and_removes_only_package_files(self) -> None:
        output = self.generate()
        install = (output / "chocolatey/tools/chocolateyinstall.ps1").read_text(
            encoding="utf-8"
        )
        uninstall = (output / "chocolatey/tools/chocolateyuninstall.ps1").read_text(
            encoding="utf-8"
        )

        self.assertIn("'X64'", install)
        self.assertIn("'Arm64'", install)
        self.assertIn("Install-BinFile -Name 'blokebot'", install)
        self.assertIn("Uninstall-BinFile -Name 'blokebot'", uninstall)
        self.assertIn("$toolsDir 'install'", uninstall)
        self.assertNotIn("APPDATA", uninstall.upper())
        self.assertNotIn("USERPROFILE", uninstall.upper())

    def test_winget_uses_nested_portable_archives_and_manual_deterministic_bundle(self) -> None:
        output = self.generate()
        installer = (
            output
            / "winget/manifests/a/alsi-lawr/BlokeBot/0.1.0/alsi-lawr.BlokeBot.installer.yaml"
        ).read_text(encoding="utf-8")

        self.assertIn("NestedInstallerType: portable", installer)
        self.assertIn("RelativeFilePath: blokebot/blokebot.exe", installer)
        self.assertIn("Architecture: x64", installer)
        self.assertIn("Architecture: arm64", installer)
        with zipfile.ZipFile(output / "winget/winget-pr-v0.1.0.zip") as archive:
            self.assertEqual(archive.namelist(), sorted(archive.namelist()))
            self.assertTrue(
                all(member.date_time == package_metadata.ZIP_TIMESTAMP for member in archive.infolist())
            )
            self.assertIn("README.md", archive.namelist())

    def test_status_is_explicit_about_every_unavailable_external_channel(self) -> None:
        status = (self.generate() / "STATUS.md").read_text(encoding="utf-8")

        self.assertIn("has not been published", status)
        self.assertIn("tap repository does not yet exist", status)
        self.assertIn("bucket repository does not yet exist", status)
        self.assertIn("pending moderation", status)
        self.assertIn("No upstream pull request is created", status)

    def test_jreleaser_config_uses_the_1_25_winget_schema(self) -> None:
        config = (self.generate() / "jreleaser/jreleaser.yml").read_text(encoding="utf-8")

        self.assertIn("links:\n    homepage:", config)
        self.assertNotIn("  website:", config)
        self.assertIn("package:\n      identifier: alsi-lawr.BlokeBot", config)
        self.assertIn("publisher:\n      name: alsi-lawr", config)
        self.assertIn("supportUrl: https://github.com/alsi-lawr/BlokeBot/issues", config)
        self.assertNotIn("packageIdentifier:", config)
        self.assertIn("copyright: Copyright (c) 2026 BlokeBot contributors", config)
        self.assertIn("tags:\n    - twitch\n    - bot\n    - cli", config)
        self.assertEqual(config.count("downloadUrl: https://github.com/alsi-lawr/BlokeBot/"), 3)
        self.assertIn("skipScoop: true", config)
        self.assertIn("skipChocolatey: true", config)
        self.assertIn("skipWinget: true", config)

    def test_metadata_never_auto_starts_or_places_user_state_in_install_directory(self) -> None:
        output = self.generate()
        source = "\n".join(
            path.read_text(encoding="utf-8")
            for path in output.rglob("*")
            if path.is_file() and path.suffix != ".zip"
        )

        self.assertIn("blokebot help", source)
        self.assertIn("does not start automatically", source)
        self.assertNotIn("Start-Service", source)
        self.assertNotIn("New-Service", source)
        self.assertNotIn("systemctl enable", source)

    def test_tampered_release_asset_is_rejected_before_generation(self) -> None:
        archive = self.release / release_artifacts.archive_name("win-x64")
        archive.write_bytes(archive.read_bytes() + b"tampered")

        with self.assertRaisesRegex(package_metadata.PackageMetadataError, "hash mismatch"):
            self.generate()

    def test_existing_output_is_never_overwritten(self) -> None:
        output = self.generate()
        status = (output / "STATUS.md").read_bytes()

        with self.assertRaisesRegex(package_metadata.PackageMetadataError, "overwrite"):
            package_metadata.generate(self.release, self.checksums, output)
        self.assertEqual((output / "STATUS.md").read_bytes(), status)

    def test_validation_rejects_modified_generated_metadata(self) -> None:
        output = self.generate()
        formula = output / "homebrew/Formula/blokebot.rb"
        formula.write_text(formula.read_text(encoding="utf-8").replace("sha256", "sha257"))

        with self.assertRaises(package_metadata.PackageMetadataError):
            package_metadata.validate(output, self.release, self.checksums)

    def test_generation_failure_leaves_no_partial_output(self) -> None:
        bad_checksums = self.root / "bad-checksums.toml"
        shutil.copyfile(self.checksums, bad_checksums)
        bad_checksums.write_text(
            bad_checksums.read_text(encoding="utf-8").replace("win-arm64", "win-other"),
            encoding="utf-8",
        )
        output = self.root / "failed"

        with self.assertRaises(package_metadata.PackageMetadataError):
            package_metadata.generate(self.release, bad_checksums, output)
        self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
