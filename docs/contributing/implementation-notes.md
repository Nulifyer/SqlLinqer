[Contributor documentation](README.md) · [User documentation](../README.md)

# Query and execution implementation notes

These notes describe library implementation and extension points.
For application examples, start with the [user documentation](../README.md).

## Capture ownership and cost

Capture copies mutable query objects, initialized model relationships, table and column aliases, and their lists and dictionaries.
It preserves shared-node identity and relationship cycles within the copied graph.
CLR types and reflection member descriptors are shared.
Insert capture reads only the mapped values, avoiding copies of application entities and their navigation graphs.

Arrays, binary buffers, and `DataTable` values are copied, including mutable cells in a `DataTable`.
Other provider-specific scalar objects and extension-owned reference data must remain immutable for the snapshot's lifetime.
Ordinary legacy procedure rendering retains its existing TVP reference behavior. Explicit procedure capture copies TVP data.

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
5. `JsonResultMapper` decodes database JSON using the selected model contract for synchronous and asynchronous execution.

Pagination, SELECT assembly, nested JSON projection, and relationship JSON wrapping now belong to the database compilers.
Existing expression and write implementations retain their specialized dialect handling.
Working graphs own discovered joins, CTEs, and temporary ordering. `RenderContext` owns the dialect and parameter collection.

CASE expressions with aggregates group their predicate columns and non-aggregate branch inputs.
Other CASE expressions group by their result, so equal results share one group.
Relationship projections group by every outer column used by their correlation predicates, including additional join conditions.

MySQL builds grouped fields before constructing JSON objects to satisfy `ONLY_FULL_GROUP_BY`.
Computed scalar inputs inside aggregate expressions use lateral projections before grouping.
MySQL and PostgreSQL apply `DISTINCT` to logical fields before JSON construction, with comparable JSON values for PostgreSQL relationships.
Ordering and paging use the resulting logical fields. Binary and relationship fields retain their JSON encodings.

Typed `BuildQuery()` ordering resolves nested members through the selected relationship aliases.
Collection ordering uses the minimum member value for ascending order and the maximum for descending order.
Missing relationships retain the provider's default NULL ordering. Add a unique final sort field for stable paging when values tie.
Ordering paths must end at a selected scalar member.

## Streaming implementation

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
Existing `JsonResultsRowByRow` overrides continue to control JSON transport decoding for both styles.

Override `ExecuteCommandAsyncStream` for policies that cover the complete streaming enumeration, including early disposal.
`ExecuteCommand` and `ExecuteCommandAsync` cover buffered execution only.
Consumer exceptions occur outside the execution hook and retain their original type.
