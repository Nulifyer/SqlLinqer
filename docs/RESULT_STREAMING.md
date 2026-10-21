[README](../README.md)

# Experimental result streaming

`ExecuteAsyncStream` yields complete root models or projections without retaining previous rows. The experiment covers typed selects and aggregate projections.
Existing `Execute` and `ExecuteAsync` methods continue to buffer results and return response objects.

Execution names retain the operation name and append `Async` or `AsyncStream` for the execution style.
Typed selects use `Execute`, `ExecuteAsync`, and `ExecuteAsyncStream`.
Connector JSON reads use `ExecuteReaderJson`, `ExecuteReaderJsonAsync`, and `ExecuteReaderJsonAsyncStream`.
All three modes use the same [result value conversion rules](RESULT_CONVERSIONS.md).

```csharp
var query = User.BeginSelect(connector)
    .SelectRootColumns()
    .OrderBy(x => x.Id, SqlDir.ASC);

await foreach (var user in query.ExecuteAsyncStream(cancellationToken: token))
{
    await ProcessAsync(user);
}
```

Consumers need a compiler that supports C# async enumeration. The library retains its .NET Standard 2.0 target and explicitly references the compatible async interface contracts.

## Choose how many rows to retain

Process each row immediately to avoid retaining results. To process batches, hold a bounded collection and clear it after processing:

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

`ProcessBatchAsync` must finish using the collection before returning. To retain every result, use the existing buffered execution method or collect the stream yourself.
Batching uses one query execution. It does not issue paged queries.

## Capture and execution

The typed query captures its definition and resolves its connector when `ExecuteAsyncStream` is called. Later builder changes do not affect that sequence.
Database execution and rendering start when enumeration advances. Each enumeration renders fresh bindings and executes the captured definition again.
`IAsyncQueryExecutableStream<T>` describes these methods without changing existing executable interfaces.

The low-level connector method accepts a rendered command:

```csharp
await foreach (var user in connector.ExecuteReaderJsonAsyncStream<User>(rendered, transaction, token))
{
    await ProcessAsync(user);
}
```

Keep a directly supplied `RenderedQuery` and its parameter values unchanged until all enumerations finish. It is mutable and is not captured by the connector.

## Lifetime and errors

The stream owns its reader and command. It also owns its connection unless a transaction supplies one.
Async enumeration disposes these resources on completion, `break`, cancellation, or a consumer exception. Manually acquired enumerators must also be disposed.
The library does not commit, roll back, or dispose caller transactions. Do not use their connection concurrently with an active stream.
Slow consumers retain a connection while they process results.

Database and mapping failures throw `SqlResponseException` during enumeration. The exception retains the rendered query and original error.
Cancellation throws `OperationCanceledException`. Both the method token and `WithCancellation` are supported.
A failure can occur after some rows have been delivered. Processed rows cannot be withdrawn, and streams do not automatically retry.

Custom connectors can override `ExecuteCommandAsyncStream` for policies that cover the complete enumeration lifetime, including early disposal.
Existing `ExecuteCommand` and `ExecuteCommandAsync` hooks cover buffered execution only.
Consumer exceptions occur outside the execution hook and retain their original type.

## JSON transport and memory limits

PostgreSQL, MySQL, SQLite, and DuckDB queries return one root JSON object per database row. The connector deserializes each value directly and yields the model.
It materializes that row's JSON string and the root model, including nested collections and binary values.

SQL Server and Azure SQL return a JSON array split into text fragments. The connector requests sequential access and reads fragment text through `GetTextReader`.
A stateful UTF-8 encoder preserves characters across fragments. Fixed buffers coalesce fragments before the framework parser reads them.
The parser yields complete array elements without constructing a complete document string or result list.
Zero transport rows produce an empty sequence, matching buffered loading. Malformed JSON content fails enumeration.
Parser buffers can grow for large values. Bounded read-ahead can load several small objects before the first yield.

DuckDB.NET requires `UseStreamingMode=true` as well as reader iteration. The connector enables it for streaming commands through the provider property.
A provider without that writable Boolean property is rejected for DuckDB streams. Buffered commands keep their existing provider behavior.
The DuckDB timeout and cancellation registration remain active through enumeration and are disposed before the command and connection are released.
Its deadline includes time spent processing yielded rows.

Memory consists of provider and parser buffers, the current root object, and caller-retained rows. A batch size limits row count, not bytes.
Sorting, aggregation, large nested collections, and provider-native execution can still require substantial memory.
Synchronous streams, callable result-set streams, write-output streams, and automatic batching helpers are outside this experiment.

See the [benchmark project](../SqlLinqer.Streaming.Benchmarks/README.md) and [measured experiment](experiments/result-streaming.md).
