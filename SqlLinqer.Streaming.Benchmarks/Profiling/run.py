import argparse
import hashlib
import json
import pathlib
import subprocess
from capture import capture, convert

root = pathlib.Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser()
p.add_argument("--layers", nargs="+", choices=["model", "capture", "render", "map"], default=["model", "capture", "render", "map"])
p.add_argument("--seconds", type=int, default=12)
p.add_argument("--output", type=pathlib.Path, required=True)
p.add_argument("--allocations", action="store_true")
a = p.parse_args()
if not 1 <= a.seconds <= 120:
    p.error("seconds must be in 1..120")
tool = root / "TestResults/profiling/tools/dotnet-trace"
if not tool.exists():
    raise RuntimeError("Install dotnet-trace 10.0.750501 at TestResults/profiling/tools; see README.md")
subprocess.run(["dotnet", "build", str(root / "SqlLinqer.Tests"), "-c", "Release", "-v", "quiet"], check=True)
assembly = root / "SqlLinqer.Tests/bin/Release/net10.0/SqlLinqer.Tests.dll"
a.output.mkdir(parents=True, exist_ok=True)
manifest = {"tool": subprocess.check_output([str(tool), "--version"], text=True).strip(),
    "coreSha256": hashlib.sha256((assembly.parent / "SQLLinqer.dll").read_bytes()).hexdigest(),
    "testSha256": hashlib.sha256(assembly.read_bytes()).hexdigest(), "seconds": a.seconds, "allocations": a.allocations,
    "metric": "EventPipe sampled managed thread time; not precise CPU time or invocation counts", "layers": []}
for layer in a.layers:
    result = capture(["dotnet", "exec", "--fx-version", "10.0.13", str(assembly), "--profile-layer", layer, str(a.seconds)],
                     [str(tool)], a.output, layer, a.seconds, allocations=a.allocations)
    convert(tool, a.output, layer)
    manifest["layers"].append(result)
    print(layer, result["operations"], "operations", flush=True)
(a.output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
