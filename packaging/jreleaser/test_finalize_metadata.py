import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest
import zipfile

FINALIZER = Path(__file__).with_name("finalize_metadata.sh")
RIDS = ("linux-x64", "linux-arm64", "osx-arm64", "win-x64", "win-arm64")


class FinalizeMetadataTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.release = self.root / "release"
        self.metadata = self.root / "metadata"
        self.release.mkdir()
        self.metadata.mkdir()

    def write_archives(self, version: str, *, worker_mode: int = 0o755, unsafe_windows: bool = False) -> None:
        for rid in RIDS:
            root = f"blokebot-v{version}-{rid}"
            if rid.startswith("linux"):
                with tarfile.open(self.release / f"{root}.tar.gz", "w:gz") as archive:
                    member = tarfile.TarInfo(f"{root}/bin/plugin-worker/BlokeBot.PluginWorker")
                    member.size = 6
                    member.mode = worker_mode
                    archive.addfile(member, io.BytesIO(b"worker"))
            else:
                with zipfile.ZipFile(self.release / f"{root}.zip", "w") as archive:
                    if rid == "osx-arm64":
                        member = zipfile.ZipInfo(f"{root}/bin/plugin-worker/BlokeBot.PluginWorker")
                        member.create_system = 3
                        member.external_attr = (0o100000 | worker_mode) << 16
                        archive.writestr(member, b"worker")
                    else:
                        archive.writestr(f"{root}/bin/blokebot.exe", b"application")
                        if unsafe_windows:
                            archive.writestr(f"{root}/../escape", b"unsafe")

    def write_metadata(self, version: str) -> None:
        scoop = self.metadata / "blokebot/scoop/bucket/blokebot.json"
        scoop.parent.mkdir(parents=True, exist_ok=True)
        scoop.write_text(json.dumps({"version": version}))
        winget = self.metadata / f"blokebot/winget/manifests/a/alsi-lawr/BlokeBot/{version}"
        winget.mkdir(parents=True)
        for suffix, kind in (("installer", "installer"), ("locale.en-US", "defaultLocale"), ("", "version")):
            name = "alsi-lawr.BlokeBot" + ("." + suffix if suffix else "") + ".yaml"
            (winget / name).write_text(f"PackageIdentifier: alsi-lawr.BlokeBot\nPackageVersion: {version}\nManifestType: {kind}\n")

    def run_finalizer(self, version: str, **environment: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(["bash", str(FINALIZER), "--version", version,
                               "--release-directory", str(self.release), "--metadata-directory", str(self.metadata)],
                              env=dict(os.environ, **environment), capture_output=True, text=True)

    def test_stable_and_candidate_metadata_bind_each_architecture_to_its_exact_archive(self) -> None:
        for version in ("0.18.0", "0.18.0-rc.1"):
            with self.subTest(version=version):
                self.write_archives(version)
                self.write_metadata(version)
                result = self.run_finalizer(version)
                self.assertEqual(result.returncode, 0, result.stderr)
                scoop = json.loads((self.metadata / "blokebot/scoop/bucket/blokebot.json").read_text())
                self.assertEqual(scoop["version"], version)
                winget = (self.metadata / f"blokebot/winget/manifests/a/alsi-lawr/BlokeBot/{version}/alsi-lawr.BlokeBot.installer.yaml").read_text()
                for rid, architecture in (("win-x64", "64bit"), ("win-arm64", "arm64")):
                    name = f"blokebot-v{version}-{rid}"
                    digest = hashlib.sha256((self.release / f"{name}.zip").read_bytes()).hexdigest()
                    entry = scoop["architecture"][architecture]
                    self.assertEqual(entry["hash"], "sha256:" + digest)
                    self.assertEqual(entry["extract_dir"], name)
                    self.assertTrue(entry["url"].endswith(f"/v{version}/{name}.zip"))
                    self.assertIn(digest, winget)
                    self.assertIn(f"{name}\\bin\\blokebot.exe", winget)
                (self.metadata / "blokebot/scoop/bucket/blokebot.json").unlink()

    def test_disabled_metadata_publication_does_not_disable_five_rid_or_worker_checks(self) -> None:
        version = "0.18.0-rc.1"
        self.write_archives(version)
        missing = self.release / f"blokebot-v{version}-win-arm64.zip"
        missing.unlink()
        self.assertNotEqual(self.run_finalizer(version, PUBLISH_SCOOP="false", PUBLISH_WINGET="false").returncode, 0)
        self.write_archives(version, worker_mode=0o644)
        self.assertNotEqual(self.run_finalizer(version, PUBLISH_SCOOP="false", PUBLISH_WINGET="false").returncode, 0)

    def test_candidate_windows_traversal_is_rejected_before_metadata_is_rewritten(self) -> None:
        version = "0.18.0-rc.1"
        self.write_archives(version, unsafe_windows=True)
        self.write_metadata(version)
        scoop = self.metadata / "blokebot/scoop/bucket/blokebot.json"
        before = scoop.read_bytes()
        self.assertNotEqual(self.run_finalizer(version).returncode, 0)
        self.assertEqual(scoop.read_bytes(), before)


if __name__ == "__main__":
    unittest.main()
