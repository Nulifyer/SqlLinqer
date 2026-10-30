import argparse
import json
import pathlib

p = argparse.ArgumentParser()
p.add_argument("summary", type=pathlib.Path)
p.add_argument("output", type=pathlib.Path)
a = p.parse_args()
source = json.loads(a.summary.read_text())
frames = []; indexes = {}
def intern(name):
    if name not in indexes:
        indexes[name] = len(frames); frames.append(name)
    return indexes[name]
def tree(node):
    return {"f": intern(node["name"]), "w": node["weight"], "c": [tree(c) for c in node["children"]]}
profiles = [{"name": p["name"], "metric": p["metric"], "total": p["selectedWeight"],
    "poll": p["pollGcPercent"], "unresolved": p["unresolvedLeafPercent"], "groups": p["groups"],
    "tree": tree(p["tree"]), "hot": [{"f": intern(f["name"]), "self": f["selfPercent"], "inc": f["inclusivePercent"]}
        for f in p["hotFunctions"]]} for p in source["profiles"]]
data = json.dumps({"frames": frames, "profiles": profiles}, separators=(",", ":")).replace("<", "\\u003c")
template = pathlib.Path(__file__).with_name("flamegraph.html").read_text()
a.output.write_text(template.replace("__PROFILE_DATA__", data))
