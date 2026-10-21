[README](../README.md)

# DuckDB

`DbFlavor.DuckDb` has value `4`. It renders DuckDB SQL, `$P1` named parameters, JSON objects, nested JSON arrays, and `RETURNING` clauses. SQL Server, MySQL, and PostgreSQL retain enum values `0`, `1`, and `2`. The core library remains .NET Standard 2.0 and has no DuckDB provider dependency.

## Setup

Add `DuckDB.NET.Data.Full` to the executable that uses SqlLinqer. The integration executable targets .NET 10 and pins provider `1.5.6.1`, which includes native DuckDB `v1.5.6`.

```csharp
using DuckDB.NET.Data;
using SqlLinqer;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;

using var example = new DuckDBConnection("Data Source=application.duckdb");
var connector = new ReplicatorConnector(example, DbFlavor.DuckDb);
var response = new SelectQuery<Person>()
    .SelectRootColumns()
    .Where(x => x.Name, "Ada", SqlOp.EQ)
    .Execute(connector);
```

`Person` must be a mapped model and its table must already exist. SqlLinqer does not create tables or install extensions. `ReplicatorConnector` copies the connection string without adding a server timeout option. File databases persist across connections created by the connector.

Each plain `Data Source=:memory:` connection has its own database. Replicating that connection does not share its tables. For an in-memory database, keep its connection open and pass a transaction from that connection to every query:

```csharp
using var connection = new DuckDBConnection("Data Source=:memory:");
connection.Open();
// Create tables and seed data on connection before querying.
using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Snapshot);
var connector = new ReplicatorConnector(connection, DbFlavor.DuckDb);
var response = new SelectQuery<Person>().SelectRootColumns().Execute(connector, transaction);
transaction.Commit();
```

The connector uses `Snapshot` for transactions it creates. DuckDB.NET accepts `Snapshot` or `Unspecified`; it rejects `ReadCommitted`. The caller owns a supplied transaction's commit, rollback, and disposal. SqlLinqer does not close its connection, including after a query error.

The selected provider package `DuckDB.NET.Data.Full` 1.5.6.1 supplies managed targets for .NET 8 and .NET 10. It does not target .NET Framework 4.8. The core remains `netstandard2.0`; the `net48` demo build verifies core compatibility, not native DuckDB execution. This suite ran on Linux x64. Windows, macOS and ARM native execution remain unverified.

## Queries and results

- Root typed selects return one `json_object` per row. Relationships produce nested JSON. Missing to-one relationships deserialize as `null`; empty to-many relationships deserialize as empty lists.
- Nested selects apply ordering and pagination before `json_group_array`. The current typed API has no public nested paging method. The integration test configures the core query collections to verify this renderer path.
- `Top`, `Page`, `Limit`, parameterized `IN` subqueries, related-row filters, and related-row ordering work. Count queries wrap the unpaged projection, so grouped and distinct totals count result rows. DuckDB paging does not add hidden key columns to grouped or distinct projections. Specify `OrderBy` when page ordering matters.
- Inserts, updates, and deletes support affected-row counts and returned rows through `ExecuteWithOutput`. The primary-key query classes capture keys through `RETURNING`. Empty returned result sets have zero rows.
- Generated keys require a database default, such as a sequence or `uuid()`. A primary-key model attribute alone does not create a database sequence or default. Models with no insertable columns support default-only inserts, including batches and returned keys.

```sql
CREATE SEQUENCE person_ids START 100;
CREATE TABLE people (
    Id INTEGER PRIMARY KEY DEFAULT nextval('person_ids'),
    Name VARCHAR
);
```

```csharp
using SqlLinqer.Modeling;
using SqlLinqer.Queries.TypedExt;

[SqlTable("people"), SqlAutoInsert]
public class Person
{
    [SqlPrimaryKey, SqlAutoInsertExclude]
    public int Id { get; set; }
    public string Name { get; set; }
}
```

In application code, after creating the table:

