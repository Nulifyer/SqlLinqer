# SQL Linqer

Build typed SQL queries that return nested C# models.

Select a customer and their orders, a user and their permissions, or a page with its total count.
SqlLinqer uses your model mappings to build parameterized SQL and populate the selected objects and collections.
Use C# members for filters, projections, and explicit writes against your existing schema.

Supports SQL Server, Azure SQL, PostgreSQL, MySQL, SQLite, and DuckDB. Targets .NET Standard 2.0.

[NuGet package](https://www.nuget.org/packages/SqlLinqer/) · [Get started](SqlLinqer/README.md) · [Documentation](docs/README.md) · [Choosing SqlLinqer](docs/choosing-sqllinqer.md)

## Load the relationships your endpoint needs

Filter users by their manager and load a page with each user's permissions:

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

`users` is a `List<User>` with each user's `Permissions` populated, loaded in one database command.
SqlLinqer binds the filter value as a SQL parameter and maps the nested database JSON into your models.
Use `ExecuteWithTotalCount()` when you also need the unpaged total; it adds a separate count command.
The example uses the [sample models](docs/getting-started/model-mapping.md) and a [configured connector](docs/getting-started/connections.md).
Import `SqlLinqer` for query factories, operators, and response helpers.

## Why SqlLinqer?

- Select related objects and collections through mapped members. SqlLinqer populates the graph, including empty collections and missing objects.
- Build search queries incrementally with optional filters, grouped conditions, sorting, and paging. Values become SQL parameters.
- Reuse table, column, key, and relationship mappings for selects, aggregate reports, inserts, updates, and deletes.
- Inspect generated SQL and bindings with `Render()`. Pass your own transaction when several operations belong together.
- Share a model between SQL loading and API serialization. API JSON attributes do not change the database mappings.
- Choose buffered synchronous or asynchronous results, or experimental async streaming of complete root models.

SqlLinqer fits applications that build search endpoints, nested API responses, and reports over an existing database.
You manage the schema and issue writes explicitly. SqlLinqer handles SQL generation, parameter binding, and result loading.
Ordinary model classes work with `connector.BeginSelect<T>()` and `SelectQuery<T>`; the model base classes provide convenient `BeginSelect` and write helpers.
See [choosing SqlLinqer](docs/choosing-sqllinqer.md) for tradeoffs with Dapper, SqlKata, LINQ to DB, and EF Core.

## Start with your database

```sh
dotnet add package SqlLinqer
```

Install your database's ADO.NET provider, [create a connector](docs/getting-started/connections.md), and [map your models](docs/getting-started/model-mapping.md).
The [NuGet README](SqlLinqer/README.md) walks through relationships, projections, inserts, updates, and deletes with one example model.

The NuGet page contains the latest published release. This repository also includes development features, including experimental result streaming.
Use the documentation at your package's [release tag](https://github.com/Nulifyer/SqlLinqer/tags) when API versions differ.

Try the [runnable minimal API](samples/SqlLinqer.MinimalApi/README.md) for filtering, paging, response helpers, DI, and logs. The optional DI and diagnostics packages are development projects in this repository.

## Compare on a workload you can reproduce

The [comparison suite](SqlLinqer.Comparisons/README.md) runs SqlLinqer, Dapper, SqlKata with Dapper, LINQ to DB, and EF Core against the same SQLite fixtures.
It checks flat reads, nested collections, pages with totals, and streaming, and retains executed SQL and raw measurements.
Use it to compare latency, managed allocations, and query strategies. SQLite results do not establish performance on a remote SQL server.

## Explore the library

The [documentation index](docs/README.md) groups the guides by task:

- [Getting started](docs/README.md#getting-started): connections and model mappings.
- [Queries](docs/README.md#queries): selects, writes, relationships, [stored procedures](docs/queries/stored-procedures.md), and SQL inspection.
- [Execution and results](docs/README.md#execution-and-results): async methods, transactions, response handling, conversions, and streaming.

## Work on the project

To change the library or publish a package, see the [contributor documentation](docs/contributing/README.md).
