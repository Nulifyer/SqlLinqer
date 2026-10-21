# SQL Linqer

SqlLinqer builds SQL queries from C# model mappings and expressions. It supports typed selects, nested relationships, aggregates, and writes.
The library targets .NET Standard 2.0. Install the database provider in your application and create the database schema before querying.

Set `ConnectorOptions.UseSqlModelJsonContract = true` to load SQL results independently of API JSON attributes and converters.
This opt-in mode lets one class use SqlLinqer attributes for database mappings and JSON attributes for API serialization. The default preserves existing JSON loading behavior.

## Define a model

Map a class to an existing table. These examples use SQL Server and the following model:

```csharp
using System;
using SqlLinqer.Modeling;

[SqlTable("Users")]
public class User : SqlLinqerPrimaryKeyObject<User, string>
{
    [SqlPrimaryKey(dbGenerated: false)]
    [SqlColumn("id")]
    public string Id { get; set; }

    [SqlColumn("username")]
    public string Username { get; set; }

    [SqlColumn("email")]
    public string Email { get; set; }

    public User()
    {
        Id = Guid.NewGuid().ToString();
    }
}
```

## Configure a connector

Install `Microsoft.Data.SqlClient` in your application. Supply a connection string for an existing database:

```csharp
using Microsoft.Data.SqlClient;
using SqlLinqer;
using SqlLinqer.Connections;

using var template = new SqlConnection(connectionString);
var connector = new ReplicatorConnector(template, DbFlavor.SqlServer);
```

The connector creates and disposes connections for queries. Pass it when starting a query. The query retains it for `Execute()`.
If you supply a transaction to `Execute`, you own its commit, rollback, and disposal.

## Insert rows

```csharp
var newUsers = new[]
{
    new User { Username = "ab789", Email = "boss@company.com" },
    new User { Username = "js354", Email = "myemail@company.com" }
};

var insertResponse = User.BeginInsert(connector)
    .InsertAuto(newUsers)
    .Execute();
```

`BeginInsert` captures primary keys. If the response is valid, `TouchedPrimaryKeys` contains the inserted IDs and `Result` contains the affected-row count.

## Select rows

```csharp
var selectResponse = User.BeginSelect(connector)
    .SelectRootColumns()
    .OrderBy(x => x.Username, SqlDir.ASC)
    .Execute();

if (selectResponse.State == ResponseState.Valid)
{
    foreach (var user in selectResponse.Result)
        Console.WriteLine(user.Username);
}
```

`ExecuteWithTotalCount` returns `SqlSelectResponse<User>`, with rows in `Result` and the count before paging in `TotalResults`.

## Update rows

```csharp
var updateResponse = User.BeginUpdate(connector)
    .Update(x => x.Email, "noreply@company.com")
    .Where(x => x.Email, "%@company.com", SqlOp.LIKE)
    .Execute();
```

## Delete rows

```csharp
var deleteResponse = User.BeginDelete(connector)
    .Where(x => x.Email, "%@company.com", SqlOp.LIKE)
    .Execute();
```

Filter updates and deletes before executing them. Without a filter, they affect every row in the table.
Check `State` before reading `Result`. If the response has an error, inspect `Error`.

See the repository's [connection](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/SETUP_A_CONNECTION.md),
[model](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/BUILDING_A_DATAMODEL.md), and
[query guides](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/QUERY_READ.md) for more examples.

See [response handling](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/RESPONSES.md) for state, results, and errors.
See [typed stored procedures](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/CALLABLES.md) for callable models, typed inputs, result sets, and output parameters on SQL Server and Azure SQL.
The [connector guide](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/SETUP_A_CONNECTION.md#inject-a-connector) also shows constructor injection and service registration.

See [transactions](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/TRANSACTIONS.md) to commit several writes together,
and [relationship updates](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/QUERY_RELATIONSHIPS.md) to assign managers or change permissions.


- [Sync and async execution](../docs/EXECUTION.md)
- [Query snapshots and compilation](../docs/QUERY_COMPILATION.md)
