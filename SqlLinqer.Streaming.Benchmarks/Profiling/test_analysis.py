import json
import pathlib
import tempfile
import unittest
from analyze import cpu_stacks, group, relevant, summarize, thread_stacks


class AnalysisChecks(unittest.TestCase):
    def test_recursive_frames_do_not_double_count_inclusive_share(self):
        core = "SQLLinqer!SqlLinqer.Compilation.SnapshotGraph.Copy(class System.Object)"
        result = summarize("capture", [(10, [core, core, "Runtime.Clone"]), (5, ["Idle.Wait"])], "samples")
        self.assertEqual(result["allWeight"], 15)
        self.assertEqual(result["selectedWeight"], 10)
        self.assertEqual(result["hotFunctions"][0]["inclusivePercent"], 100)
        self.assertEqual(next(f for f in result["hotFunctions"] if f["name"] == "Runtime.Clone")["selfPercent"], 100)

    def test_parameter_type_does_not_choose_the_declaring_layer(self):
        symbol = "object [SqlLinqer.Tests] SqlLinqer.Tests.Helper::Other(class [SQLLinqer]SqlLinqer.Components.Native.NativeResultPlan)"
        self.assertFalse(relevant(symbol))
        self.assertEqual(group(symbol), "Harness / reader setup")
        self.assertEqual(group("Microsoft.Data.SqlClient!Microsoft.Data.SqlClient.SqlDataReader.Read()"), "SQL client")

    def test_thread_time_and_synthetic_marker(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "trace.json"
            path.write_text(json.dumps({"shared": {"frames": [{"name": "A"}, {"name": "CPU_TIME"}]},
                "profiles": [{"type": "evented", "unit": "milliseconds", "startValue": 0, "events": [
                    {"type": "O", "frame": 0, "at": 0}, {"type": "O", "frame": 1, "at": 0},
                    {"type": "C", "frame": 1, "at": 8}, {"type": "C", "frame": 0, "at": 8}]}]}))
            self.assertEqual(list(thread_stacks(path)), [(8, ["A"])])

    def test_perf_map_range_and_leaf_to_root_order(self):
        with tempfile.TemporaryDirectory() as directory:
            base = pathlib.Path(directory)
            (base / "perf-42.map").write_text("0x1000 10 object [SQLLinqer] SqlLinqer.Owner::Parent()\n0x2000 10 object [SQLLinqer] SqlLinqer.Owner::Leaf()\n")
            (base / "map.perf.worker.log").write_text('{"pid":42}\n')
            path = base / "map.perf.script.txt"
            path.write_text("dotnet 42 1.0: cpu-clock:u:\n\t2001 [unknown] (/memfd:doublemapper (deleted))\n\t1001 [unknown] (/memfd:doublemapper (deleted))\n\n"
                            "dotnet 42 2.0: cpu-clock:u:\n\t2010 [unknown] (/memfd:doublemapper (deleted))\n\n")
            stacks = list(cpu_stacks(path))
            self.assertIn("Parent", stacks[0][1][0]); self.assertIn("Leaf", stacks[0][1][1])
            self.assertTrue(stacks[1][1][0].startswith("[unresolved native:"))


if __name__ == "__main__": unittest.main()
