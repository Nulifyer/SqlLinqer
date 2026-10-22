#!/usr/bin/env python3
"""Run validated SQLite comparisons in sequential, isolated Release processes."""
import argparse
import datetime
import hashlib
import json
import os
import pathlib
import platform
import random
import statistics
import subprocess
import tempfile

LIBRARIES = ["sqllinqer", "dapper", "sqlkata", "linq2db", "ef-single", "ef-split"]
WORKLOADS = ["flat", "graph", "page", "stream"]
ROOT = pathlib.Path(__file__).resolve().parent.parent
PROJECT = ROOT / "SqlLinqer.Comparisons"


def positive(text):
    value = int(text)
    if value < 1:
        raise argparse.ArgumentTypeError("must be positive")
    return value


def nonnegative(text):
    value = int(text)
    if value < 0:
        raise argparse.ArgumentTypeError("must be nonnegative")
    return value


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rows", nargs="+", type=nonnegative, default=[1000, 10000])
    parser.add_argument("--libraries", nargs="+", choices=LIBRARIES, default=LIBRARIES)
    parser.add_argument("--workloads", nargs="+", choices=WORKLOADS, default=WORKLOADS)
    parser.add_argument("--iterations", type=positive, default=5)
    parser.add_argument("--operations", type=positive, default=5)
    parser.add_argument("--warmup", type=positive, default=5)
    parser.add_argument("--width", type=nonnegative, default=128)
    parser.add_argument("--page-size", type=positive, default=50)
    parser.add_argument("--offset", type=nonnegative, default=100)
    parser.add_argument("--seed", type=int, default=20261022)
    parser.add_argument("--timeout", type=positive, default=300, help="seconds per worker")
    parser.add_argument("--verify", action="store_true", help="check empty, single-row, and mixed fixtures; not a performance run")
    parser.add_argument("--overwrite", action="store_true")
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    for key in ("rows", "libraries", "workloads"):
        values = getattr(args, key)
        if len(set(values)) != len(values):
            parser.error(f"--{key} must not contain duplicates")
    if args.verify:
        args.rows, args.iterations, args.operations, args.warmup = [0, 1, 37], 1, 1, 1
        args.width, args.page_size, args.offset = 16, 7, 5
    if args.output.exists() and not args.overwrite:
        parser.error("Output exists; choose another path or pass --overwrite")
    return args


def command_output(arguments):
    return subprocess.run(arguments, cwd=ROOT, capture_output=True, text=True, check=True).stdout.strip()


def source_identity():
    files = sorted([*PROJECT.glob("*.cs"), PROJECT / "run.py", *PROJECT.glob("*.csproj"), PROJECT / "packages.lock.json",
                    *(ROOT / "SqlLinqer").rglob("*.cs"), ROOT / "SqlLinqer/SQLLinqer.csproj"])
    digest = hashlib.sha256()
    for file in files:
        if "obj" in file.parts or "bin" in file.parts:
            continue
        digest.update(str(file.relative_to(ROOT)).encode() + b"\0" + file.read_bytes() + b"\0")
    return {"commit": command_output(["git", "rev-parse", "HEAD"]),
            "dirty": bool(command_output(["git", "status", "--porcelain"])), "codeSha256": digest.hexdigest()}


def host_info():
    processor = platform.processor()
    if not processor and pathlib.Path("/proc/cpuinfo").exists():
        processor = next((line.split(":", 1)[1].strip() for line in pathlib.Path("/proc/cpuinfo").read_text().splitlines()
                          if line.startswith("model name")), "unknown")
    return {"platform": platform.platform(), "processor": processor, "logicalCpus": os.cpu_count()}


