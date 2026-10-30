#!/usr/bin/env python3
"""Run sequential isolated Release workers; retain raw samples and median summaries."""
import argparse
import datetime
import json
import pathlib
import platform
import random
import statistics
import subprocess
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--rows", nargs="+", type=int, default=[100_000, 1_000_000])
parser.add_argument("--providers", nargs="+", choices=["sqlite", "duckdb", "duckdb-scan"], default=["sqlite", "duckdb"])
parser.add_argument("--modes", nargs="+", choices=["buffered", "stream", "batch", "collect", "first"], default=["buffered", "stream", "batch", "collect", "first"])
parser.add_argument("--iterations", type=int, default=7)
parser.add_argument("--width", type=int, default=256)
parser.add_argument("--batch-size", type=int, default=1000)
parser.add_argument("--output", type=pathlib.Path, required=True)
args = parser.parse_args()
if min(*args.rows, args.iterations, args.width, args.batch_size) < 1:
    parser.error("Counts and sizes must be positive")
root = pathlib.Path(__file__).resolve().parent.parent
project = root / "SqlLinqer.Streaming.Benchmarks"
subprocess.run(["dotnet", "build", str(project), "-c", "Release", "--nologo", "-v", "quiet"], check=True)
dll = project / "bin/Release/net10.0/SqlLinqer.Streaming.Benchmarks.dll"

def invoke(*arguments):
    result = subprocess.run(["dotnet", str(dll), *map(str, arguments)], capture_output=True, text=True, check=True, timeout=300)
    return json.loads(result.stdout.strip().splitlines()[-1])

samples = []
started = datetime.datetime.now(datetime.timezone.utc).isoformat()
metrics = ["elapsedMs", "firstRowMs", "rowsPerSecond", "allocatedBytes", "allocatedBytesPerRow", "peakManagedBytes", "retainedManagedDeltaBytes", "baselineWorkingSetBytes", "peakWorkingSetBytes"]

def save():
    summaries = []
    for provider in args.providers:
        for rows in args.rows:
            for mode in args.modes:
                group = [sample for sample in samples if (sample["provider"], sample["rows"], sample["mode"]) == (provider, rows, mode)]
                if not group:
                    continue
                summaries.append(dict(provider=provider, rows=rows, mode=mode, samples=len(group), **{key: statistics.median(item[key] for item in group) for key in metrics}))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(dict(startedUtc=started, host=platform.platform(), cpu=platform.processor(), options=vars(args) | {"output": str(args.output)}, summaries=summaries, samples=samples), indent=2) + "\n")

with tempfile.TemporaryDirectory(prefix="sqllinqer-stream-benchmark-") as temporary:
    for rows in args.rows:
        paths = {}
        for engine in sorted({"duckdb" if provider.startswith("duckdb") else "sqlite" for provider in args.providers}):
            path = pathlib.Path(temporary) / f"{engine}-{rows}.db"
            print("SETUP", invoke("setup", engine, path, rows, args.width), flush=True)
            paths[engine] = path
        jobs = [(provider, mode, iteration) for iteration in range(args.iterations) for provider in args.providers for mode in args.modes]
        random.Random(20261021 + rows).shuffle(jobs)
        for provider, mode, iteration in jobs:
            sample = invoke("measure", provider, paths["duckdb" if provider.startswith("duckdb") else "sqlite"], rows, mode, args.batch_size)
            sample["iteration"] = iteration
            samples.append(sample)
            save()
            print(f"{provider:15s} {mode:8s} rows={rows} sample={iteration} elapsed={sample['elapsedMs']:.1f}ms first={sample['firstRowMs']:.2f}ms peak_rss={sample['peakWorkingSetBytes']/2**20:.1f}MiB managed={sample['peakManagedBytes']/2**20:.1f}MiB", flush=True)
print(f"Saved {len(samples)} validated samples to {args.output}", flush=True)
