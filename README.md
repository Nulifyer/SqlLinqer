# SQL Linqer

SqlLinqer builds SQL queries from C# model mappings and expressions. It supports typed selects, nested relationships, aggregates, and writes.
The library targets .NET Standard 2.0. Install the database provider in your application and create the database schema before querying.

Set `ConnectorOptions.UseSqlModelJsonContract = true` to load SQL results independently of API JSON attributes and converters.
This opt-in mode lets one class use SqlLinqer attributes for database mappings and JSON attributes for API serialization. The default preserves existing JSON loading behavior.

## Build and check

`SqlLinqer.Tests.Net10` tests .NET 10 compatibility. `SqlLinqer.Tests.NetFramework48` tests .NET Framework 4.8 compatibility and uses Npgsql 4.1.14.
The library targets .NET Standard 2.0.

```sh
dotnet build SqlLinqer.Tests.Net10/SqlLinqer.Tests.Net10.csproj
dotnet build SqlLinqer.Tests.NetFramework48/SqlLinqer.Tests.NetFramework48.csproj
dotnet run --project SqlLinqer.Tests
```

All automated suites use [SqlLinqer.Tests](SqlLinqer.Tests/README.md).
For the complete repeatable run, including real PostgreSQL, MySQL, and SQL Server databases:

```sh
bash TestDBs/test-containers.sh
# If Docker requires sudo, run this from your terminal:
bash TestDBs/test-containers.sh --sudo
```

The runner builds once, runs every suite in an isolated container environment, saves one result log, and removes its containers and volumes.
It supports Docker or Podman. Use `--host` to run tests on the host with random localhost database ports.
Failures remain nonzero, including the currently known generated-type TVP insert deadlock on SQL Server.
Direct Azure SQL testing is optional and uses the same shared server fixtures.

`dotnet run --project SqlLinqer.Tests` runs local regression, SQLite, DuckDB, and offline provider checks without server connections.
Use `-- --suite regression`, `sqlite`, `duckdb`, `sql-server`, or `integration` for focused checks.
The two compatibility test programs require the employee database described in their connection settings.

## Guides

- [Sync and async execution](docs/EXECUTION.md)
- [Query snapshots and compilation](docs/QUERY_COMPILATION.md)

- [Publish a NuGet release](docs/RELEASING.md)
- [Set up a connection](docs/SETUP_A_CONNECTION.md)
- [Inject a connector](docs/SETUP_A_CONNECTION.md#inject-a-connector)
- [Build a data model](docs/BUILDING_A_DATAMODEL.md)
- [Insert rows](docs/QUERY_CREATE.md)
- [Select rows](docs/QUERY_READ.md)
- [Update rows](docs/QUERY_UPDATE.md)
- [Delete rows](docs/QUERY_DELETE.md)
- [Handle query responses](docs/RESPONSES.md)
- [Call typed stored procedures](docs/CALLABLES.md)
- [Run several writes in one transaction](docs/TRANSACTIONS.md)
- [Change relationships](docs/QUERY_RELATIONSHIPS.md)

## Quick example

Use the `User` model from the [model guide](docs/BUILDING_A_DATAMODEL.md).
Configure a SQL Server connection string before running this example:

```csharp
using System.Linq;
using Microsoft.Data.SqlClient;
using SqlLinqer;
using SqlLinqer.Connections;

using var template = new SqlConnection(connectionString);
var connector = new ReplicatorConnector(template, DbFlavor.SqlServer);

var response = User
    .BeginSelect(connector)
    .Top(10)
    .SelectRootColumns()
    .Select(x => x.Permissions)
    .Where(x => x.Permissions.First().Name, "% admin", SqlOp.NOTLIKE)
    .OrderBy(x => x.Manager.Name, SqlDir.ASC)
    .ThenBy(x => x.Name, SqlDir.ASC)
    .Execute();

if (response.State == ResponseState.Valid)
{
    var users = response.Result;
}
else
{
    System.Diagnostics.Debug.WriteLine(response.Error?.Message);
}
```
