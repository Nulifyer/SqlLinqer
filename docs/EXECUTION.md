[README](../README.md)

# Synchronous and asynchronous execution

The fluent query interface supports both execution styles. Both return the same response types and use the same SQL generation and JSON mapping rules.

```csharp
var query = User.BeginSelect(connector)
    .SelectRootColumns()
    .Where(x => x.Name, "Ada", SqlOp.EQ);

var synchronous = query.Execute();
var asynchronous = await query.ExecuteAsync(cancellationToken: cancellationToken);
```

Import `System.Threading` and `System.Threading.Tasks` when declaring cancellation tokens or task-returning methods.
Run the asynchronous example inside an `async` method.

## Available methods

The executable query types provide asynchronous counterparts to their synchronous methods:

- `ExecuteAsync` selects rows or executes a write, including primary-key responses.
- `ExecuteWithTotalCountAsync` selects a page and counts the unpaged root projection.
- `ExecuteWithOutputAsync` returns modified rows as a `DataTable`.
- `ExecuteTvpAsync` and `ExecuteTvpWithOutputAsync` use the existing SQL Server TVP insert path.
- `ExecuteJsonAsync` and `ExecuteDataTableAsync` execute legacy stored-procedure queries.
- Typed callable invocations provide `ExecuteAsync`, including multiple result sets, output parameters, and return codes.

Database support and unsupported-operation errors match synchronous execution. For example, MySQL modified-row output remains unsupported.

Each method accepts an optional transaction followed by an optional `CancellationToken`.
An overload also accepts an explicit connector before those arguments:

```csharp
var response = await query.ExecuteAsync(
    connector,
    transaction,
    cancellationToken);

if (response.State == ResponseState.Error)
    throw response.Error;
```

Queries retain the connector supplied at creation. An explicit execution connector applies to that call.
If no connector was retained, execution uses the current default connector.
The new `IAsyncQueryExecutable*` interfaces describe asynchronous execution without adding members to the existing synchronous interfaces.

## Cancellation and errors

Requested cancellation throws `OperationCanceledException` with the supplied token.
Database execution and mapping failures remain `ResponseState.Error` values.
Query validation and unsupported-operation errors can occur before database execution, as with synchronous methods.

```csharp
using (var cancellation = new CancellationTokenSource())
{
    cancellation.CancelAfter(TimeSpan.FromSeconds(5));
    try
    {
        var response = await query.ExecuteAsync(
            cancellationToken: cancellation.Token);
        if (response.State == ResponseState.Error)
            throw response.Error;
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        // Handle the cancelled operation.
    }
}
```

The library passes the token to provider methods for connection opening, command execution, row reading, and result-set advancement.
The provider determines whether these operations perform asynchronous I/O. Embedded providers can complete them synchronously.
SqlLinqer does not use `Task.Run` to wrap synchronous execution.
DuckDB also receives a scoped interrupt registration because its cancellation applies to the connection.

## Transactions and ownership

Caller-owned transactions remain caller-owned. SqlLinqer does not commit, roll back, or dispose them, including after cancellation.
The caller must decide how to complete a cancelled transaction.
Connections and transactions created by the library are disposed. Failed or cancelled owned writes attempt rollback.
Batched inserts retain one transaction for all batches.

The library retains its .NET Standard 2.0 target.
Transaction begin, commit, rollback, and disposal use synchronous base contracts, which do not provide asynchronous lifecycle methods in this target.
Connection opening and database commands use the asynchronous provider contracts.

## Compilation and mutation

Select execution compiles an isolated query graph before database I/O.
Select-with-count execution captures one definition for both commands, so changes to the builder during the first command cannot change the count query.
Batched inserts capture their mapped input values before the first asynchronous operation.

Do not mutate a builder while it is being captured. Use a [query snapshot](QUERY_COMPILATION.md) for repeated or concurrent compilation.
Treat provider-specific parameter objects as immutable while an operation is in progress.
Standard captured arrays and `DataTable` values are copied.

## Custom connector hooks

`BaseConnector.ExecuteCommand` remains the synchronous interception hook.
Override `ExecuteCommandAsync` to apply logging or execution policies to asynchronous calls:

```csharp
protected override async Task<SqlResponse<T>> ExecuteCommandAsync<T>(
    RenderedQuery query,
    Func<Task<SqlResponse<T>>> action,
    CancellationToken cancellationToken)
{
    return await base.ExecuteCommandAsync(
        query, action, cancellationToken).ConfigureAwait(false);
}
```

Import `System`, `SqlLinqer`, and `SqlLinqer.Components.Render` for this override.
Override both hooks when the same policy must apply to both execution styles.
Existing `JsonResultsRowByRow` overrides continue to control JSON transport decoding for both styles.
