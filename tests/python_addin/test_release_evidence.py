"""Tests for the stable-release evidence gate."""
import hashlib
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from check_release_evidence import check_release_evidence  # noqa: E402


class ReleaseEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.addin = self.root / "addin.zip"
        self.addin.write_bytes(b"python addin")
        self.server = self.root / "server.zip"
        self.server.write_bytes(b"windows server")
        self.mac_server = self.root / "mac-server.zip"
        self.mac_server.write_bytes(b"mac server")
        self.evidence = self.root / "evidence.md"
        self.values = {
            "Status": "pass",
            "Windows": "deferred (post-release)",
            "macOS": "pass",
            "Candidate version": "1.0.0",
            "Candidate source commit": "a" * 40,
            "Add-in SHA256": self.sha(self.addin),
            "Server win-x64 SHA256": self.sha(self.server),
            "Server osx-arm64 SHA256": self.sha(self.mac_server),
        }
        self.write_evidence()

    def tearDown(self):
        self.temp.cleanup()

    @staticmethod
    def sha(path):
        return hashlib.sha256(path.read_bytes()).hexdigest()

    def write_evidence(self):
        self.evidence.write_text("".join(f"{key}: {value}\n" for key, value in self.values.items()))

    def check(self):
        check_release_evidence(
            self.evidence,
            "1.0.0",
            self.addin,
            {"win-x64": self.server, "osx-arm64": self.mac_server},
        )

    def test_rejects_pending_macos(self):
        self.values["macOS"] = "pending"
        self.write_evidence()
        with self.assertRaises(ValueError):
            self.check()

    def test_rejects_wrong_windows_status(self):
        self.values["Windows"] = "pending"
        self.write_evidence()
        with self.assertRaises(ValueError):
            self.check()

    def test_rejects_wrong_version(self):
        self.values["Candidate version"] = "0.1.0"
        self.write_evidence()
        with self.assertRaises(ValueError):
            self.check()

    def test_rejects_wrong_addin_hash(self):
        self.values["Add-in SHA256"] = "0" * 64
        self.write_evidence()
        with self.assertRaises(ValueError):
            self.check()

    def test_rejects_wrong_server_hash(self):
        self.values["Server win-x64 SHA256"] = "0" * 64
        self.write_evidence()
        with self.assertRaises(ValueError):
            self.check()

    def test_rejects_missing_source_commit(self):
        del self.values["Candidate source commit"]
        self.write_evidence()
        with self.assertRaises(ValueError):
            self.check()

    def test_accepts_complete_matching_record(self):
        self.check()


if __name__ == "__main__":
    unittest.main()
