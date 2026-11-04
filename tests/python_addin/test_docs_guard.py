"""Checks for the repository's Markdown link guard."""
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import check_guards


class DocsGuardTests(unittest.TestCase):
    def test_root_readme_missing_link_is_reported(self):
        with tempfile.TemporaryDirectory() as directory:
            doc = Path(directory) / "README.md"
            doc.write_text("[missing](absent.md)\n")
            self.assertTrue(check_guards.check_doc_links(paths=[doc]))

    def test_agent_file_missing_link_is_reported(self):
        with tempfile.TemporaryDirectory() as directory:
            doc = Path(directory) / "AGENTS.md"
            doc.write_text("[missing](absent.py)\n")
            self.assertTrue(check_guards.check_doc_links(paths=[doc]))

    def test_valid_relative_link_resolves(self):
        with tempfile.TemporaryDirectory() as directory:
            doc = Path(directory) / "README.md"
            doc.write_text("[present](present.md)\n")
            (doc.parent / "present.md").write_text("ok\n")
            self.assertEqual([], check_guards.check_doc_links(paths=[doc]))

    def test_default_scan_includes_readme_and_agent_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "README.md").write_text("[missing](readme-missing.md)\n")
            (root / "nested").mkdir()
            (root / "nested/AGENTS.md").write_text("[missing](agent-missing.md)\n")
            with patch.object(check_guards, "ROOT", root):
                failures = check_guards.check_doc_links()
            self.assertEqual(2, len(failures))
            self.assertTrue(any("readme-missing.md" in line for line in failures))
            self.assertTrue(any("agent-missing.md" in line for line in failures))
