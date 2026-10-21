# SQL Linqer

Build parameterized SQL queries around your C# models.

SqlLinqer maps tables, columns, and relationships to C# members. Use those members to filter rows, load related models,
build aggregate projections, and write changes. The same mappings carry through each query.

Supports SQL Server, Azure SQL, PostgreSQL, MySQL, SQLite, and DuckDB. Targets .NET Standard 2.0.

[NuGet package](https://www.nuget.org/packages/SqlLinqer/) · [Get started](docs/SETUP_A_CONNECTION.md) · [Model guide](docs/BUILDING_A_DATAMODEL.md)

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
The example uses the [sample models](docs/BUILDING_A_DATAMODEL.md) and a [configured connector](docs/SETUP_A_CONNECTION.md).
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

Install your database's ADO.NET provider, [create a connector](docs/SETUP_A_CONNECTION.md), and [map your models](docs/BUILDING_A_DATAMODEL.md).
The [NuGet README](SqlLinqer/README.md) walks through relationships, projections, inserts, updates, and deletes with one example model.

This development line targets 2.2.

The NuGet page contains the latest published release. This repository also includes development features, including experimental result streaming.

## Explore the library

- [Connections and dependency injection](docs/SETUP_A_CONNECTION.md)
- [Models, keys, and relationships](docs/BUILDING_A_DATAMODEL.md)
- [Selects, paging, and aggregate projections](docs/QUERY_READ.md)
- [Inserts](docs/QUERY_CREATE.md), [updates](docs/QUERY_UPDATE.md), and [deletes](docs/QUERY_DELETE.md)
- [Relationship changes](docs/QUERY_RELATIONSHIPS.md) and [transactions](docs/TRANSACTIONS.md)
- [Synchronous and asynchronous execution](docs/EXECUTION.md)
- [Experimental streaming and memory measurements](docs/RESULT_STREAMING.md)
- [Result conversions](docs/RESULT_CONVERSIONS.md) and [response handling](docs/RESPONSES.md)
- [Typed stored procedures](docs/CALLABLES.md)
- [Query snapshots and compilation](docs/QUERY_COMPILATION.md)
- [DuckDB support](docs/DUCKDB.md)

## Work on the project

See the [contributor guide](docs/CONTRIBUTING.md) for builds, tests, and source organization.
The [test guide](SqlLinqer.Tests/README.md) covers the complete container-backed suite and focused local runs.
Release maintainers can follow the [NuGet publishing guide](docs/RELEASING.md).