def summarize(samples):
    groups = {}
    for sample in samples:
        groups.setdefault((sample["fixtureRows"], sample["workload"], sample["library"]), []).append(sample)
    summaries = []
    for (rows, workload, library), group in sorted(groups.items()):
        timings = [x["elapsedMsPerOperation"] for x in group]
        summaries.append({"fixtureRows": rows, "workload": workload, "library": library, "samples": len(group),
                          "medianMs": statistics.median(timings), "minMs": min(timings), "maxMs": max(timings),
                          "medianAllocatedBytes": statistics.median(x["allocatedBytesPerOperation"] for x in group),
                          "medianFirstRowMs": statistics.median(x["firstRowMs"] for x in group) if group[0]["firstRowMs"] is not None else None,
                          "commandsPerOperation": group[0]["commandsPerOperation"], "resultRows": group[0]["resultRows"]})
    return summaries


def main():
    args = parse_args()
    subprocess.run(["dotnet", "restore", str(PROJECT), "--locked-mode", "--nologo"], cwd=ROOT, check=True)
    subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release", "--no-restore", "--nologo", "-v", "quiet",
                    "-p:GeneratePackageOnBuild=false"], cwd=ROOT, check=True)
    dll = PROJECT / "bin/Release/net10.0/SqlLinqer.Comparisons.dll"
    samples = []
    report = {"schemaVersion": 1, "status": "running", "purpose": "correctness" if args.verify else "measurement",
              "startedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "host": host_info(),
              "sdk": command_output(["dotnet", "--version"]), "source": source_identity(),
              "packageLock": json.loads((PROJECT / "packages.lock.json").read_text()),
              "options": vars(args) | {"output": str(args.output)}, "samples": samples}

    def save():
        report["summaries"] = summarize(samples)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        # Preserve a usable report if the process is interrupted during a write.
        temporary = args.output.with_name(args.output.name + ".tmp")
        temporary.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n")
        temporary.replace(args.output)

    worker_environment = os.environ | {"DOTNET_TieredCompilation": "0"}
    report["workerEnvironment"] = {key: worker_environment.get(key) for key in
                                   ("DOTNET_TieredCompilation", "DOTNET_ReadyToRun", "DOTNET_gcServer", "DOTNET_GCHeapHardLimit")}

    def invoke(*arguments):
        result = subprocess.run(["dotnet", str(dll), *map(str, arguments)], capture_output=True,
                                text=True, cwd=ROOT, timeout=args.timeout, env=worker_environment)
        if result.returncode:
            raise RuntimeError(f"Worker failed ({result.returncode}): {' '.join(map(str, arguments))}\n{result.stderr}\n{result.stdout}")
        return json.loads(result.stdout.strip())

    save()
    try:
        with tempfile.TemporaryDirectory(prefix="sqllinqer-comparisons-") as temporary:
            for rows in args.rows:
                path = pathlib.Path(temporary) / f"fixture-{rows}.db"
                invoke("setup", path, rows, args.width)
                jobs = [(iteration, library, workload) for iteration in range(args.iterations)
                        for library in args.libraries for workload in args.workloads]
                random.Random(args.seed + rows).shuffle(jobs)
                for iteration, library, workload in jobs:
                    sample = invoke("measure", library, workload, path, args.page_size, args.offset, args.warmup, args.operations, rows)
                    sample["iteration"] = iteration
                    # Each worker checks its independent fixture oracle; also guard
                    # against accidental cross-library differences in one run.
                    for previous in samples:
                        if (previous["fixtureRows"], previous["workload"]) == (rows, workload):
                            for key in ("fingerprint", "resultRows", "orders", "labels", "total"):
                                if sample[key] != previous[key]:
                                    raise ValueError(f"Cross-library result mismatch for {key}")
                    samples.append(sample)
                    save()
                    print(f"rows={rows:<6} {library:10} {workload:6} sample={iteration + 1} "
                          f"{sample['elapsedMsPerOperation']:9.3f} ms/op "
                          f"{sample['allocatedBytesPerOperation'] / 1024:10.1f} KiB/op "
                          f"commands={sample['commandsPerOperation']}", flush=True)
        report["status"] = "complete"
        report["finishedUtc"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        save()
    except BaseException as error:
        report["status"] = "failed"
        report["error"] = str(error)
        save()
        raise
    print(f"Saved {len(samples)} validated samples to {args.output}", flush=True)


if __name__ == "__main__":
    main()
