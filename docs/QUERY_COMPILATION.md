[README](../README.md)

# Query snapshots and compilation

Fluent builders remain mutable. `Capture()` creates a query definition that owns a detached copy of their current structure.
Changing the builder afterward does not change the snapshot.

```csharp
var query = User.BeginSelect(connector)
    .SelectRootColumns()
    .Where(x => x.Name, "Ada", SqlOp.EQ)
    .Page(2, 20);

var snapshot = query.Capture();
query.Top(1);

var page = snapshot.Render(connector);
var total = snapshot.RenderForCount(connector.DbFlavor);
```

`page` retains the original paging and predicate.
The count removes root paging and ordering while retaining projection, `DISTINCT`, grouping, and nested-query semantics.
Each render returns fresh SQL bindings. Count rendering does not change future data rendering.

## Repeated and concurrent rendering

A captured definition can render for several database flavors or from several callers.
Each compilation gets its own working graph and parameter collection.
Do not modify a shared external `ParameterCollection` concurrently.

To execute an explicitly captured select, pass its rendered query to the connector:

```csharp
var response = await connector.ExecuteReaderJsonAsync<User>(
    snapshot.Render(connector),
    cancellationToken: cancellationToken);
```

Capturing is not synchronized with concurrent builder mutation.
Finish constructing the builder before calling `Capture()` or starting execution.

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

## Logical result shape

`RenderedQuery.ResultShape` describes the selected members of regular SELECT renders.
Count-only and other rendered commands can leave this property null.
Each member has a name, CLR type, logical kind, and nested members.
The kinds are scalar, optional object, and collection.
Nested collections use empty arrays when no related rows match. Missing one-to-one results use null.
SQL expressions without a declared CLR type use `object` in this metadata.

```csharp
var rendered = User.BeginSelect(connector)
    .SelectRootColumns()
    .Select(x => x.Permissions)
    .Render(connector);

foreach (var member in rendered.ResultShape.Members)
    Console.WriteLine($"{member.Name}: {member.Kind}");
```

The shape is immutable metadata. Current typed loading still uses the existing JSON model contract and converters.
It does not add a direct reader mapper. SQL loading always uses SqlLinqer's model contract and ignores API JSON attributes and converters.
The database compiler determines the transport encoding independently of that logical structure.

## Verification

Run the local regression and embedded-provider checks:

```sh
dotnet run --project SqlLinqer.Tests
```

Compilation regression cases cover model and value isolation, repeated data/count compilation, and concurrent dialect rendering.
Async checks cover provider calls, cancellation, disposal, transaction ownership, batching, and callable outputs.
SQLite and DuckDB checks also execute the new interfaces against real embedded databases.
The integration suite includes asynchronous PostgreSQL/MySQL cases when its disposable endpoints are configured.

Use the existing render allocation benchmark to measure the copying cost:

```sh
dotnet run -c Release --project SqlLinqer.Tests -- --benchmark-render-only
```
