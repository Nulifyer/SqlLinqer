# SqlLinqer

**Typed SQL. Complete C# models.**

Build search endpoints, nested API responses, and reports over the database you already have. SqlLinqer turns mapped C# members into parameterized SQL and loads the objects and collections you select.

[Install on NuGet](https://www.nuget.org/packages/SqlLinqer/) · [Quick start](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/getting-started/first-query.md) · [Examples and guides](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/README.md)

## Load the result your endpoint needs

```csharp
var users = connector.BeginSelect<User>()
    .SelectRootColumns()
    .Select(user => user.Permissions)
    .Where(user => user.Manager.Name, "Ada Lovelace", SqlOp.EQ)
    .OrderBy(user => user.Id, SqlDir.ASC)
    .Page(1, 20)
    .Execute()
    .GetResultOrThrow();
```

Get a `List<User>` with each user's selected permissions already populated. Filter through relationships, request a page with its total count, or shape an aggregate report with the same model mappings. Start with the [sample models](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/getting-started/model-mapping.md) and a [configured connector](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/getting-started/connections.md).

## Spend less time on data-access plumbing

- **Return nested results.** Select related objects and collections directly through mapped C# members.
- **Build flexible queries.** Add optional filters, sorting, paging, and aggregate projections as your endpoint grows.
- **Stream complete rows.** Process each model with its selected nested data and stop when you have what you need.
- **Keep control.** Inspect parameterized SQL, share your transactions, and issue explicit inserts, updates, and deletes.
- **Fit your application.** Use ordinary model classes, optional dependency injection, and optional logs and tracing.

Supports SQL Server, Azure SQL, PostgreSQL, MySQL, SQLite, and DuckDB. The .NET Standard 2.1 core has no package dependencies; the .NET Standard 2.0 asset retains async-interface compatibility. Install your database's ADO.NET provider separately.

```sh
dotnet add package SqlLinqer
```

[Run the quick start](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/getting-started/first-query.md), try the [Minimal API sample](https://github.com/Nulifyer/SqlLinqer/blob/master/samples/SqlLinqer.MinimalApi/README.md), or [choose a guide by task](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/README.md).

## Make it your own

Use SqlLinqer for one query alongside your existing data access, or reuse your mappings across an application. You own the schema, connections, and transaction boundaries.

Streaming and query performance depend on your provider, indexes, selected fields, and nested collection sizes. The [streaming guide](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/execution/result-streaming.md) shows cancellation and memory considerations. These guides describe the development tree; use the matching [release documentation](https://github.com/Nulifyer/SqlLinqer/tags) for a published version.

[Contribute](https://github.com/Nulifyer/SqlLinqer/blob/master/eng/README.md) · [MIT license](https://github.com/Nulifyer/SqlLinqer/blob/master/LICENSE)
