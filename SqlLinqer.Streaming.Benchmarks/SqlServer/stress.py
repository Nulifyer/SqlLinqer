"""Isolated released/current nested graph stress tests. Generated evidence stays under TestResults/."""
import argparse
import hashlib
import json
import os
import pathlib
import random
import secrets
import selectors
import shutil
import subprocess
import threading
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]
PROJECT = pathlib.Path(__file__).resolve().parent
FIXTURES = [
    dict(Name="control", Users=2000, Groups=2, Permissions=2, Grants=0, Width=0),
    dict(Name="many-roots", Users=50000, Groups=2, Permissions=2, Grants=0, Width=0),
    dict(Name="deep", Users=2000, Groups=8, Permissions=8, Grants=2, Width=0),
    dict(Name="wide-text", Users=500, Groups=4, Permissions=16, Grants=2, Width=512),
    dict(Name="giant-root", Users=1, Groups=100, Permissions=10000, Grants=0, Width=0),
]


def run_worker(directory, mode, fixture, env, output, timeout, prefix, container, trace=None):
    command = prefix + ["/dotnet/dotnet", str(directory / "SqlLinqer.Streaming.SqlServer.Benchmarks.dll"), "--stress", mode, str(fixture)]
    process = subprocess.Popen(command, cwd=ROOT, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE, text=True)
    collector = None
    ready = None
    peaks = []
    stopping = threading.Event()

    def rss():
        try:
            return int(pathlib.Path(f"/proc/{host_process_id}/statm").read_text().split()[1]) * os.sysconf("SC_PAGE_SIZE")
        except (FileNotFoundError, ProcessLookupError):
            return 0

    def sample():
        while not stopping.wait(0.005):
            peaks.append(rss())

    try:
        selector = selectors.DefaultSelector()
        selector.register(process.stdout, selectors.EVENT_READ)
        if not selector.select(timeout=timeout):
            raise TimeoutError("Worker warmup timed out")
        ready = json.loads(process.stdout.readline())
        selector.close()
        if not ready.get("ready"):
            raise RuntimeError("Missing worker gate")
        host_process_id = process.pid
        top = subprocess.check_output(["docker", "top", container, "-eo", "pid,args"], text=True)
        matches = [line.split()[0] for line in top.splitlines() if str(directory / "SqlLinqer.Streaming.SqlServer.Benchmarks.dll") in line and "--stress " + mode in line]
        if len(matches) != 1:
            raise RuntimeError("Cannot identify isolated worker PID for RSS sampling")
        host_process_id = int(matches[0])
        baseline_rss = rss()
        peaks.append(baseline_rss)
        if trace:
            tool = ROOT / "TestResults/nested-stress/tools/dotnet-trace"
            with trace.with_suffix(".collector.log").open("w") as log:
                collector = subprocess.Popen(prefix + [str(tool), "collect", "--process-id", str(ready["pid"]),
                    "--profile", "gc-verbose", "--output", str(trace)], stdout=log, stderr=subprocess.STDOUT)
            deadline = time.monotonic() + 30
            while not (trace.exists() and trace.stat().st_size > 32):
                if collector.poll() is not None or time.monotonic() > deadline:
                    raise RuntimeError("Allocation trace failed to attach")
                time.sleep(0.05)
        sampler = threading.Thread(target=sample, daemon=True)
        sampler.start()
        stdout, stderr = process.communicate("go\n", timeout=timeout)
        stopping.set()
        sampler.join()
        if collector:
            collector.wait(timeout=30)
        if process.returncode:
            raise RuntimeError(stderr.strip() or "Worker exited " + str(process.returncode))
        result = json.loads(stdout.strip().splitlines()[-1])
        result.update(status="passed", rssBeforeBytes=baseline_rss, sampledPeakRssBytes=max(peaks),
                      sampledPeakRssIncreaseBytes=max(peaks) - baseline_rss, rssSamplingIntervalMs=5,
                      instrumented=bool(trace))
        return result
    finally:
        stopping.set()
        # Killing docker exec alone can leave its remote worker alive after a timeout.
        if process.poll() is None and ready is not None:
            subprocess.run(["docker", "exec", "--user", str(os.getuid()), container,
                            "kill", "-KILL", str(ready["pid"])], capture_output=True)
        for child in (process, collector):
            if child is not None and child.poll() is None:
                child.kill()
                child.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline-core", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "TestResults/nested-stress/results.json")
    parser.add_argument("--cases", nargs="+", choices=[f["Name"] for f in FIXTURES])
    parser.add_argument("--runs", type=int, default=7)
    parser.add_argument("--modes", nargs="+", choices=["stream", "buffered", "first"], default=["stream", "buffered", "first"])
    parser.add_argument("--timeout", type=int, default=200)
    parser.add_argument("--trace", action="store_true", help="One instrumented allocation capture per version/case/mode; not timing evidence")
    distinct = parser.add_mutually_exclusive_group()
    distinct.add_argument("--distinct", action="store_true", help="Measure complete graph DISTINCT with the same validated fixture")
    distinct.add_argument("--nested-distinct", action="store_true", help="Measure a small DISTINCT sibling beside the full nested graph")
    parser.add_argument("--baseline-label", default="released")
    parser.add_argument("--candidate-label", default="native")
    parser.add_argument("--plans-only", action="store_true", help="Capture actual full-query plans separately from measurements")
    args = parser.parse_args()
    if not 1 <= args.runs <= 20:
        parser.error("runs must be 1..20")
    if args.baseline_label == args.candidate_label:
        parser.error("Version labels must differ")
    output = args.output.resolve()
    if output.exists():
        parser.error("Choose a fresh output path")
    output.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release", "-p:GeneratePackageOnBuild=false", "-v", "quiet"], check=True)
    candidate = PROJECT / "bin/Release/net10.0"
    baseline = output.parent / "baseline-worker"
    shutil.copytree(candidate, baseline, dirs_exist_ok=True)
    shutil.copy2(args.baseline_core, baseline / "SQLLinqer.dll")
    support = args.baseline_core.parent / "Microsoft.Bcl.AsyncInterfaces.dll"
    if support.is_file():
        shutil.copy2(support, baseline / support.name)
        deps = baseline / "SqlLinqer.Streaming.SqlServer.Benchmarks.deps.json"
        metadata = json.loads(deps.read_text())
        key = "Microsoft.Bcl.AsyncInterfaces/10.0.13"
        metadata["libraries"][key] = dict(type="reference", serviceable=False, sha512="")
        metadata["targets"][metadata["runtimeTarget"]["name"]][key] = {"runtime": {support.name: {}}}
        deps.write_text(json.dumps(metadata))
    runtime = ROOT / "TestResults/nested-stress/runtime"
    if not (runtime / "dotnet").exists():
        runtime.mkdir(parents=True, exist_ok=True)
        cid = subprocess.check_output(["docker", "create", "mcr.microsoft.com/dotnet/runtime:10.0"], text=True).strip()
        try:
            subprocess.run(["docker", "cp", cid + ":/usr/share/dotnet/.", str(runtime)], check=True)
        finally:
            subprocess.run(["docker", "rm", cid], check=True, capture_output=True)
    name = "sqllinqer-nested-stress-" + secrets.token_hex(4)
    password = "Aa1!" + secrets.token_hex(20)
    envfile = output.parent / ".server.env"
    envfile.write_text("ACCEPT_EULA=Y\nMSSQL_SA_PASSWORD=" + password + "\nMSSQL_MEMORY_LIMIT_MB=2048\n")
    envfile.chmod(0o600)
    results = dict(samples=[], method="Alternating isolated library workers; baseline and candidate assembly hashes and per-sample transports identify each implementation. Same fixture/SQL Server/runtime/validation. Compilation, warmup, connection and transaction establishment excluded. Timing includes validation and early cancellation/reader disposal. RSS externally sampled every 5ms, not managed allocation. Final retained bytes measured after forced GC, with buffered results kept alive. SQLClient byte counters, not TCP capture.",
                   coreSha256={v: hashlib.sha256((d / "SQLLinqer.dll").read_bytes()).hexdigest() for v, d in [(args.baseline_label, baseline), (args.candidate_label, candidate)]},
                   harnessSha256=hashlib.sha256((candidate / "SqlLinqer.Streaming.SqlServer.Benchmarks.dll").read_bytes()).hexdigest(),
                   fixtures=FIXTURES, runs=args.runs, instrumented=args.trace, distinct=args.distinct, nestedDistinct=args.nested_distinct, plans=[])
    try:
        subprocess.run(["docker", "run", "--detach", "--name", name, "--env-file", str(envfile), "--memory", "4g",
                        "--volume", str(ROOT) + ":" + str(ROOT) + ":ro", "--volume", str(runtime) + ":/dotnet:ro",
                        "--volume", str(output.parent) + ":" + str(output.parent) + ":rw", "mcr.microsoft.com/mssql/server:2022-latest"], check=True, capture_output=True)
        envfile.unlink()
        deadline = time.monotonic() + 120
        while "SQL Server is now ready for client connections" not in subprocess.check_output(["docker", "logs", name], stderr=subprocess.STDOUT, text=True):
            if time.monotonic() > deadline:
                raise TimeoutError("SQL Server startup")
            time.sleep(1)
        inspected = json.loads(subprocess.check_output(["docker", "inspect", name], text=True))[0]
        port = "1433"
        results["serverImageId"] = inspected["Image"]
        env = dict(os.environ, SQLLINQER_PROBE_SQLSERVER=f"Server=127.0.0.1,{port};Database=master;User Id=sa;Password={password};Encrypt=True;TrustServerCertificate=True;Connect Timeout=30;Pooling=False")
        # Fix the JIT regime for both versions. Warmup is identical and performed before each measurement gate.
        env["DOTNET_TieredCompilation"] = "0"
        prefix = ["docker", "exec", "--interactive", "--user", str(os.getuid()), "--env", "DOTNET_ROOT=/dotnet",
                  "--env", "DOTNET_TieredCompilation=0", "--env", "SQLLINQER_PROBE_SQLSERVER=" + env["SQLLINQER_PROBE_SQLSERVER"], name]
        for fixture in FIXTURES:
            if args.cases and fixture["Name"] not in args.cases:
                continue
            fixture = dict(fixture, Distinct=args.distinct, NestedDistinct=args.nested_distinct)
            path = output.parent / (fixture["Name"] + ".fixture.json")
            path.write_text(json.dumps(fixture))
            setup = subprocess.run(prefix + ["/dotnet/dotnet", str(candidate / "SqlLinqer.Streaming.SqlServer.Benchmarks.dll"), "--stress", "setup", str(path)],
                                   env=env, capture_output=True, text=True, timeout=args.timeout)
            if setup.returncode:
                raise RuntimeError(setup.stderr)
            print(setup.stdout.strip(), flush=True)
            if args.plans_only:
                for version, directory in [(args.baseline_label, baseline), (args.candidate_label, candidate)]:
                    plan = output.parent / f"{fixture['Name']}-{version}.sqlplan"
                    captured = subprocess.run(prefix + ["/dotnet/dotnet", str(directory / "SqlLinqer.Streaming.SqlServer.Benchmarks.dll"),
                        "--stress", "plan", str(path), str(plan)], env=env, capture_output=True, text=True, timeout=args.timeout)
                    if captured.returncode:
                        raise RuntimeError(captured.stderr)
                    results["plans"].append(dict(version=version, fixture=fixture, file=plan.name,
                        sha256=hashlib.sha256(plan.read_bytes()).hexdigest()))
                    output.write_text(json.dumps(results, indent=2) + "\n")
                    print(version, "actual plan captured", flush=True)
                continue
            for run in range(args.runs):
                modes = list(args.modes)
                random.Random(611 + run).shuffle(modes)
                for mode in modes:
                    versions = [(args.baseline_label, baseline), (args.candidate_label, candidate)]
                    if run % 2:
                        versions.reverse()
                    for version, directory in versions:
                        trace = output.parent / f"{fixture['Name']}-{version}-{mode}-{run}.nettrace" if args.trace else None
                        try:
                            result = run_worker(directory, mode, path, env, output, args.timeout, prefix, name, trace)
                        except Exception as error:
                            result = dict(status="failed", error=str(error), fixture=fixture, mode=mode)
                        result.update(version=version, run=run)
                        results["samples"].append(result)
                        output.write_text(json.dumps(results, indent=2) + "\n")
                        print(f"{fixture['Name']} {mode} {version} {run}: {result['status']} " +
                              (f"{result['elapsedMs']:.1f}ms {result['allocatedBytes']/1e6:.1f}MB allocated {result['sampledPeakRssBytes']/1e6:.1f}MB RSS" if result["status"] == "passed" else result["error"][:500]), flush=True)
        if any(s["status"] != "passed" for s in results["samples"]):
            raise RuntimeError("Some workers failed; see raw results")
    finally:
        envfile.unlink(missing_ok=True)
        subprocess.run(["docker", "rm", "--force", "--volumes", name], capture_output=True)


if __name__ == "__main__":
    main()
