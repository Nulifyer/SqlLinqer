# Flat reader controls

This maintained runner compares handwritten typed reader access, tabular loading, native model loading, and framework JSON controls on SQLite. It uses the current core API. The JSON controls are benchmark-only.

```sh
python3 SqlLinqer.Comparisons.Investigations/run.py --verify --output TestResults/flat-verify.json
python3 SqlLinqer.Comparisons.Investigations/run.py --output TestResults/flat-measurements.json
```

The runner validates flat DTO fields and nulls against independent fixture rules, uses shuffled isolated processes, and preserves raw samples. Connection lifetime, SQL execution, mapping, and common fingerprinting are measured. Fixture creation and SQL preparation are outside timing.

The `native` mode is a handwritten control, `tabular` and `model` use the library, and `json-standard`/`stored-standard` use framework parsing. Stored JSON has a different storage layout; subtracting its timing does not isolate database formatting cost. These measurements do not establish nested-object or remote-server performance.

Keep generated reports in ignored `TestResults/` or `results/` directories. See [performance methodology](performance.md) before comparing measurements from different code versions.