```csharp
var response = new InsertWithPrimaryKeyQuery<Person, int>()
    .InsertAuto(new Person { Name = "Ada" })
    .Execute(connector);
// response.TouchedPrimaryKeys contains the generated INTEGER key.
```

Joined updates use `UPDATE FROM` with the original LEFT JOIN tree. Joined deletes select distinct target keys before deleting, which avoids a native `DELETE USING` error for repeated indexed rows. Both require a mapped primary key on the target that uniquely identifies its database rows. Assignments must target the root table; expressions targeting a related table reject before execution. Source-column assignments preserve database types and missing LEFT JOIN values become SQL NULL. Root and related-row filters, including generated CTEs, apply before selecting targets. `RETURNING` exposes target columns and keys.

Joined updates reject multiple source matches for any selected target key with the database error `Joined DuckDB updates require at most one source match per target primary key.` This check runs inside the write statement. It does not choose an arbitrary source row. Constrain the source relationship or its filters to produce one match. Deletes remove each target once even if the source matches several times. The native suite verifies no-match outputs, duplicate matches, owned writes, caller transactions and rollback.

`TRUNCATE TABLE` executes directly. DuckDB defines it as an alias for deleting all rows. It does not reset a sequence.

`Render(DbFlavor.DuckDb)`, `Render(connector)`, and rendering into an empty `ParameterCollection` all generate `$P` placeholders. Commands bind provider parameter names without `$`. To add parameters before rendering, construct `new ParameterCollection(DbFlavor.DuckDb)`. Imported parameter names must already use DuckDB syntax, such as `$P1`; generated names skip existing names. Reusing a populated collection across `$` and `@` dialects throws `InvalidOperationException`. Each ordinary query render creates a fresh collection, so a query can render repeatedly for different dialects.

## Types and functions

The native suite covers boolean values, numeric and string enum JSON, GUIDs, DATE, TIMESTAMP, TIMESTAMPTZ, decimals, byte arrays, and nullable values. Model byte-array columns render through `to_base64` for System.Text.Json. Raw tabular results retain `Guid`, `decimal`, and `DateTime` values. `ExecuteScalar<byte[]>` also copies BLOB data before closing its reader. DuckDB.NET returns BLOB values as streams over native result memory, so the connector copies them to `byte[]` before closing the reader. It uses a DuckDB-specific table reader instead of `DataTable.Load` for this reason. Binary equality uses one BLOB parameter; byte-valued numeric columns can still use byte arrays as `IN` lists.

Enum parameters bind their underlying numeric value. Use numeric database columns for enum writes and predicates. Enum names stored as strings can deserialize into enum properties. TIMESTAMPTZ preserves the instant; do not depend on preserving the input offset. DuckDB JSON formats dates and timestamps as strings, which the existing database date converters accept.

Value-based `LIKE` and `NOTLIKE` use a backslash `ESCAPE` clause. Use `EscapeForSqlLike(DbFlavor.DuckDb)` to match literal `%`, `_`, and backslash characters.

Common date and string functions use DuckDB's functions. `LEFT` projects the requested prefix through one-based `SUBSTRING`. CASE projections group by the full expression; CASE, functions and ordering reuse the rendered expression and its parameter identities. `COUNT_BIG`, `STDEV`, `STDEVP`, `VAR`, and `VARP` map to `COUNT`, `STDDEV_SAMP`, `STDDEV_POP`, `VAR_SAMP`, and `VAR_POP`. Function arguments reuse placeholders when the same expression appears in `SELECT` and `GROUP BY`.

The existing projection builder declares `Avg` and `Sum` as returning `int`. Use a projection with the needed numeric type, for example `new { Average = (double)x.Avg(p => p.Id) }`, when the database result is fractional or exceeds `Int32`. Primary-key captures convert compatible numeric types, including INTEGER to `long`, and preserve UUID keys as `Guid`. Narrowing overflow reports an error. Scalar nullable results preserve SQL NULL; integral SUM results returned as `BigInteger` convert to the requested numeric type with overflow checking.

## Limits

