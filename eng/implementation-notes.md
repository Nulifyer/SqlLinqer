[Contributor documentation](README.md) · [User documentation](../docs/README.md)

# Query and execution implementation notes

These notes describe library implementation and extension points.
For application examples, start with the [user documentation](../docs/README.md).

## Capture ownership and cost

Capture copies mutable query objects, initialized model relationships, table and column aliases, and their lists and dictionaries.
It preserves shared-node identity and relationship cycles within the copied graph.
CLR types and reflection member descriptors are shared.
Insert capture reads only the mapped values, avoiding copies of application entities and their navigation graphs.

Arrays, binary buffers, and `DataTable` values are copied, including mutable cells in a `DataTable`.
Other provider-specific scalar objects and extension-owned reference data must remain immutable for the snapshot's lifetime.
Procedure rendering retains TVP references. Async procedure execution snapshots arrays and TVP data before awaiting the provider.

`SnapshotGraph` caches field layouts by runtime type as a compatibility adapter for the existing public query objects.
New reference fields on those objects participate in capture without a separate clone implementation for each dialect.
This also retains user-derived query, selection, predicate, and ordering implementations.

Capture costs scale with the reachable query graph and captured values.
Each render copies the private captured graph into mutable working state.
The current implementation does not provide constant-time capture or persistent immutable nodes.
It avoids copying on every fluent builder call and does not re-reflect the model's attributes on each capture.
Compilation caching is separate work and is not enabled.

## Preparation and dialect rendering

Select compilation has separate owners:

1. `SnapshotGraph` isolates the definition and its input values.
2. `SelectPreparation` derives pagination ordering and renders expressions that discover joins and CTEs on the working graph.
3. `PreparedSelect` retains the resulting clauses and paging values without retaining the builder.
4. `SqlDialect` assembles SELECT and count SQL for SQL Server, Azure SQL, PostgreSQL, MySQL, SQLite, or DuckDB.
5. `NativeSelectCompiler` builds native scalar and complete-root plans.
6. `NativeResultMapper` and SQLite's `LocalNativeResultMapper` share `NativeMaterializer`, SQL model binding, and checked value conversion.

The compiler preserves selected shapes, aliases, local paging, aggregate grouping, and CTE filters on detached working graphs.
Server providers use root-correlated APPLY/LATERAL records with occurrence positions and a count scoped to the current root.
SQLite uses prepared child cursors on the root's embedded connection, loading only the current root's selected descendants.
See [native streaming](query-pipeline.md) for the exact protocol, DISTINCT behavior, and provider costs.

## Streaming implementation

The same native materializer supports buffered sync/async reads and asynchronous streaming.
The positional reader yields after receiving the current root's declared records, without reading another root.
Malformed counts, duplicate headers, incomplete paths, and truncated roots fail before yielding a partial model.
Flat default-constructed rows assign directly through cached setters; constructor-bound flat rows reuse execution-local staging and constructor argument buffers.
Collections, strings, output models, and owned binary values still allocate.

DuckDB.NET streaming requires its writable Boolean `UseStreamingMode` property.
Deadline and cancellation registrations remain active through enumeration, including time spent processing a yielded row.
An unfinished stream requests provider cancellation before disposing the reader, and releases only resources owned by SqlLinqer.
Requested logical sorts, aggregates, provider buffering, root DISTINCT state, and large nested roots still have memory/latency costs.

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

Import `System`, `System.Threading`, `System.Threading.Tasks`, `SqlLinqer`, and `SqlLinqer.Components.Render` for this override.
Override both hooks when the same policy must apply to both execution styles.

Override `ExecuteCommandAsyncStream` for policies that cover the complete streaming enumeration, including early disposal.
`ExecuteCommand` and `ExecuteCommandAsync` cover buffered execution only.
Consumer exceptions occur outside the execution hook and retain their original type.
