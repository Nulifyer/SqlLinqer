# SQLite setup and integration checks

Run this executable from the repository root with the .NET 10 SDK:

```sh
dotnet run --project SqlLinqer.Tests -- --suite sqlite
```

It creates a temporary file database and a named shared-memory database, exercises both through `ReplicatorConnector`, and deletes its file afterward. No database server or cloud account is required. The project references `Microsoft.Data.Sqlite` 10.0.13. The suite runs against SQLite 3.53.3 and the minimum SQLite 3.38.0, with .NET 10.0.13. The executable prints the provider and runtime versions on each run.

The core library remains `netstandard2.0` and has no SQLite provider dependency. Applications must reference their own provider package. This implementation requires SQLite 3.38 or newer with JSON enabled. SQLite builds can omit JSON, so check the runtime you deploy. See [SQLite JSON support](https://www.sqlite.org/json1.html#compiling_in_json_support).

## Validate the minimum runtime

On Linux x64, install Python 3, a C compiler available as `cc`, and the .NET 10 SDK, then run:

```sh
python3 SqlLinqer.Tests/Sqlite/validate-minimum.py
```

The script downloads the official SQLite 3.38.0 amalgamation, verifies its pinned archive SHA-256 and the `sqlite3.c` SHA3-256 published in the [3.38.0 release notes](https://www.sqlite.org/releaselog/3_38_0.html), and compiles `libe_sqlite3.so`. It publishes the test executable into a new temporary directory and replaces only that directory's native provider asset. Both database modes must print and assert `SQLite 3.38.0`; loading another engine fails validation. The temporary source, library, and published output are removed on exit. System libraries and the NuGet cache are never replaced. The normal command above continues to use the provider's bundled modern engine.

## Connect and query

```csharp
using Microsoft.Data.Sqlite;
using SqlLinqer;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

using var example = new SqliteConnection("Data Source=example.db;Default Timeout=15");
var connector = new ReplicatorConnector(example, DbFlavor.Sqlite,
    new ConnectorOptions { CommandTimeout = TimeSpan.FromSeconds(15) });

example.Open();
using (var command = example.CreateCommand())
{
    command.CommandText = "CREATE TABLE IF NOT EXISTS main.people (Id INTEGER PRIMARY KEY, Name TEXT)";
    command.ExecuteNonQuery();
}
var inserted = new InsertWithPrimaryKeyQuery<Person, int>()
    .InsertAuto(new Person { Name = "Ada" }).Execute(connector);
if (inserted.State == ResponseState.Error)
    throw inserted.Error;

var page = new SelectQuery<Person>().SelectRootColumns()
    .OrderBy(x => x.Id, SqlDir.ASC).Page(1, 20)
    .ExecuteWithTotalCount(connector);
if (page.State == ResponseState.Error)
    throw page.Error;

[SqlTable("people", "main")]
public class Person
{
    [SqlPrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
}
```

`SqlPrimaryKey` defaults to a database-generated key. It excludes that column from `InsertAuto`. Use `SqlPrimaryKey(false)` for application-generated keys, including GUIDs. SQLite tables that generate integer keys should declare `INTEGER PRIMARY KEY`.

For memory databases, keep an open connection to `Data Source=example;Mode=Memory;Cache=Shared` for the database's lifetime. `ReplicatorConnector` creates a new connection for each operation. A bare `Data Source=:memory:` creates a separate database on every connection. The integration executable demonstrates the keeper connection.

## Schemas, connections, and transactions

A model schema names a SQLite database. `main` names the primary database, `temp` names the connection's temporary database, and another name identifies an attached database. Names are quoted as identifiers, including embedded quotes. Supply raw names, without SQL quoting. A null schema leaves the table unqualified and follows SQLite's lookup rules. An explicit schema is never discarded or translated from `dbo`; an unattached name fails in SQLite.

Attachments and temporary tables belong to a connection. `ReplicatorConnector` copies the connection string, not attachments. Attach databases on the connection used by an explicit transaction, then pass that transaction to every related query. Alternatively, implement `BaseConnector.CreateConnection()` to initialize each new connection. The executable tests attached reads, updates, deletes, and a missing database name.

Owned model writes use serializable transactions, including writes returning rows through `RETURNING`. Supplied transactions retain caller ownership after success or failure. The caller commits, rolls back, and disposes them. This also lets a caller keep several writes atomic. See [Microsoft.Data.Sqlite transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions).

`ConnectorOptions.CommandTimeout` sets command lock-wait timeouts. `ReplicatorConnector` preserves SQLite's `Default Timeout` connection-string setting and does not inject `Connect Timeout`. `ConnectionTimeout` has no SQLite equivalent and is ignored. Provider-created commands, including transaction initialization, use the provider default timeout. See [provider connection strings](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings).

## Query behavior

`Render(DbFlavor.Sqlite)` and `Render(connector)` generate `@P1` parameters, double-quoted identifiers, and escaped string literals. `Top`, `Page`, and `Limit` use `LIMIT/OFFSET`. Choose an explicit order for stable pagination. As with the existing paging controls, zero means no limit.

Typed SELECTs return one JSON object per root row. One-to-one relationships return an object or null. One-to-many relationships return arrays, including `[]` for no matches. Nested objects remain JSON objects. For relationship arrays, a derived query orders and limits source rows before an outer `json_group_array(json(...))`. Root counts remove pagination and count the result rows, preserving grouping and `DISTINCT`. CASE expressions group by their result values, and ordering an anonymous projection uses its selected scalar or aggregate expression before pagination. Cast aggregate projections to the required JSON type, for example `(double)q.Avg(x => x.Id)` and `(int?)q.Sum(x => x.Id)` when an empty set can produce null.

INSERT, UPDATE, and DELETE support output columns and primary-key capture with `RETURNING`. Returned columns have no table or alias qualification. Output order is not guaranteed, and returned values precede changes made by AFTER triggers. Virtual tables can restrict `RETURNING`. See [SQLite RETURNING limitations](https://www.sqlite.org/lang_returning.html#limitations_and_caveats).

Joined UPDATE uses `UPDATE ... FROM` with the original source joins and a separate target alias, matched by the target primary key. Assign constants, target columns, joined source columns, and target-column arithmetic through the typed API. LEFT JOIN nulls remain null, and related CTE filters still select eligible targets. All assignments read the original row values, including when changing a primary key. `RETURNING` and primary-key capture report the updated target values. Joined DELETE selects target keys through the original joins and WHERE predicates. Both require the library's declared single-column primary key.

When several source rows match a target, SQLite updates that target once using one arbitrary matching source row. The library preserves this [native UPDATE FROM behavior](https://www.sqlite.org/lang_update.html#update_from), without adding `LIMIT 1` or an order. The chosen row can change between executions or engine versions. Ensure the source join is unique when the assigned value must be deterministic. A to-many filter selects eligible root rows; it does not impose an ordering on joined assignment sources.

LIKE patterns use `ESCAPE '\'`. For literal text, call `text.EscapeForSqlLike(DbFlavor.Sqlite)` and add `%` wildcards only where intended. SQLite's own LIKE case rules apply. `LIKEWORD` and `NOTLIKEWORD` translate these patterns to native GLOB predicates with ASCII boundaries. They preserve the existing library boundary range `0-9A-z`, which includes underscore and the ASCII punctuation between `Z` and `a`. Non-ASCII characters count as boundaries. Matching follows the connection's built-in LIKE case setting; ASCII letters fold by default. Null operands remain null, so neither positive nor negative predicates include null rows. `%`, `_`, and backslash escapes keep their LIKE meanings, while GLOB metacharacters in the input remain literal. Custom replacements for SQLite's LIKE or lower functions are outside this translation's contract. `IN`, `NOT IN`, empty arrays, and null predicates are supported. Other quantified subqueries throw `NotSupportedException`.

Supported function translations include `DAY`, `MONTH`, `YEAR`, `LEFT`, `RIGHT`, `SUBSTRING`, `COUNT_BIG`, `STRING_AGG`, `CONCAT`, and `CONCAT_WS`. SQLite supplies `ABS`, case conversion, trims, `COALESCE`, and the usual aggregates. Date extraction uses `strftime` and follows SQLite's UTC normalization for offset timestamps. Substrings use SQLite's indexing rules; positive starts are one-based. Unsupported extension functions, including `REVERSE`, `SOUNDDEX`, checksum, variance, and standard deviation, throw `NotSupportedException`.

Stored procedures, TVPs, and `TruncateQuery` are explicitly unsupported. Use DELETE for table clearing. Multirow inserts with selected columns work; default-only inserts accept one row per query.

## Type round trips

The tests cover booleans, enums, GUIDs, nullable values, DateTime, DateTimeOffset, decimals, and BLOBs. Booleans and enums use integers. GUIDs and dates use the provider's TEXT representation. Existing System.Text.Json model options and database converters load the results. Integer primary-key outputs convert SQLite's Int64 values to the requested key type; GUID outputs parse the provider's text.

Use TEXT affinity for exact decimal storage. SQLite NUMERIC or REAL affinity and arithmetic or numeric aggregates can lose decimal precision. DateTime's clock value and fractional seconds round-trip, but its `Kind` does not, because the provider's text omits that information. DateTimeOffset preserves its offset. Unsigned integers above Int64.MaxValue cannot round-trip through SQLite INTEGER. Alternate Julian-day dates and binary GUID storage are not supported by the typed JSON loader. These constraints follow the [provider type mappings](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types).

Direct byte[] column projections encode BLOBs as hex inside JSON. A SQLite-only converter restores byte arrays and distinguishes empty BLOBs from null. Tabular outputs retain raw BLOBs. Computed BLOB expressions need an explicit encoding; SQLite JSON objects cannot directly contain BLOB values. Raw JSON submitted to `ExecuteReaderJson` must use hex for byte[] fields with this connector.
