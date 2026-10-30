"""Summarize full captured stacks; retain original traces for independent review."""
import argparse
import bisect
import collections
import json
import pathlib
import re

SYNTHETIC = {"CPU_TIME", "UNMANAGED_CODE_TIME"}


def relevant(name, sql=False):
    return (name.startswith("SQLLinqer!") or bool(re.search(r" \[SQLLinqer\] SqlLinqer\.[^ ()]+::", name))
        or (sql and name.startswith("Microsoft.Data.SqlClient!"))
        or bool(re.search(r"ProfilingWorkloads::(?:Map|Model)\(", name))
        or "ProfilingWorkloads::Graph(" in name)


def thread_stacks(path):
    data = json.loads(path.read_text())
    names = [f["name"] for f in data["shared"]["frames"]]
    for profile in data["profiles"]:
        if profile["unit"] != "milliseconds" or profile["type"] != "evented":
            raise ValueError("Expected evented millisecond profiles")
        stack = []; last = profile["startValue"]
        for event in profile["events"]:
            weight = event["at"] - last
            if weight > 0 and stack:
                yield weight, [names[i] for i in stack if names[i] not in SYNTHETIC]
            last = event["at"]
            if event["type"] == "O": stack.append(event["frame"])
            elif stack.pop() != event["frame"]: raise ValueError("Unbalanced stack")
        if stack: raise ValueError("Unclosed stack")


def cpu_stacks(path):
    # perf's memfd:doublemapper frames are not automatically symbolicated by
    # this perf build. Resolve captured instruction addresses against the
    # runtime's own perf map, checking every address against its range.
    maps = {}
    for file in path.parent.glob("perf-*.map"):
        entries = []
        for line in file.read_text().splitlines():
            start, size, name = line.split(" ", 2)
            entries.append((int(start, 16), int(size, 16), name))
        entries.sort()
        maps[file.stem.split("-")[1]] = ([e[0] for e in entries], entries)
    # Map filename uses process ID; perf headers contain thread ID. All
    # managed call addresses in one capture belong to its single worker.
    if len(maps) < 1: raise ValueError("Missing .NET perf map")
    ready = json.loads(path.with_name(path.name.replace(".perf.script.txt", ".perf.worker.log")).read_text().splitlines()[0])
    starts, entries = maps[str(ready["pid"])]
    frames = []
    for line in path.read_text().splitlines() + [""]:
        if not line.strip():
            if frames: yield 1, list(reversed(frames))
            frames = []
        elif line.startswith("\t"):
            match = re.match(r"\s*([0-9a-f]+)\s+(.+?)\s+\((.*)\)$", line)
            if not match: continue
            address = int(match[1], 16); symbol = match[2]; library = match[3]
            index = bisect.bisect_right(starts, address) - 1
            if index >= 0 and address < entries[index][0] + entries[index][1]:
                symbol = entries[index][2]
            elif symbol == "[unknown]":
                symbol = "[unresolved native: " + pathlib.Path(library).name + "]"
            frames.append(symbol)


def group(name):
    # Classify the declaring method, not type names embedded in its parameters.
    if "!" in name:
        name = name.split("!", 1)[1].split("(", 1)[0]
    else:
        owner = re.search(r" \[[^\]]+\] ([^ ()]+)::([^ (]+)\(", name)
        if owner: name = owner[1] + "::" + owner[2]
    if "System.Data." in name: return "DataTable reader"
    if "SnapshotGraph" in name: return "Snapshot copying"
    if "DbValueConversion" in name: return "Value conversion"
    if any(s in name for s in ["NativeModelBinding", "SqlModelType", "NativeModelType", "NativeCollectionBinding"]): return "CLR construction / collections"
    if any(s in name for s in ["NativeMaterializer", "NativeObjectState"]): return "Graph assembly"
    if "Components.Native" in name: return "Native protocol / binding"
    if "Compilation" in name: return "SQL compilation"
    if "Components.Modeling" in name or "SqlLinqer.Modeling" in name: return "Model metadata"
    if name.startswith("Microsoft.Data.SqlClient."): return "SQL client"
    if name.startswith("SqlLinqer.Tests."): return "Harness / reader setup"
    if name.startswith("SqlLinqer."): return "Other library work"
    return None


def summarize(name, stacks, metric):
    tree = {"name": "Selected library/provider stacks", "weight": 0, "children": {}}
    inclusive = collections.Counter(); exclusive = collections.Counter(); groups = collections.Counter()
    all_weight = selected_weight = poll_weight = unresolved_weight = 0; intervals = 0
    for weight, frames in stacks:
        all_weight += weight
        first = next((i for i, frame in enumerate(frames) if relevant(frame, name.startswith("sqlserver"))), None)
        if first is None: continue
        frames = frames[first:]; selected_weight += weight; intervals += 1
        exclusive[frames[-1]] += weight
        for frame in set(frames): inclusive[frame] += weight
        groups[next((g for g in (group(f) for f in reversed(frames)) if g), "Other")] += weight
        if "PollGC" in frames[-1]: poll_weight += weight
        if frames[-1].startswith("[unresolved native:") or frames[-1] == "?!?": unresolved_weight += weight
        node = tree; node["weight"] += weight
        for frame in frames:
            node = node["children"].setdefault(frame, {"name": frame, "weight": 0, "children": {}})
            node["weight"] += weight
    if not selected_weight: raise ValueError("No library/provider stacks selected for " + name)

    def prune(node):
        small = sum(c["weight"] for c in node["children"].values() if c["weight"] < selected_weight / 1000)
        children = [prune(c) for c in node["children"].values() if c["weight"] >= selected_weight / 1000]
        if small: children.append({"name": "[paths below 0.1%]", "weight": small, "children": []})
        return {"name": node["name"], "weight": node["weight"], "children": sorted(children, key=lambda c: -c["weight"])}

    return {"name": name, "metric": metric, "allWeight": all_weight, "selectedWeight": selected_weight,
        "intervals": intervals, "pollGcPercent": poll_weight / selected_weight * 100,
        "unresolvedLeafPercent": unresolved_weight / selected_weight * 100,
        "groups": [{"name": g, "percent": w / selected_weight * 100} for g, w in groups.most_common()],
        "hotFunctions": [{"name": f, "selfPercent": exclusive[f] / selected_weight * 100,
                          "inclusivePercent": w / selected_weight * 100} for f, w in inclusive.most_common()],
        "tree": prune(tree)}


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--thread-time", type=pathlib.Path, required=True)
    p.add_argument("--cpu", type=pathlib.Path, required=True)
    p.add_argument("--output", type=pathlib.Path, required=True)
    a = p.parse_args(); profiles = []
    for path in sorted(a.cpu.glob("*.perf.script.txt")):
        profiles.append(summarize(path.name.split(".")[0] + " (CPU)", cpu_stacks(path), "on-CPU samples"))
    for path in sorted(a.thread_time.glob("*.speedscope.json")):
        profiles.append(summarize(path.name.split(".")[0] + " (managed)", thread_stacks(path), "sampled managed thread time (ms)"))
    a.output.parent.mkdir(parents=True, exist_ok=True)
    a.output.write_text(json.dumps({"profiles": profiles}, separators=(",", ":")) + "\n")
    for profile in profiles:
        print(profile["name"], profile["groups"][:5], "GC-poll self%", round(profile["pollGcPercent"], 1))
