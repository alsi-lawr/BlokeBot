from __future__ import annotations

import importlib.util
import io
from pathlib import Path
import unittest
from unittest import mock
import urllib.error


ROOT = Path(__file__).resolve().parents[2]


def load_script(name: str):
    spec = importlib.util.spec_from_file_location(name, ROOT / "scripts" / f"{name}.py")
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


release_smoke = load_script("release_smoke")
container_smoke = load_script("container_smoke")


def http_error(status: int, body: str) -> urllib.error.HTTPError:
    return urllib.error.HTTPError(
        "http://127.0.0.1:8080/",
        status,
        "test response",
        {},
        io.BytesIO(body.encode("utf-8")),
    )


BOT_BODY = "Sign in to BlokeBot Continue with Twitch Public leaderboard"


class FakeResponse:
    def __init__(self, status: int, body: str) -> None:
        self.status = status
        self._body = body.encode("utf-8")

    def __enter__(self):
        return self

    def __exit__(self, exception_type, exception, traceback) -> None:
        return None

    def read(self) -> bytes:
        return self._body


class HttpSmokeTests(unittest.TestCase):
    def test_normal_success_remains_accepted_for_bot_and_site(self) -> None:
        with mock.patch.object(
            release_smoke.urllib.request,
            "urlopen",
            return_value=FakeResponse(200, BOT_BODY),
        ):
            release_smoke._wait_for_login_surface("http://127.0.0.1:8080/", 0.1)

        with mock.patch.object(
            container_smoke.urllib.request,
            "urlopen",
            return_value=FakeResponse(200, "Own your channel tools."),
        ):
            container_smoke._wait_for_page(
                "8081",
                ("Own your channel tools.",),
                frozenset({200}),
                0.1,
            )

    def test_release_smoke_accepts_branded_offline_503_body(self) -> None:
        with mock.patch.object(
            release_smoke.urllib.request,
            "urlopen",
            side_effect=http_error(503, BOT_BODY),
        ):
            release_smoke._wait_for_login_surface("http://127.0.0.1:8080/", 0.1)

    def test_release_smoke_rejects_other_http_errors_even_with_branded_body(self) -> None:
        with mock.patch.object(
            release_smoke.urllib.request,
            "urlopen",
            side_effect=http_error(500, BOT_BODY),
        ):
            with self.assertRaisesRegex(release_smoke.SmokeError, "HTTP status 500"):
                release_smoke._wait_for_login_surface("http://127.0.0.1:8080/", 0.1)

    def test_container_bot_smoke_accepts_branded_offline_503_body(self) -> None:
        with mock.patch.object(
            container_smoke.urllib.request,
            "urlopen",
            side_effect=http_error(503, BOT_BODY),
        ):
            container_smoke._wait_for_page(
                "8080",
                ("Sign in to BlokeBot", "Continue with Twitch", "Public leaderboard"),
                frozenset({200, 503}),
                0.1,
            )

    def test_container_site_and_unrelated_bot_errors_reject_503_or_500(self) -> None:
        with mock.patch.object(
            container_smoke.urllib.request,
            "urlopen",
            side_effect=http_error(503, "Own your channel tools."),
        ):
            with self.assertRaisesRegex(container_smoke.ContainerSmokeError, "HTTP status 503"):
                container_smoke._wait_for_page(
                    "8081",
                    ("Own your channel tools.",),
                    frozenset({200}),
                    0.1,
                )

        with mock.patch.object(
            container_smoke.urllib.request,
            "urlopen",
            side_effect=http_error(500, BOT_BODY),
        ):
            with self.assertRaisesRegex(container_smoke.ContainerSmokeError, "HTTP status 500"):
                container_smoke._wait_for_page(
                    "8080",
                    ("Continue with Twitch",),
                    frozenset({200, 503}),
                    0.1,
                )


if __name__ == "__main__":
    unittest.main()
