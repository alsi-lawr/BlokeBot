from pathlib import Path
import unittest
from unittest.mock import Mock

import native_smoke


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
