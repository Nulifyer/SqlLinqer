"""Own-process, user-space CPU samples. No sudo or system settings required."""
import argparse
import json
import os
import pathlib
import selectors
import shutil
import signal
import subprocess
import time

root = pathlib.Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser()
p.add_argument("--layers", nargs="+", choices=["model", "capture", "render", "map"], default=["render", "map"])
p.add_argument("--seconds", type=int, default=12)
p.add_argument("--output", type=pathlib.Path, required=True)
p.add_argument("--perf", type=pathlib.Path, default=root / "TestResults/profiling/perf/usr/bin/perf")
a = p.parse_args()
if not 1 <= a.seconds <= 120:
    p.error("seconds must be in 1..120")
a.output.mkdir(parents=True, exist_ok=True)
environment = dict(os.environ, DOTNET_PerfMapEnabled="1")
environment["LD_LIBRARY_PATH"] = str(a.perf.resolve().parent.parent / "lib") + ":" + environment.get("LD_LIBRARY_PATH", "")
assembly = root / "SqlLinqer.Tests/bin/Release/net10.0/SqlLinqer.Tests.dll"
for layer in a.layers:
    data = a.output.resolve() / (layer + ".perf.data")
    if data.exists():
        raise FileExistsError(data)
    worker = subprocess.Popen(["dotnet", "exec", "--fx-version", "10.0.13", str(assembly), "--profile-layer", layer, str(a.seconds)],
                              stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, env=environment)
    collector = None
    try:
        selector = selectors.DefaultSelector(); selector.register(worker.stdout, selectors.EVENT_READ)
        if not selector.select(timeout=45):
            raise TimeoutError("Worker did not warm up")
        ready_line = worker.stdout.readline(); ready = json.loads(ready_line)
        with data.with_suffix(".log").open("w") as log:
            collector = subprocess.Popen([str(a.perf), "record", "-e", "cpu-clock:u", "-F", "199", "--call-graph", "fp",
                "-p", str(ready["pid"]), "-o", str(data)], stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT, env=environment)
            time.sleep(0.5)
            if collector.poll() is not None:
                raise RuntimeError("perf did not attach; inspect " + str(data.with_suffix(".log")))
            worker.stdin.write("go\n"); worker.stdin.flush()
            if not selector.select(timeout=a.seconds + 45):
                raise TimeoutError("Worker did not finish profiling phase")
            result_line = worker.stdout.readline(); result = json.loads(result_line)
            collector.send_signal(signal.SIGINT); collector.wait(timeout=30)
        selector.close()
        if collector.returncode not in [0, -signal.SIGINT, 128 + signal.SIGINT]:
            raise RuntimeError("perf failed: " + str(collector.returncode))
        for path in pathlib.Path("/tmp").glob("*" + str(ready["pid"]) + "*"):
            if path.is_file() and (path.name.startswith("perf-") or path.name.startswith("perfinfo-")):
                shutil.copy2(path, a.output / path.name)
        with (a.output / (layer + ".perf.script.txt")).open("w") as script:
            subprocess.run([str(a.perf), "script", "-i", str(data)], stdout=script, check=True, env=environment)
        remaining, _ = worker.communicate("done\n", timeout=30)
        (a.output / (layer + ".perf.worker.log")).write_text(ready_line + result_line + remaining)
        if worker.returncode:
            raise RuntimeError("Worker failed")
        result.update(metric="Own-process user-space on-CPU samples (cpu-clock:u, 199 Hz, frame pointers)",
                      perf=subprocess.check_output([str(a.perf), "version"], env=environment, text=True).strip())
        (a.output / (layer + ".perf.workload.json")).write_text(json.dumps(result, indent=2) + "\n")
        print(layer, result["operations"], "operations", flush=True)
    finally:
        for process in [collector, worker]:
            if process is not None and process.poll() is None:
                process.terminate()
                try: process.wait(timeout=10)
                except subprocess.TimeoutExpired: process.kill(); process.wait()
