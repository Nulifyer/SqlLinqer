# SQL Linqer

Build typed SQL queries that return nested C# models.

SqlLinqer maps your existing tables and relationships to C# members, builds parameterized SQL,
and loads the objects and collections you select. Use it for search endpoints, nested API responses, and aggregate reports.

Load a page of users, include their manager and permissions, and get the total number of matches:

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
```

That query returns `User` objects with the selected relationships populated. `TotalResults` gives the count before paging.
Values such as `"Ada Lovelace"` become SQL parameters.
The selected graph loads in one database command; the total uses a separate count command.
Database JSON carries the nested result, and SqlLinqer maps it to your C# members.

SqlLinqer supports SQL Server, Azure SQL, PostgreSQL, MySQL, SQLite, and DuckDB, and targets .NET Standard 2.0.
Use the same model mappings for selects, aggregate projections, inserts, updates, and deletes.

## Why choose this query style?

- Select related objects and collections through member expressions, with their result mapping handled by SqlLinqer.
- Add optional search filters, sorting, and paging to an explicit query builder.
- Inspect generated SQL and parameter bindings before execution.
- Keep control of your schema, ADO.NET provider, and transactions. Issue writes explicitly.
- Reuse model classes for API responses. API JSON attributes do not change SQL loading.

Ordinary classes also work with the generic query builders; the model base classes below supply the `BeginSelect` and write helpers.
See [choosing SqlLinqer](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/choosing-sqllinqer.md)
for the execution tradeoffs and comparisons with other .NET libraries.

## Try it with your own tables

Install SqlLinqer and your database provider. These examples use SQL Server:

```sh
dotnet add package SqlLinqer
dotnet add package Microsoft.Data.SqlClient
```

Create a connector with your database connection string:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;
using SqlLinqer;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

var connector = new ConnectionFactoryConnector(
    () => new SqlConnection(connectionString),
    DbFlavor.SqlServer);
```

The examples below use this connector and the models in the next section. Create the matching tables in your database first.
For another database or dependency injection, see the [connection guide](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/getting-started/connections.md).

## Map the tables once

Map your existing `Users` and `Permissions` tables. The `UserPermissions` linking table supplies the many-to-many relationship:

```csharp
[SqlTable("Users")]
public class User : SqlLinqerPrimaryKeyObject<User, int>
{
    [SqlPrimaryKey(dbGenerated: true), SqlColumn("id")]
    public int Id { get; set; }

    [SqlColumn("name")]
    public string Name { get; set; }

    [SqlColumn("email")]
    public string Email { get; set; }

    [SqlColumn("manager_id")]
    public int? ManagerId { get; set; }

    [SqlOneToOne(nameof(ManagerId)), SqlAutoSelectExclude]
    public User Manager { get; set; }

    [SqlManyToMany("UserPermissions", "user_id", "permission_id"), SqlAutoSelectExclude]
    public IEnumerable<Permission> Permissions { get; set; }
}

[SqlTable("Permissions")]
public class Permission : SqlLinqerPrimaryKeyObject<Permission, int>
{
    [SqlPrimaryKey(dbGenerated: true), SqlColumn("id")]
    public int Id { get; set; }

    [SqlColumn("name")]
    public string Name { get; set; }
}
```

Now `user.Manager.Name` and `user.Permissions` are queryable relationships.
Select them when you need them. `SelectRootColumns()` loads the user's own columns.

## Use the results directly

Execute the opening query after defining your models and connector. Check its state, then work with the returned models:

```csharp
if (response.State != ResponseState.Valid)
    throw response.Error;

foreach (var user in response.Result)
{
    Console.WriteLine($"{user.Name}, managed by {user.Manager?.Name}");
    foreach (var permission in user.Permissions)
        Console.WriteLine($"  {permission.Name}");
}

Console.WriteLine($"{response.TotalResults} matching users");
```

Pages start at `1`. The same query builder also supports optional filters, grouped conditions, and `IN` lists.

## Turn relationships into a report

Count each user's permissions and return just the fields your report needs:

