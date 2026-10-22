[Documentation](../README.md) · [Project README](../../README.md)

# Execute queries and handle cancellation

Use `Execute()` for synchronous queries or `ExecuteAsync()` inside an async method.
Both return response objects. Check `State` before reading `Result`.
These examples use the [sample models](../getting-started/model-mapping.md) and a [configured connector](../getting-started/connections.md).

```csharp
var query = User.BeginSelect(connector)
    .SelectRootColumns()
    .Where(x => x.Name, "Ada", SqlOp.EQ);

var response = await query.ExecuteAsync(cancellationToken: cancellationToken);
if (response.State != ResponseState.Valid)
    throw response.Error;

var users = response.Result;
```

Import `SqlLinqer` for response states and `System.Threading` for cancellation tokens.
For large reads, experimental [result streaming](result-streaming.md) lets you process rows without loading the complete list.

## Choose an execution method

Use the async counterpart of the method you would call synchronously:

| Synchronous method | Asynchronous method | Result |
| --- | --- | --- |
| `Execute` | `ExecuteAsync` | Selected rows, procedure output rows, or a write's affected-row count. |
| `ExecuteWithTotalCount` | `ExecuteWithTotalCountAsync` | Selected rows and their total before paging. |
| `ExecuteWithOutput` | `ExecuteWithOutputAsync` | Modified rows in a `DataTable`, where the database supports it. |
| `ExecuteTvp` | `ExecuteTvpAsync` | A SQL Server table-valued parameter insert. |
| `ExecuteTvpWithOutput` | `ExecuteTvpWithOutputAsync` | A TVP insert with returned rows. |
| `ExecuteJson`, `ExecuteDataTable` | `ExecuteJsonAsync`, `ExecuteDataTableAsync` | Results from the existing procedure interface. |

See [stored procedures](../queries/stored-procedures.md) for typed input and output models.
Async methods have the same database support as synchronous methods. For example, MySQL does not support modified-row output.

## Choose the connector and transaction

A query uses the connector supplied when it was created, or the current default if none was supplied.
Pass an explicit connector to use it for one execution:

```csharp
var response = await query.ExecuteAsync(
    connector,
    transaction,
    cancellationToken);

if (response.State != ResponseState.Valid)
    throw response.Error;
```

The optional arguments are the transaction followed by the cancellation token.
Use named arguments when supplying only a cancellation token.

If you pass a transaction, you control its commit, rollback, and disposal.
SqlLinqer leaves its connection open, including after errors or cancellation.
Without a supplied transaction, SqlLinqer opens and releases connections as needed.
Writes that use a transaction created by SqlLinqer commit on success and attempt rollback on failure or cancellation.
All batches in an insert use the same write transaction.
See [transactions](transactions.md) for a complete example.

## Cancellation and errors

Database and result-mapping failures return `ResponseState.Error`.
Invalid query arguments or unsupported operations can throw before execution.
Requested cancellation throws `OperationCanceledException`:

```csharp
using var cancellation = new CancellationTokenSource();
cancellation.CancelAfter(TimeSpan.FromSeconds(5));
try
{
    var response = await query.ExecuteAsync(
        cancellationToken: cancellation.Token);
    if (response.State != ResponseState.Valid)
        throw response.Error;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    // Handle the cancelled operation.
}
```

Import `System` and `System.Threading` for this example.
The database provider determines how quickly an active command can be cancelled.
If you supplied a transaction, decide whether to roll it back after cancellation.

For DuckDB, `ConnectorOptions.CommandTimeout` includes execution and result loading. Zero disables the timeout.
A command timeout returns an error response containing `TimeoutException`.
If a supplied DuckDB transaction is interrupted, roll it back before reusing its connection.
`ConnectionTimeout` has no DuckDB connection-string effect.

## Read rows from raw SQL

Use `ExecuteReaderList<T>` or `ExecuteReaderListAsync<T>` when a command returns ordinary columns:

```csharp
var query = new RenderedQuery("SELECT Id, display_name FROM Customers");
var response = await connector.ExecuteReaderListAsync<Customer>(
    query, cancellationToken: cancellationToken);
if (response.State != ResponseState.Valid)
    throw response.Error;

List<Customer> customers = response.Result;
```

Define the output model outside the async method:

```csharp
public sealed class Customer
{
    public int Id { get; set; }

    [SqlColumn("display_name")]
    public string Name { get; set; }
}
```

Import `System.Collections.Generic`, `SqlLinqer`, `SqlLinqer.Components.Render`, and `SqlLinqer.Modeling` for these examples.
Use `ExecuteReaderDataTable` or `ExecuteReaderDataTableAsync` when you need a `DataTable` instead of models.

Public fields and properties map to columns by name without regard to case.
Use `SqlColumn` for a different database name and `SqlIgnore` for members absent from the result.
Each mapped member needs one unambiguous matching column. Extra columns are ignored, and API JSON attributes do not change SQL mapping.
A model needs a parameterless constructor or one public constructor matching its mapped member names and types.
Single-column results can also use scalar types such as `int?`.

An empty result returns an empty list. Mapping failures return an error without a partial list.
These methods load the first tabular result and do not populate relationships.
For multiple result sets, use [stored procedures with custom outputs](../queries/stored-procedure-outputs.md).
See [result property types](value-conversions.md) for null, numeric, and date rules.

## Reuse query builders safely

Finish building a query before executing it. Do not change a builder while another caller is capturing or executing it.
Use [Capture](../queries/sql-and-query-reuse.md#keep-a-query-definition) to keep a definition for repeated or concurrent use.
Keep custom provider parameter objects unchanged while a query is in progress.
