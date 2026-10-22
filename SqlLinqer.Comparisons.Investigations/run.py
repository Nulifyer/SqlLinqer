#!/usr/bin/env python3
"""Investigate flat row/JSON mapping with pre-rendered SQL in isolated workers."""
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

ROOT = pathlib.Path(__file__).resolve().parent.parent
PROJECT = pathlib.Path(__file__).resolve().parent
MODES = ["native", "tabular", "json", "json-standard", "stored-json", "stored-standard"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rows", nargs="+", type=int, default=[1000, 10000])
    parser.add_argument("--widths", nargs="+", type=int, default=[0, 128, 1024])
    parser.add_argument("--iterations", type=int, default=5)
    parser.add_argument("--operations", type=int, default=5)
    parser.add_argument("--warmup", type=int, default=5)
    parser.add_argument("--verify", action="store_true")
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    if min(args.rows + args.widths) < 0 or min(args.iterations, args.operations, args.warmup) < 1:
        parser.error("Rows and widths must be nonnegative; iteration counts must be positive")
    if len(set(args.rows)) != len(args.rows) or len(set(args.widths)) != len(args.widths):
        parser.error("Rows and widths must not contain duplicates")
    if args.output.exists():
        parser.error("Choose a new output path; reports are not overwritten")
    if args.verify:
        args.rows, args.widths, args.iterations, args.operations, args.warmup = [0, 1, 37], [16], 1, 1, 1
    subprocess.run(["dotnet", "restore", str(PROJECT), "--locked-mode", "--nologo"], check=True)
    subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release", "--no-restore", "--nologo", "-v", "quiet",
                    "-p:GeneratePackageOnBuild=false"], check=True)
    dll = PROJECT / "bin/Release/net10.0/SqlLinqer.Comparisons.Investigations.dll"
    worker_environment = os.environ | {"DOTNET_TieredCompilation": "0"}

    def invoke(*arguments):
        result = subprocess.run(["dotnet", str(dll), *map(str, arguments)], capture_output=True, text=True,
                                timeout=300, env=worker_environment)
        if result.returncode:
            raise RuntimeError(f"Worker failed: {arguments}\n{result.stderr}")
        return json.loads(result.stdout)

    digest = hashlib.sha256()
    files = sorted([*PROJECT.glob("*.cs"), PROJECT / "run.py", *PROJECT.glob("*.csproj"), PROJECT / "packages.lock.json",
                    *(ROOT / "SqlLinqer").rglob("*.cs"), ROOT / "SqlLinqer/SQLLinqer.csproj"])
    for path in files:
        if "obj" not in path.parts and "bin" not in path.parts:
            digest.update(str(path.relative_to(ROOT)).encode() + b"\0" + path.read_bytes() + b"\0")
    samples = []
    report = {"status": "running", "purpose": "correctness" if args.verify else "investigation",
              "startedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "host": {"platform": platform.platform(), "logicalCpus": os.cpu_count()},
              "sdk": subprocess.check_output(["dotnet", "--version"], text=True).strip(),
              "commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
              "codeSha256": digest.hexdigest(), "packageLock": json.loads((PROJECT / "packages.lock.json").read_text()),
              "tieredCompilation": False, "options": vars(args) | {"output": str(args.output)}, "samples": samples}

    def save():
        summaries = []
        for rows, width, mode in sorted({(s["fixtureRows"], s["width"], s["mode"]) for s in samples}):
            group = [s for s in samples if (s["fixtureRows"], s["width"], s["mode"]) == (rows, width, mode)]
            times = [s["elapsedMsPerOperation"] for s in group]
            summaries.append({"fixtureRows": rows, "width": width, "mode": mode, "samples": len(group),
                              "medianMs": statistics.median(times), "minMs": min(times), "maxMs": max(times),
                              "medianAllocatedBytes": statistics.median(s["allocatedBytesPerOperation"] for s in group)})
        report["summaries"] = summaries
        args.output.parent.mkdir(parents=True, exist_ok=True)
        temporary = args.output.with_name(args.output.name + ".tmp")
        temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
        temporary.replace(args.output)

    save()
    try:
        with tempfile.TemporaryDirectory(prefix="sqllinqer-flat-investigation-") as directory:
            for rows in args.rows:
                for width in args.widths:
                    path = pathlib.Path(directory) / f"rows-{rows}-width-{width}.db"
                    invoke("setup", path, rows, width)
                    jobs = [(mode, iteration) for iteration in range(args.iterations) for mode in MODES]
                    random.Random(20261022 + rows + width).shuffle(jobs)
                    for mode, iteration in jobs:
                        sample = invoke("measure", mode, path, args.warmup, args.operations)
                        sample["iteration"] = iteration
                        for previous in samples:
                            if (previous["fixtureRows"], previous["width"]) == (rows, width):
                                assert previous["fingerprint"] == sample["fingerprint"], "Cross-mode result mismatch"
                        samples.append(sample)
                        save()
                        print(f"rows={rows:<5} width={width:<4} {mode:16} {sample['elapsedMsPerOperation']:8.3f} ms "
                              f"{sample['allocatedBytesPerOperation']/1024:9.1f} KiB", flush=True)
        report["status"] = "complete"
        report["finishedUtc"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        save()
    except BaseException as error:
        report["status"], report["error"] = "failed", str(error)
        save()
        raise
    print(f"Saved {len(samples)} validated samples to {args.output}")


if __name__ == "__main__":
    main()
