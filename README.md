# SQL Linqer

### What is it used for?

This library is used to create application using a type of SQL database without the need to write queries as strings and maintain a strong set of data models that represent the data. This makes it much easier to develope complex database queries without the need of the developer to perform complex operations and it easier to update the code as all the developer needs to do is update the one data model and all queries will be updated.

### Why not just use Microsoft's Enity Framework (EF)?

This is more of a personal preference. They way this library behaves is difference than EF and might not have all of the functionality that EF does. EF can sometimes be cumbersome to handle as it forces a certain developer style on the stucture of the data and I find this style of use to be easier. This includes modification, extending the functionality, and allowing for more complex queries.

## Build and check

The Core demo targets .NET 10. The Framework demo targets .NET Framework 4.8 and uses Npgsql 4.1.14 for compatibility. The library continues to target .NET Standard 2.0.

Query results use `System.Text.Json`. Use JSON attributes from `System.Text.Json.Serialization`, such as `JsonIgnore` and `JsonPropertyName`, when customizing model loading. The loader supports public fields, private setters and default constructors, getter-only auto-properties, and string enums. It also accepts numeric booleans and database date/time strings.

```sh
dotnet build SQLTest/SQLTest.csproj
dotnet build SQLTestFramework/SQLTestFramework.csproj
dotnet run --project SqlLinqer.RegressionTests/SqlLinqer.RegressionTests.csproj
dotnet run --project SqlLinqer.SqliteTests/SqlLinqer.SqliteTests.csproj
```

The regression checks use fake database connections and do not require a running database. The demos require the employee database described in their connection settings.

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

The runner needs .NET 10 and access to Docker or Podman. It creates disposable databases on automatically assigned localhost ports. With `--containerized`, it runs the built tests in a .NET 10 runtime container sharing the databases' isolated network, without publishing ports. It removes its containers and anonymous volumes after the checks. Only container commands use sudo. Test results are saved to a temporary log whose path is printed by the runner. These checks cover PostgreSQL and MySQL; SQL Server is not included.

Returned rows and primary keys are supported for SQL Server, PostgreSQL, and SQLite. MySQL queries that request output columns throw `NotSupportedException` before execution. For MySQL writes without returned rows, use `Queries.Typed.InsertQuery<T>`, `UpdateQuery<T>`, or `DeleteQuery<T>` without output columns. Joined PostgreSQL updates and deletes require a primary key on the target model. Table-valued parameters are supported only for SQL Server.

SQLite 3.38 or newer with JSON is supported through `DbFlavor.Sqlite = 3`. The [SQLite setup and integration guide](SqlLinqer.SqliteTests/README.md) covers provider installation, schemas, shared memory, transaction ownership, types, and rejected operations. Its executable tests use actual file and shared-memory databases. The guide also includes a pinned SQLite 3.38.0 validation script.

When passing a transaction, the caller owns its commit, rollback, and disposal, including after a query fails. The connector disposes its commands and connections it creates itself.

## How to build a data model:

[Setup a connection](docs/SETUP_A_CONNECTION.md)

## How to build a data model:

[Building a data model](docs/BUILDING_A_DATAMODEL.md)

## How to query:

[Create/Insert Queries](docs/QUERY_CREATE.md)

[Read/Select Queries](docs/QUERY_READ.md)

[Update Queries](docs/QUERY_UPDATE.md)

[Delete Queries](docs/QUERY_DELETE.md)

## Quick Example

```C#
var connector = new ReplicatorConnector(new SqlConnection("<conn string>"), DbFlavor.SqlServer);

SqlResponse<List<User>> response = User
    .BeginSelect()
    .Top(10)
    .SelectRootColumns()
    .Select(x => x.Permissions)
    .Where(x => x.Permissions.First().name, "% admin", SqlOp.NOTLIKE)
    .OrderBy(x => x.Manger.Name, SqlDir.ASC)
    .OrderBy(x => x.Name, SqlDir.ASC)
    .Execute(connector);

if (response.State == ResponseState.Valid)
{
    List<User> users = response.Results;
}
else
{
    System.Diagnostics.Debug.WriteLine(response.Error?.Message);
    System.Diagnostics.Debug.WriteLine((response.Error as SqlResponseException).Query.GetTextWithParameterValues());
}
```