Stored-procedure queries, SQL Server table-valued parameters, `SOUNDDEX`, and `CHECKSUM_AGG` throw `NotSupportedException`. DuckDB macros and table functions require explicit SQL through a `RenderedQuery`. This implementation does not add schema migrations, bulk append APIs, spatial types, or extension-specific mappings.

`ConnectionTimeout` has no DuckDB connection-string effect. Provider `1.5.6.1` stores `CommandTimeout` without enforcing it. SqlLinqer enforces `ConnectorOptions.CommandTimeout` with a scoped timer that calls `DbCommand.Cancel()`, which interrupts the native connection. The deadline covers execution and result loading. Zero disables the deadline; negative values and values above 4294967294 milliseconds report an error. The timer repeats cancellation after expiry to cover an interrupt racing command startup. Completion and cancellation share a lock, so a callback cannot interrupt a later command after the execution scope ends. Other database backends retain their provider timeout behavior.

A timed-out query returns an error response containing a `TimeoutException`. Owned transactions roll back. Generated writes with `RETURNING` also keep their owned transaction open until result loading and the deadline check finish. A supplied transaction stays open and belongs to the caller, but native interruption leaves it aborted. Call `Rollback` before starting another transaction on that connection. Do not execute concurrent commands on the same connection. The native suite interrupts an effectively unbounded calculation on scalar, nonquery, JSON and tabular paths, then verifies rollback and connection reuse. It makes no elapsed-time assertion. `SQLLINQER_DUCKDB_TIMEOUT_PROBE=1` remains an optional diagnostic of the raw provider's ignored `CommandTimeout`; it bypasses the connector deadline.

`LIKEWORD`, `NOTLIKEWORD`, and `NOTLIKEWORDORNULL` use parameterized `regexp_matches` expressions. They preserve the library's existing ASCII `0-9` and `A-z` word-boundary definition. The range `A-z` includes underscore and the punctuation between `Z` and `a`; it is not a Unicode word definition. Matching is case sensitive, as with native DuckDB LIKE. `%` and `_` retain LIKE wildcard semantics, backslash escapes the next character, and regex punctuation is literal. Use `EscapeForSqlLike(DbFlavor.DuckDb)` for literal values. Negation excludes SQL NULL except for `NOTLIKEWORDORNULL`. Groups, arrays and related-row filters use these same rules. SQL Server rendering keeps its existing bracket-pattern behavior.

## Verification

Run from the repository root:

```sh
dotnet run --project SqlLinqer.Tests -- --suite duckdb
dotnet run --project SqlLinqer.Tests -- --suite regression
dotnet build SqlLinqer.Tests.Net10/SqlLinqer.Tests.Net10.csproj
dotnet build SqlLinqer.Tests.NetFramework48/SqlLinqer.Tests.NetFramework48.csproj
```

The DuckDB suite loads the package's native engine and creates temporary file and in-memory databases. It verifies typed and nested JSON, nested ordering and paging, scalar types, generated keys, affected counts, returned rows, parameter import, direct and repeated rendering, transactions and rollback, grouped and distinct totals, subqueries, joined writes, duplicate-match policy, default-only inserts, numeric conversion, CASE/function grouping, native cancellation, provider DataTable loading, persistence, and unsupported-operation guards. Native execution was checked on Linux x64. Other runtime assets are supplied by the package but were not executed here.

References: [DuckDB.NET usage and named parameters](https://duckdb.net/docs/basic-usage.html), [JSON creation](https://duckdb.org/docs/current/data/json/creating_json), [JSON aggregation](https://duckdb.org/docs/current/data/json/json_functions), [INSERT and RETURNING](https://duckdb.org/docs/current/sql/statements/insert), [DELETE and TRUNCATE](https://duckdb.org/docs/current/sql/statements/delete), [DuckDB.NET transaction implementation](https://github.com/Giorgi/DuckDB.NET/blob/1.5.6.1/DuckDB.NET.Data/DuckDBTransaction.cs), [DuckDB.NET command implementation](https://github.com/Giorgi/DuckDB.NET/blob/1.5.6.1/DuckDB.NET.Data/DuckDBCommand.cs).
