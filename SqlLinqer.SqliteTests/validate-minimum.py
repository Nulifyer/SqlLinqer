#!/usr/bin/env python3
"""Run the SQLite suite against the pinned minimum engine on Linux x64."""
import hashlib
import io
import os
from pathlib import Path
import platform
import subprocess
import tempfile
import urllib.request
import zipfile

URL = "https://www.sqlite.org/2022/sqlite-amalgamation-3380000.zip"
ARCHIVE_SHA256 = "e055f6054e97747a135c89e36520c0a423249e8a91c5fc445163f4a6adb20df6"
# Published at https://www.sqlite.org/releaselog/3_38_0.html
SOURCE_SHA3_256 = "a69af0a88d59271a2dd3c846a3e93cbd29e7c499864f6c0462a3b4160bee1762"
PROJECT = Path(__file__).resolve().parent / "SqlLinqer.SqliteTests.csproj"


def run(*command, **kwargs):
    print("+", " ".join(map(str, command)), flush=True)
    subprocess.run(command, check=True, **kwargs)


def main():
    if platform.system() != "Linux" or platform.machine() != "x86_64":
        raise SystemExit("Minimum-runtime validation requires Linux x64, Python 3, cc, and the .NET 10 SDK.")
    with tempfile.TemporaryDirectory(prefix="sqllinqer-sqlite-338-") as directory:
        scratch = Path(directory)
        with urllib.request.urlopen(URL, timeout=60) as response:
            archive = response.read()
        if hashlib.sha256(archive).hexdigest() != ARCHIVE_SHA256:
            raise SystemExit("SQLite archive SHA-256 mismatch")
        with zipfile.ZipFile(io.BytesIO(archive)) as zipped:
            source = zipped.read("sqlite-amalgamation-3380000/sqlite3.c")
        if hashlib.sha3_256(source).hexdigest() != SOURCE_SHA3_256:
            raise SystemExit("sqlite3.c SHA3-256 mismatch")
        source_path = scratch / "sqlite3.c"
        source_path.write_bytes(source)
        publish = scratch / "publish"
        run("dotnet", "publish", str(PROJECT), "-c", "Release", "-r", "linux-x64",
            "--self-contained", "false", "-o", str(publish), "-v:q")
        library = publish / "libe_sqlite3.so"
        if not library.is_file():
            raise SystemExit("Published provider native asset libe_sqlite3.so was not found")
        run("cc", "-shared", "-fPIC", "-O2", "-DSQLITE_THREADSAFE=1",
            "-DSQLITE_ENABLE_COLUMN_METADATA", str(source_path), "-o", str(library),
            "-lm", "-ldl", "-lpthread")
        environment = dict(os.environ, SQLLINQER_EXPECT_SQLITE_VERSION="3.38.0")
        run("dotnet", str(publish / "SqlLinqer.SqliteTests.dll"), env=environment)
    print("Minimum-runtime validation passed; temporary source, library, and publish output removed.")


if __name__ == "__main__":
    main()
