# SQL Linqer

Build parameterized SQL queries around your C# models.

SqlLinqer maps tables, columns, and relationships to C# members. Use those members to filter rows, load related models,
build aggregate projections, and write changes. The same mappings carry through each query.

Supports SQL Server, Azure SQL, PostgreSQL, MySQL, SQLite, and DuckDB. Targets .NET Standard 2.0.

[NuGet package](https://www.nuget.org/packages/SqlLinqer/) · [Documentation](docs/README.md) · [Get started](docs/getting-started/connections.md)

## Describe the result you need

Get a page of users managed by Ada, with their manager and permissions already loaded:

```csharp
var response = User.BeginSelect(connector)
    .SelectRootColumns()
    .Select(user => user.Manager)
    .Select(user => user.Permissions)
    .Where(user => user.Manager.Name, "Ada Lovelace", SqlOp.EQ)
    .OrderBy(user => user.Name, SqlDir.ASC)
    .ThenBy(user => user.Id, SqlDir.ASC)
    .Page(1, 20)
    .ExecuteWithTotalCount();

if (response.State != ResponseState.Valid)
    throw response.Error;

var users = response.Result;
var totalMatches = response.TotalResults;
```

`Result` contains typed `User` models and their selected relationships. `TotalResults` counts the matching users before paging.
The example uses the [sample models](docs/getting-started/model-mapping.md) and a [configured connector](docs/getting-started/connections.md).
Import `SqlLinqer` for operators and response states.

## Why SqlLinqer?

- **Keep mappings with the model.** Define table names, column names, keys, and relationships once. Query expressions use C# members throughout.
- **Choose what to load.** Select individual columns, nested models, or anonymous projections. Add filters, ordering, and paging to the same query builder.
- **Compose queries from application input.** Add optional conditions as you build a search. The renderer binds query values as parameters.
- **Use your database provider.** Create a connector from an ADO.NET connection or a connection factory. Pass your own transaction when several operations belong together.
- **Reuse models across SQL and APIs.** Database loading follows SqlLinqer mappings. API JSON attributes and converters control API serialization independently.
- **Choose how to consume results.** Use `Execute()` or `ExecuteAsync()` for buffered results. Experimental `ExecuteAsyncStream()` yields models as you enumerate them.

SqlLinqer fits applications that want explicit query construction over an existing database schema.
You manage the schema and choose the provider. SqlLinqer handles query rendering, parameter binding, and mapped result loading.
Database-specific capabilities and limits are covered in the guides.

## Start with your database

```sh
dotnet add package SqlLinqer
```

Install your database's ADO.NET provider, [create a connector](docs/getting-started/connections.md), and [map your models](docs/getting-started/model-mapping.md).
The [NuGet README](SqlLinqer/README.md) walks through relationships, projections, inserts, updates, and deletes with one example model.

The NuGet page contains the latest published release. This repository also includes development features, including experimental result streaming.

## Explore the library

The [documentation index](docs/README.md) groups the guides by task:

- [Getting started](docs/README.md#getting-started): connections and model mappings.
- [Queries](docs/README.md#queries): selects, writes, relationships, [stored procedures](docs/queries/stored-procedures.md), and SQL inspection.
- [Execution and results](docs/README.md#execution-and-results): async methods, transactions, response handling, conversions, and streaming.

## Work on the project

To change the library or publish a package, see the [contributor documentation](docs/contributing/README.md).
