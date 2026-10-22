# Recorded SQLite comparisons

This directory retains a local measurement run alongside its raw evidence. It is a baseline for these queries and this machine.

Recorded on October 22, 2026, with an AMD Ryzen 5 7640U, Linux x64, .NET 10.0.13, SDK 10.0.402, and SQLite 3.53.3.
The completed run contains 240 validated process samples across 48 cases.

- [Interactive report](sqlite-2026-10-22.html): select a fixture size, workload, and time or allocation metric.
- [Raw report](sqlite-2026-10-22.json): every validated sample, actual SQL and parameter bindings, package versions, environment, and source identity.
- [Methodology and reproduction](../README.md): workloads, query strategies, timing boundaries, and limitations.

## Reproduce the run

From the repository root:

```sh
python3 SqlLinqer.Comparisons/run.py \
  --rows 1000 10000 --iterations 5 --operations 5 --warmup 5 \
  --output TestResults/comparisons-sqlite.json
python3 SqlLinqer.Comparisons/report.py TestResults/comparisons-sqlite.json \
  --output TestResults/comparisons-sqlite.html
```

Each fixture size has six query strategies and four workloads, with five independent process samples per case.
Every process warms its full workload and then measures five operations.
The 10,000-customer fixture returns 6,667 active customers for full reads. Its paged workload returns 50 roots after a 100-root offset, plus the unpaged total.
Graph results include both child collections, with the same selected fields and ordering across libraries.

## What this run showed

For the 10,000-customer fixture, SqlLinqer's flat buffered read had a median of 26.651 ms per operation.
Dapper measured 12.459 ms and LINQ to DB measured 12.884 ms.
SqlLinqer's flat stream also took longer in this fixture. These results support investigating its flat-read execution costs.

The full graph took 99.814 ms with SqlLinqer, compared with 40.296 ms for Dapper, 75.092 ms for LINQ to DB,
162.457 ms for EF single-query loading, and 87.077 ms for EF split-query loading.
SqlLinqer allocated 12,760.9 KiB per graph operation, below EF's 34,034.4 KiB and 22,428.4 KiB respectively.
The JSON strategy offered an allocation benefit against these EF queries, with different latency tradeoffs.

The 50-root graph page with its total took 3.469 ms with SqlLinqer, 2.128 ms with Dapper, and 2.590 ms with LINQ to DB.
SqlLinqer's page timing range overlapped both EF variants in this run.
The interactive report includes every library, workload, fixture size, and observed timing range, including slower SqlLinqer cases.

The SQL strategies differ deliberately. Dapper batches its graph result sets; EF Core has both single-query and split-query variants.
A command count of one can represent several SQL statements. Timing includes query construction, connection opening, materialization, identical result consumption, and disposal.

## Limits

These are warm local SQLite reads with indexed relationships and low child fanout. They do not establish a universal library ranking.
The run measures no network latency, transferred bytes, database server CPU, writes, cold startup, or concurrent requests.
Managed allocations exclude SQLite native memory. Connection pooling is disabled for every adapter.
SqlLinqer is measured from the working project, not the published NuGet binary.
The raw report records the source commit, dirty state, and SHA-256 of the measured source and runner.

The summary medians describe five process-level means, not a tail-latency distribution.
See the raw samples and ranges before interpreting small differences.

The recorded baseline predates the additive adoption helpers and diagnostics hooks now in this worktree. Its source hash identifies the measured implementation; rerun the suite to measure a later checkout.
