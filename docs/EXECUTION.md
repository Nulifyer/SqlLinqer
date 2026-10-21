[README](../README.md)

# Synchronous and asynchronous execution

The fluent query interface supports both execution styles. Both return the same response types and use the same SQL generation and JSON mapping rules.

Typed selects and projections also provide experimental [result streaming](RESULT_STREAMING.md) through `ExecuteAsyncStream`.

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

## Tabular model lists

For commands that return ordinary columns, choose `ExecuteReaderDataTable` for a `DataTable` or
`ExecuteReaderList<T>` for a `List<T>`. Both return a `SqlResponse` whose state must be checked.
The asynchronous counterparts are `ExecuteReaderDataTableAsync` and `ExecuteReaderListAsync<T>`.

```csharp
var query = new RenderedQuery("SELECT Id, display_name FROM Customers");
var response = await connector.ExecuteReaderListAsync<Customer>(
    query, cancellationToken: cancellationToken);
if (response.State == ResponseState.Error)
    throw response.Error;

List<Customer> customers = response.Result;

public sealed class Customer
{
    public int Id { get; set; }
    [SqlColumn("display_name")]
    public string Name { get; set; }
}
```

Import `System.Collections.Generic`, `SqlLinqer`, `SqlLinqer.Components.Render`, and `SqlLinqer.Modeling` for this example.
Run the query code inside an asynchronous method and declare the model outside that method.

The list methods map provider rows directly into a buffered `List<T>`.
They preserve native values, including `DateTime.Kind`, without a temporary `DataTable`.
DuckDB binary streams become independent byte arrays before the database reader closes.

Mapping uses public fields and properties, matches names without regard to case, and honors `SqlColumn` and `SqlIgnore`.
API JSON attributes do not affect these mappings. Each mapped member requires a result column; extra columns are ignored.
Models can use a parameterless constructor or one public constructor that matches mapped member names and types.
SQL NULL maps to nullable members. Compatible numeric, GUID, and enum values are converted.
Incompatible values and missing columns produce an error response without a partial list.
An empty result produces an empty list. Scalar types such as `int?` are supported for single-column results.

These methods load the first tabular result and skip leading results without columns.
They map flat rows and do not populate relationships. Use typed callables when a command needs multiple result sets.
The list methods accept the same caller-owned transaction as the table methods.
Asynchronous cancellation applies during database loading and model conversion.

See [result value conversions](RESULT_CONVERSIONS.md) for numeric range checks, date and timezone rules, and the conversion test matrix.

## Transactions and ownership

Caller-owned transactions remain caller-owned. SqlLinqer does not commit, roll back, or dispose them, including after cancellation.
The caller must decide how to complete a cancelled transaction.
Connections and transactions created by the library are disposed. Failed or cancelled owned writes attempt rollback.
Batched inserts retain one transaction for all batches.

Generated SQL Server TVP inserts create and drop their table type through separate autocommit connections.
The insert uses the owned or supplied write transaction. Type cleanup also runs after cancellation or a server-aborted transaction.
The connector must create connections to the same server as the write connection. Metadata commands use the write connection's database.

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
