# SQL Linqer

SqlLinqer builds SQL queries from C# model mappings and expressions. It supports typed selects, nested relationships, aggregates, and writes.
The library targets .NET Standard 2.0. Install the database provider in your application and create the database schema before querying.

Set `ConnectorOptions.UseSqlModelJsonContract = true` to load SQL results independently of API JSON attributes and converters.
This opt-in mode lets one class use SqlLinqer attributes for database mappings and JSON attributes for API serialization. The default preserves existing JSON loading behavior.

## Build and check

The Core demo targets .NET 10. The Framework demo targets .NET Framework 4.8 and uses Npgsql 4.1.14 for compatibility.
The library targets .NET Standard 2.0.

```sh
dotnet build SQLTest/SQLTest.csproj
dotnet build SQLTestFramework/SQLTestFramework.csproj
dotnet run --project SqlLinqer.RegressionTests/SqlLinqer.RegressionTests.csproj
dotnet run --project SqlLinqer.SqliteTests/SqlLinqer.SqliteTests.csproj
dotnet run --project SqlLinqer.DuckDbTests/SqlLinqer.DuckDbTests.csproj
dotnet run --project SqlLinqer.AzureSqlTests/SqlLinqer.AzureSqlTests.csproj
```

The regression checks use fake database connections and do not require a running database.
The demos require the employee database described in their connection settings.

To run integration checks against real PostgreSQL 18 and MySQL 8.4 containers:

```sh
bash TestDBs/test-containers.sh --engine docker
# If Docker requires sudo, run this from your terminal:
bash TestDBs/test-containers.sh --engine docker --sudo
# Podman is also supported:
bash TestDBs/test-containers.sh --engine podman
# If host connections to Docker ports time out, keep tests inside containers:
bash TestDBs/test-containers.sh --engine docker --containerized
```

The runner needs .NET 10 and access to Docker or Podman. It creates disposable databases on automatically assigned localhost ports.
With `--containerized`, it runs tests in a .NET 10 runtime container on the databases' isolated network. This mode does not publish ports.
The runner removes its containers and anonymous volumes after the checks. Only container commands use sudo.

The runner saves test results to a temporary log and prints its path.
These checks cover PostgreSQL and MySQL. They do not cover SQL Server.

## Guides

- [Publish a NuGet release](docs/RELEASING.md)
- [Set up a connection](docs/SETUP_A_CONNECTION.md)
- [Inject a connector](docs/SETUP_A_CONNECTION.md#inject-a-connector)
- [Build a data model](docs/BUILDING_A_DATAMODEL.md)
- [Insert rows](docs/QUERY_CREATE.md)
- [Select rows](docs/QUERY_READ.md)
- [Update rows](docs/QUERY_UPDATE.md)
- [Delete rows](docs/QUERY_DELETE.md)
- [Handle query responses](docs/RESPONSES.md)
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
    .Execute(connector);

if (response.State == ResponseState.Valid)
{
    var users = response.Result;
}
else
{
    System.Diagnostics.Debug.WriteLine(response.Error?.Message);
}
```
