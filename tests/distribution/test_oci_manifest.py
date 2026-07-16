from __future__ import annotations

import importlib.util
from pathlib import Path
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "oci_manifest", ROOT / "scripts" / "oci_manifest.py"
)
assert SPEC is not None and SPEC.loader is not None
oci_manifest = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(oci_manifest)


class OciManifestTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.image = "ghcr.io/alsi-lawr/blokebot"

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def write_digest(self, architecture: str, digit: str) -> None:
        (self.root / f"blokebot-{architecture}.digest").write_text(
            f"{self.image}@sha256:{digit * 64}\n", encoding="utf-8"
        )

    def test_exact_native_digest_set_writes_ordered_references(self) -> None:
        self.write_digest("amd64", "a")
        self.write_digest("arm64", "b")
        output = self.root / "references.txt"

        oci_manifest.write_references(self.root, self.image, output)

        self.assertEqual(
            output.read_text(encoding="utf-8").splitlines(),
            [
                f"{self.image}@sha256:{'a' * 64}",
                f"{self.image}@sha256:{'b' * 64}",
            ],
        )

    def test_missing_unexpected_duplicate_and_overwrite_are_rejected(self) -> None:
        self.write_digest("amd64", "a")
        with self.assertRaisesRegex(oci_manifest.OciManifestError, "incomplete"):
            oci_manifest.collect_references(self.root, self.image)

        self.write_digest("arm64", "a")
        with self.assertRaisesRegex(oci_manifest.OciManifestError, "Duplicate"):
            oci_manifest.collect_references(self.root, self.image)

        self.write_digest("arm64", "b")
        (self.root / "unexpected.digest").write_text("unexpected", encoding="utf-8")
        with self.assertRaisesRegex(oci_manifest.OciManifestError, "Unexpected"):
            oci_manifest.collect_references(self.root, self.image)

        (self.root / "unexpected.digest").unlink()
        output = self.root / "references.txt"
        output.write_text("existing", encoding="utf-8")
        with self.assertRaisesRegex(oci_manifest.OciManifestError, "overwrite"):
            oci_manifest.write_references(self.root, self.image, output)


if __name__ == "__main__":
    unittest.main()
