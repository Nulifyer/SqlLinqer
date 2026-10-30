"""Attach after warmup, release the worker gate, and retain original trace data."""
import json
import pathlib
import selectors
import subprocess
import time


def capture(worker_command, collector_prefix, directory, name, seconds, trace_path=None, allocations=False):
    directory = pathlib.Path(directory).resolve()
    directory.mkdir(parents=True, exist_ok=True)
    trace = directory / (name + ".nettrace")
    if trace.exists():
        raise FileExistsError("Choose a fresh capture directory: " + str(trace))
    worker = subprocess.Popen(worker_command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                              stderr=subprocess.STDOUT, text=True)
    collector = None
    try:
        selector = selectors.DefaultSelector()
        selector.register(worker.stdout, selectors.EVENT_READ)
        if not selector.select(timeout=45):
            raise TimeoutError("Worker did not finish setup/warmup")
        ready_line = worker.stdout.readline()
        selector.close()
        ready = json.loads(ready_line)
        if not ready.get("ready"):
            raise RuntimeError("Worker did not reach attachment gate: " + ready_line)
        profiles = "dotnet-sampled-thread-time,dotnet-common" + (",gc-verbose" if allocations else "")
        command = collector_prefix + ["collect", "--process-id", str(ready["pid"]),
            "--profile", profiles, "--providers", "SqlLinqer-Profiling",
            "--stopping-event-provider-name", "SqlLinqer-Profiling",
            "--stopping-event-event-name", "ProfileComplete", "--duration", f"00:00:{(seconds + 20) // 60:02d}:{(seconds + 20) % 60:02d}",
            "--output", str(trace_path or trace)]
        log_path = directory / (name + ".collector.log")
        with log_path.open("w") as log:
            collector = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=log, stderr=subprocess.STDOUT, text=True)
            deadline = time.monotonic() + 30
            # Redirected dotnet-trace omits its interactive "Recording trace"
            # status. A nonempty trace means the EventPipe stream has arrived;
            # the worker remains gated until then.
            while not (trace.exists() and trace.stat().st_size > 32):
                if collector.poll() is not None:
                    raise RuntimeError("Collector exited before attaching; see " + str(log_path))
                if time.monotonic() > deadline:
                    raise TimeoutError("Collector did not produce an EventPipe stream; see " + str(log_path))
                time.sleep(0.05)
            worker.stdin.write("go\n")
            worker.stdin.flush()
            collector.wait(timeout=seconds + 60)
            output, _ = worker.communicate("done\n", timeout=30)
        (directory / (name + ".worker.log")).write_text(ready_line + output)
        if worker.returncode or collector.returncode:
            raise RuntimeError("Profile failed; inspect worker/collector logs in " + str(directory))
        result = json.loads(output.strip().splitlines()[-1])
        (directory / (name + ".workload.json")).write_text(json.dumps(result, indent=2) + "\n")
        return result
    finally:
        for process in [worker, collector]:
            if process is not None and process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


def convert(tool, directory, name):
    directory = pathlib.Path(directory).resolve()
    trace = directory / (name + ".nettrace")
    with (directory / (name + ".conversion.log")).open("w") as log:
        subprocess.run([str(tool), "convert", str(trace), "--format", "Speedscope", "--output", str(directory / name)],
                       stdout=log, stderr=subprocess.STDOUT, check=True)
    for inclusive in [False, True]:
        with (directory / (name + (".inclusive.txt" if inclusive else ".self.txt"))).open("w") as log:
            subprocess.run([str(tool), "report", str(trace), "topN", "--number", "40"] + (["--inclusive"] if inclusive else []),
                           stdout=log, stderr=subprocess.STDOUT, check=True)
