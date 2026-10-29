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

Procedure classes use `Execute(input)` and `ExecuteAsync(input)` with ordinary input and output models.
See [stored procedures](../queries/stored-procedures.md) for a complete example.
Async methods have the same database support as synchronous methods. For example, MySQL does not support modified-row output.

## Override the command timeout

Pass `commandTimeout` when an operation needs a different limit from `ConnectorOptions.CommandTimeout`:

```csharp
var response = await query.ExecuteAsync(
    cancellationToken: cancellationToken,
    commandTimeout: TimeSpan.FromMinutes(3));

// Zero disables the timeout for this execution.
var unlimited = query.Execute(commandTimeout: TimeSpan.Zero);
```

Import `System` for `TimeSpan`.
Omit the argument or pass `null` to use the connector default. The override does not change that default or other executions.
The same argument is available on synchronous, asynchronous, and streamed query execution, stored procedures, and raw connector commands:

```csharp
var value = await connector.ExecuteScalarAsync<long>(
    renderedQuery,
    cancellationToken: cancellationToken,
    commandTimeout: TimeSpan.FromMinutes(3));
```

The timeout applies separately to each generated command. A paged total uses it for both the data and count commands.
Batched inserts use it for every batch. TVP inserts also use it for provisioning, lease renewal, execution guards, and cleanup commands.
It is not a total time budget for the complete operation, and it does not override the factory's connection login timeout.
Cancellation tokens remain independent of the timeout.

Provider command timeouts use whole seconds. Positive fractional seconds round up, so a short positive limit cannot become unlimited.
Negative values and durations outside the supported range are rejected before opening a connection.
DuckDB and connectors configured with `CancelCommand` retain a finer deadline for execution and result loading, up to 4,294,967,294 milliseconds.
Streams capture the effective timeout when the sequence is created. Dispose the enumeration after an early exit.

Execution interfaces now include the optional `TimeSpan? commandTimeout` parameter.
Update custom implementations and method-group delegate types to include it, or wrap the call in a lambda that omits the argument.

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

For DuckDB and connectors configured with `CancelCommand`, `ConnectorOptions.CommandTimeout` includes execution and result loading. Zero disables the timeout.
A command timeout returns an error response containing `TimeoutException`.
If a supplied DuckDB transaction is interrupted, roll it back before reusing its connection.
Configure connection settings in the connection factory using the provider's supported options.

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
See [result property types](value-conversions.md) for null, numeric, and date rules.

## Reuse query builders safely

Finish building a query before executing it. Do not change a builder while another caller is capturing or executing it.
Use [Capture](../queries/sql-and-query-reuse.md#keep-a-query-definition) to keep a definition for repeated or concurrent use.
Keep custom provider parameter objects unchanged while a query is in progress.

## Interrupt running SQLite queries

`CancelCommand` is available starting with `SqlLinqer` `2.3.0-rc.2`. Microsoft.Data.Sqlite 10.0.13 checks cancellation before execution but does not interrupt a query already running. Its command timeout controls lock waits rather than CPU execution. Passing a token alone therefore does not establish an execution deadline.

Applications using that provider can supply its native interruption operation:

```csharp
var connector = new ConnectionFactoryConnector(
    () => new Microsoft.Data.Sqlite.SqliteConnection(connectionString),
    DbFlavor.Sqlite,
    new ConnectorOptions
    {
        CommandTimeout = TimeSpan.FromSeconds(30),
        CancelCommand = command => SQLitePCL.raw.sqlite3_interrupt(
            ((Microsoft.Data.Sqlite.SqliteConnection)command.Connection).Handle)
    });
```

This uses SQLitePCLRaw supplied by the application's SQLite provider. SqlLinqer itself has no SQLite provider dependency. The callback replaces `DbCommand.Cancel`, runs on a cancellation/timer thread, and must return promptly and tolerate repeated calls. SqlLinqer synchronizes it with command completion so a late cancellation cannot interrupt the next command using that connection. Cancellation and timeout outcomes still follow the response contracts above. The deadline covers streamed reads and consumer pauses until the enumeration is disposed. It begins after opening the connection and establishing an owned transaction; it is not a connection or transaction-acquisition deadline.

Do not execute concurrent commands on the same SQLite connection: native interruption is connection-wide. Caller-owned transactions remain caller-owned. Decide whether an interrupted business operation needs rollback before continuing.