```csharp
var summary = User.BeginSelectBuilder(connector)
    .BuildQuery(query => new
    {
        Name = query.Select(user => user.Name),
        PermissionCount = query.Count(user => user.Permissions.First().Id)
    })
    .OrderBy(row => row.PermissionCount, SqlDir.DESC)
    .Execute();

if (summary.State != ResponseState.Valid)
    throw summary.Error;

foreach (var row in summary.Result)
    Console.WriteLine($"{row.Name}: {row.PermissionCount} permissions");
```

Use an anonymous projection for a local result, or a named class to return it from a method.

## Insert models and get their keys

```csharp
var inserted = User.BeginInsert(connector)
    .InsertAuto(new[]
    {
        new User { Name = "Grace Hopper", Email = "grace@example.com" },
        new User { Name = "Margaret Hamilton", Email = "margaret@example.com" }
    })
    .Execute();

if (inserted.State != ResponseState.Valid)
    throw inserted.Error;

Console.WriteLine($"Inserted {inserted.Result} users");
Console.WriteLine(string.Join(", ", inserted.TouchedPrimaryKeys));
```

The key mapping tells SqlLinqer that the database generates these IDs. `TouchedPrimaryKeys` contains the inserted keys.
Primary-key capture depends on the database's capabilities; see the [insert guide](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/queries/insert.md).

## Make targeted changes

Update a user's email or delete a specific user with the same member expressions:

```csharp
var updated = User.BeginUpdate(connector)
    .Update(user => user.Email, "ada@example.com")
    .Where(user => user.Id, 42, SqlOp.EQ)
    .Execute();

var deleted = User.BeginDelete(connector)
    .Where(user => user.Id, 99, SqlOp.EQ)
    .Execute();

if (updated.State != ResponseState.Valid)
    throw updated.Error;
if (deleted.State != ResponseState.Valid)
    throw deleted.Error;
```

Pass a transaction to execute several changes together. You control its commit and rollback.

## Choose how results arrive

Use `Execute()` for synchronous results or `ExecuteAsync()` for asynchronous results:

```csharp
var query = User.BeginSelect(connector)
    .SelectRootColumns()
    .OrderBy(user => user.Id, SqlDir.ASC);

var asynchronous = await query.ExecuteAsync();
if (asynchronous.State != ResponseState.Valid)
    throw asynchronous.Error;
```

For large reads, experimental `ExecuteAsyncStream()` lets you process each model as it arrives:

```csharp
await foreach (var user in query.ExecuteAsyncStream())
{
    Console.WriteLine(user.Name);
}
```

Run these examples in an asynchronous method with a compiler that supports async enumeration.
A stream yields complete root models, including their selected nested collections. Process each row directly or collect batches in your application.
Provider buffers and query execution still contribute to memory use.
See the [result streaming guide](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/execution/result-streaming.md) for batching, cancellation, and memory limits.

## Keep building

Browse the [documentation index](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/README.md) for guides grouped by task.

Add [stored procedures](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/queries/stored-procedures.md),
[transactions](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/execution/transactions.md), and
[relationship changes](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/queries/relationship-changes.md) as your queries grow.
Database mappings and API JSON attributes can share a model without changing each other's behavior.

For reproducible performance comparisons, use the [SQLite comparison suite](https://github.com/Nulifyer/SqlLinqer/blob/master/SqlLinqer.Comparisons/README.md).
It validates equivalent results across SqlLinqer, Dapper, SqlKata with Dapper, LINQ to DB, and EF Core.
Database JSON generation and decoding have costs; measure your queries on the database you deploy.

Find more examples in the [query guide](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/queries/select.md),
[model guide](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/getting-started/model-mapping.md), and
[GitHub repository](https://github.com/Nulifyer/SqlLinqer).

## Application integration

Use `connector.BeginSelect<T>()` with ordinary mapped classes, and read successful results with `GetResultOrThrow()` or preserve totals and generated keys with `EnsureSuccess()`. These additive helpers are available in the current repository.

The [minimal API sample](../samples/SqlLinqer.MinimalApi/README.md) demonstrates filtering, paging totals, and streaming with optional DI and diagnostics projects. Those optional packages are under development and require project references until published. See the [connection guide](../docs/getting-started/connections.md) and [logging and tracing guide](../docs/execution/diagnostics.md).
