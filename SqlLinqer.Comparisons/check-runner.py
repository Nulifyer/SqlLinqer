#!/usr/bin/env python3
"""Check comparison rejection paths and the independent result oracle after a build."""
import pathlib
import sqlite3
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
RUNNER = ROOT / "SqlLinqer.Comparisons/run.py"
DLL = ROOT / "SqlLinqer.Comparisons/bin/Release/net10.0/SqlLinqer.Comparisons.dll"
LIBRARIES = ["sqllinqer", "dapper", "sqlkata", "linq2db", "ef-single", "ef-split"]


def worker(*arguments):
    return subprocess.run(["dotnet", str(DLL), *map(str, arguments)], cwd=ROOT, capture_output=True, text=True, timeout=60)


class RunnerChecks(unittest.TestCase):
    def test_cli_rejects_invalid_requests_before_building(self):
        for arguments in (["--iterations", "0"], ["--rows", "-1"], ["--libraries", "unknown"],
                          ["--libraries", "dapper", "dapper"], ["--operations", "0"], ["--warmup", "0"],
                          ["--page-size", "0"], ["--offset", "-1"], ["--rows", "1", "1"]):
            with self.subTest(arguments=arguments), tempfile.TemporaryDirectory() as directory:
                result = subprocess.run(["python3", str(RUNNER), *arguments, "--output", str(pathlib.Path(directory) / "report.json")],
                                        capture_output=True, text=True, timeout=10)
                self.assertEqual(2, result.returncode, result.stdout + result.stderr)

    def test_cli_preserves_existing_report(self):
        with tempfile.TemporaryDirectory() as directory:
            report = pathlib.Path(directory) / "report.json"
            report.write_text("original")
            result = subprocess.run(["python3", str(RUNNER), "--output", str(report)], capture_output=True, text=True, timeout=10)
            self.assertEqual(2, result.returncode)
            self.assertEqual("original", report.read_text())

    def test_setup_preserves_existing_fixture(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "fixture.db"
            self.assertEqual(0, worker("setup", path, 37, 16).returncode)
            original = path.read_bytes()
            self.assertNotEqual(0, worker("setup", path, 10, 1).returncode)
            self.assertEqual(original, path.read_bytes())

    def test_missing_fixture_is_not_created(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "missing.db"
            self.assertNotEqual(0, worker("measure", "dapper", "flat", path, 7, 5, 1, 1, 37).returncode)
            self.assertFalse(path.exists())

    def test_every_adapter_detects_corrupted_scalar_and_collection(self):
        for mutation in ("UPDATE Customers SET Note = 'corrupted' WHERE Id = 8", "DELETE FROM Orders WHERE Id = 81"):
            with tempfile.TemporaryDirectory() as directory:
                path = pathlib.Path(directory) / "fixture.db"
                setup = worker("setup", path, 37, 16)
                self.assertEqual(0, setup.returncode, setup.stderr)
                with sqlite3.connect(path) as connection:
                    connection.execute(mutation)
                for library in LIBRARIES:
                    with self.subTest(mutation=mutation, library=library):
                        result = worker("measure", library, "graph", path, 7, 5, 1, 1, 37)
                        self.assertNotEqual(0, result.returncode, result.stdout)
                        self.assertIn("Result mismatch", result.stderr)


if __name__ == "__main__":
    if not DLL.exists():
        raise SystemExit("Build SqlLinqer.Comparisons in Release first")
    unittest.main()
