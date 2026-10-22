#!/usr/bin/env python3
"""Build the sample, exercise its HTTP endpoints, and shut down the owned process."""
import json
import pathlib
import re
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

project = pathlib.Path(__file__).resolve().parent
subprocess.run(["dotnet", "build", str(project), "-c", "Release", "-p:GeneratePackageOnBuild=false", "-v", "quiet"], check=True)
with tempfile.TemporaryFile(mode="w+") as log:
    process = subprocess.Popen(["dotnet", str(project / "bin/Release/net10.0/SqlLinqer.MinimalApi.dll"),
                                "--urls", "http://127.0.0.1:0"], stdout=log, stderr=subprocess.STDOUT)
    try:
        deadline = time.monotonic() + 20
        base = None
        while time.monotonic() < deadline:
            log.seek(0)
            output = log.read()
            match = re.search(r"Now listening on: (http://127\.0\.0\.1:\d+)", output)
            if match:
                base = match[1]
                break
            if process.poll() is not None:
                raise RuntimeError(output)
            time.sleep(0.1)
        if base is None:
            raise TimeoutError("Sample did not start")

        def get(path):
            with urllib.request.urlopen(base + path, timeout=10) as response:
                return json.load(response)

        users = get("/users")
        assert [user["displayName"] for user in users] == ["Ada Lovelace", "Grace Hopper", "Margaret Hamilton"]
        assert all("name" not in user for user in users)
        assert get("/users?name=Ada") == [users[0]]
        assert get("/users/page?page=1&pageSize=2") == {"items": users[:2], "total": 3}
        assert get("/users/page?page=20&pageSize=2") == {"items": [], "total": 3}
        assert get("/users/stream") == users
        try:
            get("/users/page?page=0")
            raise AssertionError("Invalid paging was accepted")
        except urllib.error.HTTPError as error:
            assert error.code == 400
        print("Sample HTTP checks passed: filtering, API names, pages and totals, streaming, and invalid input")
    finally:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)
