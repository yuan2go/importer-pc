import tempfile
import unittest
from pathlib import Path
from mvs_runtime import publish

class BundledRuntimePolicy(unittest.TestCase):
    def test_missing_actual_bytes_refuses_delivery(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaisesRegex(ValueError, "requires --mvs-runtime"):
                publish(Path(__file__).resolve().parents[1], Path(temp), None)

    def test_explicit_verification_omission_is_not_delivery(self):
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp)
            result = publish(Path(__file__).resolve().parents[1], output, None, True)
            self.assertEqual(result["Status"], "VERIFICATION_ONLY_VENDOR_ABSENT")
            self.assertFalse(result["BusinessReady"])
            self.assertFalse(list(output.iterdir()))

    def test_mixed_verification_and_vendor_input_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaisesRegex(ValueError, "Choose actual SDK delivery"):
                publish(Path(__file__).resolve().parents[1], Path(temp), Path(temp), True)
