#!/usr/bin/env python3
"""Check runner exit codes and argument validation without external databases."""
import os
from pathlib import Path
import subprocess

PROJECT = Path(__file__).resolve().parent


def main():
    subprocess.run(["dotnet", "build", str(PROJECT / "SqlLinqer.Tests.csproj"),
                    "-c", "Release", "--nologo", "-v:q", "-p:GeneratePackageOnBuild=false"], check=True)
    dll = PROJECT / "bin/Release/net10.0/SqlLinqer.Tests.dll"
    environment = {key: value for key, value in os.environ.items()
                   if not key.startswith("SQLLINQER_")}
    help_result = subprocess.run(["dotnet", str(dll), "--help"], env=environment,
                                 text=True, capture_output=True, timeout=60, check=True)
    if "--rows" not in help_result.stdout or "--azure" not in help_result.stdout:
        raise SystemExit("Runner help does not describe the current scale and Azure options")
    cases = [
        (["--help"], 0, "Usage:"),
        (["--unknown"], 2, "Unknown option"),
        (["--suite"], 2, "--suite requires"),
        (["--suite", "missing"], 2, "--suite requires"),
        (["--temp-only"], 2, "requires --live"),
        (["--suite", "regression", "--live"], 2, "options require"),
        (["--suite", "regression", "--azure"], 2, "options require"),
        (["--all", "--temp-only"], 2, "cannot be combined"),
        (["--all", "--benchmark-render-only"], 2, "cannot be combined"),
        (["--suite", "sqlite", "--benchmark-render"], 2, "Rendering benchmarks require"),
        (["--benchmark-render-only", "--benchmark-render"], 2, "cannot be combined"),
        (["--benchmark-render-only", "--suite", "scale"], 2, "Rendering benchmarks require"),
        (["--rows", "1000"], 2, "Scale options require"),
        (["--iterations", "1"], 2, "Scale options require"),
        (["--results", "unused.json"], 2, "Scale options require"),
        (["--suite", "scale", "--results"], 2, "--results requires"),
        (["--suite", "scale", "--results", "--help"], 2, "--results requires"),
        (["--suite", "integration"], 1, "Run TestDBs/test-containers.sh"),
        (["--suite", "sql-server", "--require-live"], 1, "FAIL required live SQL Server endpoint is absent."),
        (["--suite", "sql-server", "--azure", "--require-live"], 1, "FAIL required live Azure SQL endpoint is absent."),
        (["--suite", "sql-server", "--live"], 0, "SKIP live SQL Server"),
        (["--suite", "sql-server", "--azure", "--live"], 0, "SKIP live Azure SQL"),
        (["--suite", "sql-server", "--suite", "sql-server"], 0, "Suites: 1 passed, 0 failed."),
        (["--suite", "integration", "--suite", "scale", "--rows", "1000", "--iterations", "1"], 1,
         ["integration: FAIL", "scale: PASS", "Suites: 1 passed, 1 failed."]),
    ]
    for option, values in [("--rows", [None, "no", "999", "1000001", "2147483648"]),
                           ("--iterations", [None, "no", "0", "21", "-1"])]:
        for value in values:
            cases.append((["--suite", "scale", option] + ([] if value is None else [value]),
                          2, option + " requires"))
    failures = 0
    for arguments, code, expected in cases:
        result = subprocess.run(["dotnet", str(dll), *arguments], env=environment,
                                text=True, capture_output=True, timeout=60)
        output = result.stdout + result.stderr
        required = [expected] if isinstance(expected, str) else expected
        valid = result.returncode == code and all(text in output for text in required)
        # Invalid arguments must exit before running fixtures or benchmarks.
        if code == 2:
            valid = valid and "=== " not in output and "PERF " not in output
        print(("PASS" if valid else "FAIL") + " " + " ".join(arguments))
        if not valid:
            failures += 1
            print(f"Expected exit {code} and {expected!r}, got {result.returncode}\n{output}")
    print(f"Runner: {len(cases) - failures} passed, {failures} failed.")
    return bool(failures)


if __name__ == "__main__":
    raise SystemExit(main())
