[Documentation](../README.md) · [Project README](../../README.md)

# Stored procedures

`StoredProcedureQuery<TInput, TOutput>` executes a procedure with an input model and an output row model.
Like a select, `Execute()` returns rows directly in `response.Result`.
`SqlStoredProcedureResponse<TOutput>` also provides `TotalResults`, `ReturnCode`, and `RowsAffected`.

Typed procedures support SQL Server and Azure SQL.
Other database flavors throw `NotSupportedException` before opening a connection.
The procedure must already exist in the database.

## Define input and output models

Input members become procedure parameters. Output members map to columns in the procedure's single tabular result set.
Use `SqlParameter` and `SqlColumn` when database names differ from member names.
Neither model needs a table mapping or primary key.

```csharp
using SqlLinqer.Modeling;

public class UserPageInput
{
    [SqlParameter("manager_id")]
    public int? ManagerId { get; set; }

    [SqlParameter("name", Size = 200)]
    public SqlArgument<string> Name { get; set; }

    public int Offset { get; set; }
    public int PageSize { get; set; } = 20;
}

public class UserPageRow : SqlLinqerObject<UserPageRow>
{
    public int Id { get; set; }

    [SqlColumn("display_name")]
    public string Name { get; set; }
}
```

`SqlLinqerObject<UserPageRow>` enables the model factory used below.
Plain output classes also work with the query constructor.
The output type describes each row. It does not need a wrapper containing lists, totals, or return codes.

## Execute and read the response

```csharp
var input = new UserPageInput { ManagerId = 12, Offset = 0, PageSize = 20 };
var query = UserPageRow.BeginStoredProcedure(
    "PageUsers", input, connector,
    schema: "dbo", totalResultsParameter: "total_count");

var response = query.Execute();
if (response.State != SqlLinqer.ResponseState.Valid)
    throw response.Error;

var users = response.Result;              // List<UserPageRow>
long? total = response.TotalResults;      // Unpaged count supplied by the procedure
int? returnCode = response.ReturnCode;    // SQL RETURN value
int? rowsAffected = response.RowsAffected;
```

The factory requires the connector argument. Pass `null` to use the current default connector at execution time.
An explicit connector passed to `Execute(connector)` applies to that execution only.

For plain output classes, construct the query directly:

```csharp
using SqlLinqer.Queries.Typed;

var query = new StoredProcedureQuery<UserPageInput, UserPageRow>(
    "PageUsers", input, schema: "dbo", totalResultsParameter: "total_count");
var response = await query.ExecuteAsync(connector, cancellationToken: cancellationToken);
```

The constructor captures input member values once. Later property assignments do not change the query.
Reference values, including arrays and `DataTable`, are not deep-copied. Keep them unchanged until execution finishes.

The following procedure supplies an unpaged count even when the requested page is empty:

```sql
CREATE PROCEDURE dbo.PageUsers
    @manager_id int,
    @Offset int,
    @PageSize int,
    @total_count bigint OUTPUT,
    @name nvarchar(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @total_count = COUNT_BIG(*)
    FROM dbo.Users
    WHERE (@manager_id IS NULL OR ManagerId = @manager_id)
      AND (@name IS NULL OR display_name = @name);

    SELECT Id, display_name
    FROM dbo.Users
    WHERE (@manager_id IS NULL OR ManagerId = @manager_id)
      AND (@name IS NULL OR display_name = @name)
    ORDER BY Id
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
    RETURN 0;
END;
```

Supply the procedure name and schema separately. Without an explicit schema, execution uses the connector's default schema.
If both are absent, SQL Server resolves the unqualified name.
`query.Render(connector)` returns SQL and its parameters without opening a connection.

## Standard procedure response

| Member | Meaning |
| --- | --- |
| `State`, `Error` | Execution and mapping status, following other SqlLinqer responses. |
| `Result` | `List<TOutput>`, including an empty list for an empty result set. |
| `TotalResults` | Nullable `long` from the configured output parameter. `0` is a known zero. `null` means unavailable. |
| `ReturnCode` | Nullable `int` from SQL `RETURN`. A nonzero code does not automatically set an error state. |
| `RowsAffected` | Nullable `int` from the provider. `SET NOCOUNT ON` can suppress this count. It is separate from the unpaged total. |

Set `totalResultsParameter` to the name of the output parameter supplying the unpaged count.
The input model must not also declare that parameter. Prefer a `bigint OUTPUT` parameter in SQL.
Without this setting, `TotalResults` remains `null`. SqlLinqer does not infer totals from returned rows or affected-row counts.
A SQL NULL count remains `null`. Invalid, overflowing, fractional, or negative counts produce an error response.

The procedure must return one tabular result set. An empty set is a successful empty list.
Missing or additional tabular sets produce an error response.
Mapping and execution failures clear both rows and metadata. SqlLinqer rolls back a transaction it created for the failed call.
Pass `Execute(connector, transaction)` or its async equivalent to use your own transaction.
You control its commit, rollback, and disposal.
Requested async cancellation throws `OperationCanceledException`.

## Map output rows

Output properties and fields match column names without regard to case.
Use `SqlColumn` to map a different column name, and `SqlIgnore` for a member the procedure does not return.
Each mapped member needs one matching column. Extra result columns are ignored.
Use nullable properties for columns that can contain SQL NULL.

The row model needs a parameterless constructor or one public constructor whose parameter names and types match its mapped members.
Members not supplied by the constructor must be writable.
API JSON attributes do not affect SQL loading. Relationship members are not loaded from a flat result set.
See [result property types](../execution/value-conversions.md) for supported conversions.

## Supply optional inputs

Public readable input properties and fields become parameters. Use `SqlIgnore` for other members.
Use `SqlParameter` to set a SQL name, string size, precision, scale, or explicit database type.
Parameter names may include a leading `@`.

Use `SqlArgument<T>` when a procedure parameter has a database default:

```csharp
var useDefault = new UserPageInput { Name = SqlArgument<string>.Omitted };
var passNull = new UserPageInput { Name = new SqlArgument<string>(null) };
var passValue = new UserPageInput { Name = "Alice" };
```

An unassigned `SqlArgument<T>` uses the procedure's default.
An ordinary nullable member always supplies its value, including SQL NULL.

For a table-valued input, declare a `DataTable` property with the existing SQL table type:

```csharp
public class BatchInput
{
    [SqlParameter("ids", TypeName = "dbo.IdList")]
    public System.Data.DataTable Ids { get; set; }
}
```

The table type must already exist. Supply a non-null `DataTable` with matching column types and order.
Use an empty table when there are no input rows.

## Multiple result sets and custom output parameters

If your procedure returns several result sets or additional output parameters, see
[map multiple result sets and output parameters](stored-procedure-outputs.md).
