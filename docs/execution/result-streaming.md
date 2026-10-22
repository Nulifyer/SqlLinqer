[Documentation](../README.md) · [Project README](../../README.md)

# Process large results with streaming

Experimental `ExecuteAsyncStream()` lets you process selected rows without loading the complete result list first.
It supports typed selects and aggregate projections, including their selected relationships.
Use `Execute()` or `ExecuteAsync()` when you want a complete list in a response object.

These examples use the [sample models](../getting-started/model-mapping.md) and a [configured connector](../getting-started/connections.md).
Run them inside an async method with a compiler that supports `await foreach`.
`token` is the cancellation token for the operation.

## Process one row at a time

```csharp
var query = User.BeginSelect(connector)
    .SelectRootColumns()
    .OrderBy(x => x.Id, SqlDir.ASC);

await foreach (var user in query.ExecuteAsyncStream(cancellationToken: token))
{
    await ProcessAsync(user);
}
```

Replace `ProcessAsync` with your application logic.
Each item is a complete model, including any selected nested collections.
An empty result does not enter the loop.

The database connection stays in use while the loop processes results.
Finish the loop before using the same connection for another operation.

## Process batches

Keep a limited list when an external service or write operation accepts batches:

```csharp
var batch = new List<User>(1000);
await foreach (var user in query.ExecuteAsyncStream(cancellationToken: token))
{
    batch.Add(user);
    if (batch.Count == 1000)
    {
        await ProcessBatchAsync(batch);
        batch.Clear();
    }
}
if (batch.Count > 0)
    await ProcessBatchAsync(batch);
```

Import `System.Collections.Generic` and replace `ProcessBatchAsync` with your batch operation.
The operation must finish using the list before returning, because the next step clears it.
This runs one database query. It does not request a separate page for each batch.
Collecting every item into a list removes the memory benefit of streaming.

## Handle failures and cancellation

Streaming throws exceptions during `await foreach` instead of returning a `SqlResponse`:

```csharp
try
{
    await foreach (var user in query.ExecuteAsyncStream(cancellationToken: token))
    {
        await ProcessAsync(user);
    }
}
catch (OperationCanceledException) when (token.IsCancellationRequested)
{
    // Handle cancellation.
}
catch (SqlLinqer.Exceptions.SqlResponseException error)
{
    // Handle a database or result-mapping failure.
    Console.WriteLine(error.InnerException?.Message);
}
```

Import `System` for these exception and console types.
A failure can occur after some rows have been processed. Streaming does not retry the operation or undo application work.
The query uses the same [value conversion rules](value-conversions.md) as other typed reads.

The reader and any connection opened by SqlLinqer are released when the loop finishes, breaks, or throws.
If you supply a transaction, you still control its commit, rollback, and disposal, including after cancellation.
If you use an enumerator manually, dispose it when you finish.

## Reuse a stream

`ExecuteAsyncStream` keeps the query definition and connector selected when the method is called.
Later changes to the query builder do not change that sequence.
Execution starts when enumeration begins. Enumerating the sequence again executes the database query again.
Finish building the query before sharing it between callers.

## Plan memory and timeouts

Streaming reduces the result rows retained by SqlLinqer. Your application still uses memory for the current model and any retained batches.
Large nested collections, database sorting, aggregation, and provider buffers can use substantial memory.
A batch size limits the number of models, not their size in bytes.

For DuckDB, command timeout includes time spent processing rows inside the loop.
A timeout can therefore occur while slow application code holds the stream open.
See [execution and cancellation](sync-and-async.md#cancellation-and-errors) for timeout settings.

Streaming currently covers selects and aggregate projections.
Stored-procedure result sets, write results, and synchronous enumeration are not supported by this experimental method.
