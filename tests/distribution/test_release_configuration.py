from __future__ import annotations

from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github" / "workflows" / "release-v0.1.0.yml"


class ReleaseConfigurationTests(unittest.TestCase):
    def test_actions_are_full_sha_pinned_with_version_comments(self) -> None:
        workflow = WORKFLOW.read_text(encoding="utf-8")
        expected = {
            "actions/checkout": ("df4cb1c069e1874edd31b4311f1884172cec0e10", "v6"),
            "actions/setup-dotnet": ("26b0ec14cb23fa6904739307f278c14f94c95bf1", "v5"),
            "actions/upload-artifact": ("ea165f8d65b6e75b540449e92b4886f43607fa02", "v4"),
            "actions/download-artifact": ("d3f86a106a0bac45b974a628896c90dbdf5c8093", "v4"),
            "actions/attest": ("a1948c3f048ba23858d222213b7c278aabede763", "v4"),
            "cachix/install-nix-action": (
                "630ae543ea3a38a9a4166f03376c02c50f408342",
                "v31",
            ),
        }
        uses = re.findall(r"uses:\s*([^@\s]+)@([^\s]+)\s+#\s+(v[^\s]+)", workflow)
        self.assertTrue(uses)
        for action, sha, version in uses:
            self.assertRegex(sha, r"^[0-9a-f]{40}$")
            self.assertIn(action, expected)
            self.assertEqual((sha, version), expected[action])
        self.assertEqual({action for action, _, _ in uses}, set(expected))

    def test_workflow_encodes_exact_native_release_matrix_and_immutable_gates(self) -> None:
        workflow = WORKFLOW.read_text(encoding="utf-8")
        for runner in (
            "ubuntu-24.04",
            "ubuntu-24.04-arm",
            "macos-15",
            "windows-2025",
            "windows-11-arm",
        ):
            self.assertIn(f"runner: {runner}", workflow)
        for rid in ("linux-x64", "linux-arm64", "osx-arm64", "win-x64", "win-arm64"):
            self.assertIn(f"rid: {rid}", workflow)
        self.assertIn("--self-contained true", workflow)
        self.assertIn("-p:DebugSymbols=false", workflow)
        self.assertIn("-p:DebugType=None", workflow)
        self.assertIn("-p:PublishTrimmed=false", workflow)
        self.assertIn("-p:PublishSingleFile=false", workflow)
        self.assertNotIn("--clobber", workflow)
        self.assertNotIn("capture-site-media", workflow)
        self.assertLess(workflow.index("Generate and verify final checksums"), workflow.index("Attest the final archive"))
        self.assertLess(workflow.index("Attest the final multi-architecture digest"), workflow.index("Promote attested images to latest"))

    def test_publish_configuration_excludes_non_release_content(self) -> None:
        project = (ROOT / "src" / "BlokeBot" / "BlokeBot.csproj").read_text(
            encoding="utf-8"
        )
        for name in (
            "appsettings.Development.json",
            "appsettings.Simulation.json",
            "package.json",
            "package-lock.json",
            "*.tokens.json",
            "*.db",
        ):
            self.assertIn(name, project)
        self.assertIn('CopyToPublishDirectory="Never"', project)

    def test_nix_images_use_packages_lowercase_entrypoints_and_expected_boundaries(self) -> None:
        flake = (ROOT / "flake.nix").read_text(encoding="utf-8")
        self.assertIn('name = "ghcr.io/alsi-lawr/blokebot";', flake)
        self.assertIn('name = "ghcr.io/alsi-lawr/blokebot-site";', flake)
        self.assertIn('"${packages.blokebot}/bin/blokebot"', flake)
        self.assertIn('"${packages.blokebot-site}/bin/blokebot-site"', flake)
        self.assertIn('"ASPNETCORE_URLS=http://0.0.0.0:8080"', flake)
        self.assertIn('"ASPNETCORE_URLS=http://0.0.0.0:8081"', flake)
        self.assertIn('"/data" = { };', flake)
        site_image = flake.split("blokebot-site-image =", 1)[1]
        self.assertNotIn("Volumes =", site_image)


if __name__ == "__main__":
    unittest.main()
