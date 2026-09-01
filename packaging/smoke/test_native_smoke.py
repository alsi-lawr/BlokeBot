from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock

import native_smoke


class WorkerProbeTests(unittest.TestCase):
    def test_worker_probe_follows_installed_executable_symlink(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            executable = root / "package" / "bin" / "blokebot"
            executable.parent.mkdir(parents=True)
            executable.write_text("", encoding="utf-8")
            self._write_worker(executable.parent)
            shim = root / "shims" / "blokebot"
            shim.parent.mkdir()
            shim.symlink_to(executable)

            native_smoke._run_worker_probe(shim, root / "state")

    def test_worker_probe_uses_scoop_shim_target(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            executable = root / "package" / "bin" / "blokebot.exe"
            executable.parent.mkdir(parents=True)
            executable.write_text("", encoding="utf-8")
            self._write_worker(executable.parent)
            shim = root / "shims" / "blokebot.exe"
            shim.parent.mkdir()
            shim.write_text("", encoding="utf-8")
            shim.with_suffix(".shim").write_text(
                f'path = "{executable}"\n',
                encoding="utf-8",
            )

            native_smoke._run_worker_probe(shim, root / "state")

    @staticmethod
    def _write_worker(executable_directory: Path) -> None:
        worker_directory = executable_directory / "plugin-worker"
        worker_directory.mkdir()
        worker = worker_directory / "BlokeBot.PluginWorker"
        worker.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
        worker.chmod(0o755)


class RemoveDataDirectoryTests(unittest.TestCase):
    def test_sharing_violation_is_retried_until_removal_succeeds(self) -> None:
        sharing_violation = PermissionError("file is in use")
        sharing_violation.winerror = 32
        remove = Mock(side_effect=[sharing_violation, None])
        monotonic = Mock(side_effect=[0.0, 0.1])
        sleep = Mock()

        native_smoke._remove_data_directory(
            Path("data"),
            remove=remove,
            monotonic=monotonic,
            sleep=sleep,
        )

        self.assertEqual(remove.call_count, 2)
        sleep.assert_called_once_with(0.1)

    def test_persistent_sharing_violation_is_raised_at_deadline(self) -> None:
        sharing_violation = PermissionError("file is in use")
        sharing_violation.winerror = 32
        remove = Mock(side_effect=sharing_violation)
        monotonic = Mock(side_effect=[0.0, 0.1, 5.0])
        sleep = Mock()

        with self.assertRaises(PermissionError) as raised:
            native_smoke._remove_data_directory(
                Path("data"),
                remove=remove,
                monotonic=monotonic,
                sleep=sleep,
            )

        self.assertIs(raised.exception, sharing_violation)
        self.assertEqual(remove.call_count, 2)
        sleep.assert_called_once_with(0.1)

    def test_sharing_violation_near_deadline_sleeps_only_for_remaining_time(self) -> None:
        sharing_violation = PermissionError("file is in use")
        sharing_violation.winerror = 32
        remove = Mock(side_effect=sharing_violation)
        monotonic = Mock(side_effect=[0.0, 4.95, 4.95, 5.0])
        sleep = Mock()

        with self.assertRaises(PermissionError):
            native_smoke._remove_data_directory(
                Path("data"),
                remove=remove,
                monotonic=monotonic,
                sleep=sleep,
            )

        self.assertEqual(sleep.call_count, 2)
        self.assertEqual(sleep.call_args_list[0].args[0], 0.1)
        self.assertAlmostEqual(sleep.call_args_list[1].args[0], 0.05)

    def test_other_permission_error_is_not_retried(self) -> None:
        permission_error = PermissionError("access denied")
        permission_error.winerror = 5
        remove = Mock(side_effect=permission_error)
        monotonic = Mock()
        sleep = Mock()

        with self.assertRaises(PermissionError) as raised:
            native_smoke._remove_data_directory(
                Path("data"),
                remove=remove,
                monotonic=monotonic,
                sleep=sleep,
            )

        self.assertIs(raised.exception, permission_error)
        remove.assert_called_once_with(Path("data"))
        monotonic.assert_not_called()
        sleep.assert_not_called()

    def test_non_permission_error_is_not_retried(self) -> None:
        cleanup_error = OSError("cleanup failed")
        remove = Mock(side_effect=cleanup_error)
        monotonic = Mock()
        sleep = Mock()

        with self.assertRaises(OSError) as raised:
            native_smoke._remove_data_directory(
                Path("data"),
                remove=remove,
                monotonic=monotonic,
                sleep=sleep,
            )

        self.assertIs(raised.exception, cleanup_error)
        remove.assert_called_once_with(Path("data"))
        monotonic.assert_not_called()
        sleep.assert_not_called()


if __name__ == "__main__":
    unittest.main()
