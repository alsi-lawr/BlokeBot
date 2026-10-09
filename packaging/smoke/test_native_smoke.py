from collections.abc import Callable
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import Mock

import container_smoke
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


class DocumentProbeTests(unittest.TestCase):
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self) -> None:
            navigation = (
                self.headers.get("Sec-Fetch-Site"),
                self.headers.get("Sec-Fetch-Mode"),
                self.headers.get("Sec-Fetch-Dest"),
            ) == ("none", "navigate", "document")
            status = 503 if navigation and self.path == "/auth/login" else 403
            body = b"offline-auth-body" if status == 503 else b"forbidden"
            self.send_response(status)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, format: str, *args: object) -> None:
            pass

    def setUp(self) -> None:
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), self.Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.url = f"http://127.0.0.1:{self.server.server_port}"

    def tearDown(self) -> None:
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()

    def test_document_navigation_reads_the_unavailable_auth_response_body(self) -> None:
        for name, read in self._readers():
            with self.subTest(probe=name):
                self.assertEqual(read(f"{self.url}/auth/login"), "offline-auth-body")

    def test_forbidden_document_is_not_accepted_as_an_offline_auth_response(self) -> None:
        for name, read in self._readers():
            with self.subTest(probe=name):
                error = (
                    native_smoke.NativeSmokeError
                    if name == "native"
                    else container_smoke.ContainerSmokeError
                )
                with self.assertRaisesRegex(error, "Unexpected HTTP status 403"):
                    read(f"{self.url}/forbidden")

    @staticmethod
    def _readers() -> tuple[tuple[str, Callable[[str], str]], ...]:
        return (
            ("native", native_smoke._read_http_body),
            (
                "container",
                lambda url: container_smoke._read_http_body(url, frozenset({503})),
            ),
        )


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
