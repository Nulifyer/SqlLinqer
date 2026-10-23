# Minimal API example

Run a small HTTP API with connector-based queries, response helpers, DI registration, structured logs, paging totals, and async streaming. It uses ordinary mapped classes: `JsonPropertyName("displayName")` controls the API JSON while `SqlColumn("user_name")` controls SQL.

From the repository root, with the .NET 10 SDK:

```sh
dotnet run --project samples/SqlLinqer.MinimalApi -- --urls http://localhost:5080
```

Try `/users`, `/users?name=Ada`, `/users/page?page=1&pageSize=2`, and `/users/stream`. Paging accepts positive pages and page sizes from 1 through 100. This is a runnable adoption example using development project references, including the two optional integrations; it does not require published versions of those packages.

The host owns a temporary SQLite database seeded with three users. Each query gets a fresh connection, and host disposal deletes the database. Streaming keeps its connection until enumeration ends. The API passes request cancellation to database execution.

Run the HTTP checks from the repository root:

```sh
python3 samples/SqlLinqer.MinimalApi/check.py
```

The checks build the sample, start an owned process on a dynamic loopback port, verify filtering, API property names, pages and totals, streaming, and invalid input, then stop the process.

See [connections](../../docs/getting-started/connections.md), [response helpers](../../docs/execution/responses-and-errors.md), and [diagnostics](../../docs/execution/diagnostics.md) for the contracts used here.

## Debug and production diagnostics

The sample reads `SqlLinqer:Diagnostics` for each operation and uses logger category `SqlLinqer`. Production defaults retain Error logs with SQL, values and exceptions off. Development settings select Trace and explicitly enable SQL logs, while leaving values and activity SQL off. Build configuration does not select those runtime settings.

Set `SqlLinqer__Diagnostics__IncludeParameterValues=true` only when values are required for an investigation. Use `IncludeSqlInActivities` separately for trace export. See the [diagnostics guide](../../docs/execution/diagnostics.md) for the full contract and migration.
