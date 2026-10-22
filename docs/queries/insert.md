[Documentation](../README.md) · [Project README](../../README.md)

# Insert rows

These examples use the [sample models](../getting-started/model-mapping.md) and a [configured connector](../getting-started/connections.md).
Import `SqlLinqer` for response states. See [response handling](../execution/responses-and-errors.md) for `State`, `Result`, and `Error` examples.

## Insert one user

`BeginInsert` creates the query. `InsertAuto` reads the columns from the object.

```csharp
var newUser = new User
{
    Username = "js354",
    Name = "John Smith",
    Email = "myemail@company.com"
};

var query = User.BeginInsert(connector).InsertAuto(newUser);
var response = query.Execute();
if (response.State != ResponseState.Valid)
    throw response.Error;

var insertedCount = response.Result;
var insertedIds = response.TouchedPrimaryKeys;
```

`Result` contains the affected-row count. `TouchedPrimaryKeys` contains the inserted IDs.

## Insert multiple users

Pass a collection to `InsertAuto`:

```csharp
var newUsers = new[]
{
    new User { Username = "ab789", Name = "Big Boss", Email = "boss@company.com" },
    new User { Username = "ml123", Name = "Morgan Lee", Email = "morgan@company.com" }
};

var batchQuery = User.BeginInsert(connector).InsertAuto(newUsers);
var batchResponse = batchQuery.Execute();
```

## Batch limits and transactions

`Execute` and `ExecuteWithOutput` split insert rows according to `ConnectorOptions.ParameterLimit`.
SQL Server and Azure SQL also limit each command to 2,100 parameters and 1,000 `VALUES` rows.
SQLite and DuckDB default-only inserts use one row per command.

All batches use one connection and transaction. SqlLinqer commits once, after all batches succeed.
A failed batch rolls back the transaction that SqlLinqer created. A rollback failure does not replace the original error.
If you supply a transaction, you must commit or roll it back. SqlLinqer leaves its connection open.

A row that exceeds the parameter budget returns an error response before connecting.
A non-positive `ParameterLimit` disables the configured budget; SQL Server limits still apply.
Each batch is a separate SQL statement, so statement-level triggers run once per batch.
Returned rows and primary keys are collected across batches. Their order within each batch follows the provider result order.
A failed operation does not return partial results.

Batching applies during execution. `Render` still renders all supplied rows as one statement for inspection.
Applications that execute rendered SQL themselves must enforce their own command limits.
`ExecuteTvp` uses its existing table-valued parameter path.

## Insert from an instance

`BeginInsertAuto` reads the instance and creates an insert with its automatic-insert columns:

```csharp
var anotherUser = new User
{
    Username = "rk456",
    Name = "Riley Kim",
    Email = "riley@company.com"
};

var instanceResponse = anotherUser
    .BeginInsertAuto(connector)
    .Execute();
```

## Use a table-valued parameter

On SQL Server, use `ExecuteTvp` as an alternative to `Execute` for a table-valued parameter insert:

```csharp
var tvpResponse = batchQuery.ExecuteTvp();
```

`ExecuteTvp` creates and removes a SQL table type for the insert, so the database user needs permission to create types in the target schema.
The connector must be able to create fresh connections to the same server, even when you pass your own transaction.
For an existing table type, use a [stored-procedure table-valued input](stored-procedures.md#supply-optional-inputs) instead.
