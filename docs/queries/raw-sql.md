[Documentation](../README.md) · [Project README](../../README.md)

# Run hand-written SQL

Use `BeginSql` for a statement that the typed builders do not cover.
You write the SQL. SqlLinqer binds the values, runs the command through your connector, and returns a `SqlResponse`.
These examples use a [configured connector](../getting-started/connections.md). Import `SqlLinqer` for the response helpers.

## Name the values

Mark each value in the SQL with `@name`. Supply the values as an object whose property names match the markers:

```csharp
var affected = (await connector
    .BeginSql("UPDATE jobs SET state = @state WHERE id = @id", new { state = "ready", id })
    .ExecuteAsync(transaction, cancellationToken))
    .GetResultOrThrow();
```

Write `@name` for every database. SqlLinqer renders the placeholder that the target database expects.
The values are bound as parameters. They are never concatenated into the SQL text.

A marker and its value match without regard to case. A marker that appears twice binds one parameter.
A marker name starts with a letter or underscore and continues with letters, digits, and underscores. Letters outside ASCII are allowed.
SqlLinqer ignores `@` text inside string literals, quoted identifiers, comments, and PostgreSQL dollar-quoted strings.

Each value binds as one parameter. A list is not expanded into several placeholders.
On PostgreSQL, pass an array and compare with `= ANY(@ids)`. On other databases, write one marker for each value.

`Values` also accepts a dictionary with string keys, such as `Dictionary<string, object>`. Use it when the value names are not known at compile time.

## Supply one value at a time

`Value` sets one value. It replaces an earlier value that has the same name:

```csharp
var response = connector
    .BeginSql("INSERT INTO users (username, email) VALUES (@username, @email)")
    .Value("username", username)
    .Value("email", email, DbType.String)
    .Execute(transaction);
```

Pass a `DbType` for a null whose type the server cannot infer. Import `System.Data` for `DbType`.

## Find a mismatch before the command runs

SqlLinqer checks the markers against the values when it renders the command. It opens no connection first.

- A marker without a value throws `InvalidOperationException`.
- A value without a marker throws `InvalidOperationException`.

Some SQL declares its own variables, such as T-SQL `DECLARE @total` or a MySQL session variable.
Call `IgnoreUnmatchedMarkers()` for that SQL. SqlLinqer then leaves `@name` text without a value unchanged.
A value without a marker is still an error.

## Choose a terminal

Each terminal has a synchronous form and an `Async` form. Each accepts an optional `commandTimeout`.

| Terminal | Returns | Use it for |
| --- | --- | --- |
| `Execute` | `SqlResponse<int>` | A write. The result is the affected-row count. |
| `ExecuteScalar<T>` | `SqlResponse<T>` | The first value of the first row. |
| `ExecuteList<T>` | `SqlResponse<List<T>>` | Rows mapped to a class, or one column read into a scalar list. |
| `ExecuteDataTable` | `SqlResponse<DataTable>` | Rows in a `DataTable`. Dispose the table after use. |

`ExecuteList<T>` maps columns to members by name, without regard to case.
Alias a column when its name differs from the member, such as `created_utc AS CreatedUtc`, or use `SqlColumn` on the member.
See [reading rows from raw SQL](../execution/sync-and-async.md#read-rows-from-raw-sql) for the mapping rules.

```csharp
var leases = connector
    .BeginSql("SELECT document_id, generation FROM document_leases WHERE document_id = ANY(@ids)", new { ids })
    .ExecuteList<LeaseRow>()
    .GetResultOrThrow();
```

`Render(connector)` returns the SQL and its parameters without executing them.

Like the other builders, every terminal also accepts a connector as its first argument.
Use that form with `new RawQuery(sql, values)` when no connector is bound, or to run one command on another connector.
`Tag("name")` names the command for [diagnostics](../execution/diagnostics.md).

## Run on one open connection

Pass an open `DbConnection` to run the command on exactly that connection.
Use this form for session state, such as a PostgreSQL advisory lock or a temporary table:

```csharp
using var connection = await connector.OpenConnectionAsync(cancellationToken);

await connector.BeginSql("SELECT pg_advisory_lock(@key)", new { key })
    .ExecuteAsync(connection, cancellationToken);

var held = (await connector.BeginSql("SELECT count(*) FROM pg_locks WHERE locktype = 'advisory'")
    .ExecuteScalarAsync<long>(connection, cancellationToken))
    .GetResultOrThrow();
```

On a supplied connection, SqlLinqer opens no transaction. It never closes or disposes the connection.
The connection must be open. A closed connection returns an error response.
If the connection has an active transaction, pass that transaction instead of the connection.

## Control the owned transaction

When you pass a transaction, the command uses it. You commit or roll it back.

When you pass neither a transaction nor a connection, SqlLinqer uses a pooled connection:

| Terminal | Default on a pooled connection |
| --- | --- |
| `Execute`, `ExecuteScalar<T>` | Opens a transaction, commits on success, and rolls back on failure. |
| `ExecuteList<T>`, `ExecuteDataTable` | Opens no transaction. |

Call `WithoutTransaction()` to make `Execute` or `ExecuteScalar<T>` run without that transaction.
Use it for a read, and for a statement that cannot run inside a transaction:

```csharp
var total = connector
    .BeginSql("SELECT count(*) FROM reader_events WHERE kind = @kind", new { kind = "view" })
    .WithoutTransaction()
    .ExecuteScalar<long>()
    .GetResultOrThrow();
```

The default transaction keeps a command that contains several statements atomic.

## Errors

A marker mismatch throws before execution. An execution failure returns an error response that contains `SqlResponseException`.
See [where errors appear](../execution/responses-and-errors.md#where-errors-appear).
