import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

SMOKES = Path(__file__).resolve().parent


class SmokeAdapterTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.bin = self.root / "tools with spaces"
        self.bin.mkdir()
        for name in ("python3", "python"):
            stub = self.bin / name
            stub.write_text(f"#!{sys.executable}\nimport json, os, pathlib, sys\npathlib.Path(os.environ['ARGUMENTS']).write_text(json.dumps(sys.argv[1:]))\nsys.exit(int(os.environ['RESULT']))\n")
            stub.chmod(0o755)
        self.env = dict(os.environ, PATH=str(self.bin) + os.pathsep + os.environ["PATH"],
                        ARGUMENTS=str(self.root / "arguments.json"), RESULT="23")

    def test_bash_adapters_forward_arguments_and_failure_without_reimplementing_smokes(self) -> None:
        cases = (
            ("native_smoke", ["/path with spaces/blokebot", "--version", "0.18.0-rc.1"]),
            ("container_smoke", ["--image", "fixture:0.18.0-rc.1", "--kind", "bot", "--version", "0.18.0-rc.1", "--revision", "source-revision"]),
        )
        for name, arguments in cases:
            with self.subTest(adapter=name):
                result = subprocess.run(["bash", str(SMOKES / (name + ".sh")), *arguments], env=self.env)
                self.assertEqual(result.returncode, 23)
                self.assertEqual(json.loads((self.root / "arguments.json").read_text()), [str(SMOKES / (name + ".py")), *arguments])

    @unittest.skipUnless(shutil.which("pwsh"), "PowerShell is not installed; hosted Windows gate remains required")
    def test_powershell_supports_native_named_and_installed_positional_executable(self) -> None:
        executable = "/path with spaces/blokebot.exe"
        for arguments in (["-Executable", executable], [executable]):
            with self.subTest(arguments=arguments):
                result = subprocess.run(["pwsh", "-NoProfile", "-File", str(SMOKES / "native_smoke.ps1"), *arguments, "-Version", "0.18.0-rc.1"], env=self.env)
                self.assertEqual(result.returncode, 23)
                self.assertEqual(json.loads((self.root / "arguments.json").read_text()), [str(SMOKES / "native_smoke.py"), executable, "--version", "0.18.0-rc.1"])


if __name__ == "__main__":
    unittest.main()
