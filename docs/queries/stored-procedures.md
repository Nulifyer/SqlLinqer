[Documentation](../README.md) · [Project README](../../README.md)

# Stored procedures

Define a procedure class with `SqlLinqerCallable<TInput, TOutput>` when it takes inputs, or `SqlLinqerCallable<TOutput>` when it does not.
The `SqlProcedure` attribute names the database procedure, `TInput` describes its arguments, and `TOutput` describes each returned row.
Call `Execute(input)` or `ExecuteAsync(input)` to get rows and standard response fields, as you do with a select.

Typed procedures support SQL Server and Azure SQL.
The procedure must already exist in the database.

## Procedures without inputs

Starting with `2.3.1`, use `SqlLinqerCallable<TOutput>` for a procedure with no input parameters.
No empty input model or input argument is required.

```csharp
using SqlLinqer.Modeling;

[SqlProcedure("GetUsers", Schema = "dbo")]
public sealed class GetUsers : SqlLinqerCallable<UserRow> { }

// Inside an application method, using its connector:
var procedure = new GetUsers();
var response = procedure.Execute(connector);
var asyncResponse = await procedure.ExecuteAsync(connector, cancellationToken: cancellationToken);
var rendered = procedure.Render(connector);
```

`TOutput` describes each returned row and can also be a scalar type such as `int`.
Without an explicit connector, `Execute()`, `ExecuteAsync()`, and `Render()` use the retained or current default connector.
Both callable forms support caller-owned transactions, per-call command timeouts, and asynchronous cancellation.
They return the same `SqlCallableResponse<TOutput>` with rows, return code, affected-row count, and automatically detected standard total outputs.
The existing `SqlLinqerCallable<TInput, TOutput>` form remains available for procedures that take inputs, including empty input models.

## Define the procedure and models

Use ordinary properties for inputs, including `string`, `string?`, and nullable value types.
Null inputs omit the parameter so the procedure can use its default.
Empty strings, zero, false, and other supplied values are sent as provided.

```csharp
using SqlLinqer.Modeling;

[SqlProcedure("PageUsers", Schema = "dbo")]
public sealed class PageUsers : SqlLinqerCallable<UserPageInput, UserPageRow> { }

public class UserPageInput
{
    [SqlParameter("manager_id")]
    public int? ManagerId { get; set; }

    [SqlParameter("name", Size = 200)]
    public string? Name { get; set; }

    public int? Offset { get; set; }
    public int? PageSize { get; set; }
}

public class UserPageRow
{
    public int Id { get; set; }

    [SqlColumn("display_name")]
    public string? Name { get; set; }
}
```

Neither model needs a table mapping, primary key, or model base class.
Enable nullable reference types in your application to use `string?`.
The runtime behavior for null is the same for `string` and `string?` properties.

Use `SqlParameter` and `SqlColumn` when database names differ from member names.
Use `SqlProcedure` on the procedure class for its database name and optional schema. No constructor is required.

## Execute and read the response

```csharp
var procedure = new PageUsers();
var input = new UserPageInput { ManagerId = 12, Offset = 0, PageSize = 20 };
var response = procedure.Execute(input, connector);
if (response.State != SqlLinqer.ResponseState.Valid)
    throw response.Error;

var users = response.Result;              // List<UserPageRow>
long? total = response.TotalResults;      // Unpaged count supplied by the procedure
int? returnCode = response.ReturnCode;    // SQL RETURN value
int? rowsAffected = response.RowsAffected;
```

`Execute(input)` uses the current default connector.
Pass a connector to `Execute(input, connector)` to use it for that call.
A derived class can retain an injected connector through `base(connector)` when needed.

For asynchronous execution:

```csharp
var response = await new PageUsers().ExecuteAsync(
    input, connector, cancellationToken: cancellationToken);
```

Reuse the procedure instance for later calls with different inputs.
Each call reads the input member values before execution starts. Later property assignments do not change an active call.
Async execution copies arrays and `DataTable` inputs before awaiting the provider.
Keep input models unchanged while a call is capturing their values. Other provider-specific reference values must remain unchanged until the call finishes.

The following procedure supplies defaults for omitted inputs and an unpaged count even when the requested page is empty:

```sql
CREATE PROCEDURE dbo.PageUsers
    @manager_id int = NULL,
    @Offset int = 0,
    @PageSize int = 20,
    @TotalResults bigint OUTPUT,
    @name nvarchar(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @TotalResults = COUNT_BIG(*)
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

Supply the procedure name and schema separately in `SqlProcedure`.
Without an explicit schema, execution uses the connector's default schema.
If both are absent, SQL Server resolves the unqualified name.
`procedure.Render(input, connector)` returns SQL and its parameters without opening a connection.

## Standard procedure response

`Execute` returns `SqlCallableResponse<TOutput>`:

| Member | Meaning |
| --- | --- |
| `State`, `Error` | Execution and mapping status, following other SqlLinqer responses. |
| `Result` | `List<TOutput>`, including an empty list for an empty result set. |
| `TotalResults` | Nullable `long` from a standard total output, detected automatically. `0` is a known zero. `null` means unavailable. |
| `ReturnCode` | Nullable `int` from SQL `RETURN`. A nonzero code does not automatically set an error state. |
| `RowsAffected` | Nullable `int` from the provider. `SET NOCOUNT ON` can suppress this count. It is separate from the unpaged total. |

Totals require no C# parameter configuration or extra properties on your input or output model.
If the SQL procedure exposes `@TotalResults OUTPUT`, SqlLinqer reads it automatically.
The earlier `@total_count OUTPUT` spelling is also recognized. Prefer `bigint` for the count.
A procedure without either output returns `null` for `TotalResults`.
Counts are never inferred from returned rows or affected-row counts.
The standard count output must use an integer SQL type and must not also appear in the input model.
A SQL NULL count remains `null`. Invalid, overflowing, fractional, or negative counts produce an error response.

The procedure must return one tabular result set. Missing or additional sets produce an error response.
Mapping and execution failures clear both rows and metadata. SqlLinqer rolls back a transaction it created for the failed call.
Use `Execute(input, transaction)` with a default or retained connector, or `Execute(input, connector, transaction)` with an explicit connector.
You control a supplied transaction's commit, rollback, and disposal.
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

```csharp
var useDefault = new UserPageInput { Name = null };
var passEmpty = new UserPageInput { Name = string.Empty };
var passValue = new UserPageInput { Name = "Alice" };
```

Leaving a property null omits it. SqlLinqer does not insert a replacement value.
The database applies the declared procedure default, which may itself be SQL NULL.
If a required scalar parameter has no default, the database reports the missing argument.

If you need to distinguish omission from an explicit SQL NULL, declare that property as `SqlArgument<string>` or `SqlArgument<string?>`.
`SqlArgument<string>.Omitted` omits it, and `new SqlArgument<string>(null)` explicitly sends SQL NULL.
A string value, including `string.Empty`, supplies that value.
This wrapper is optional. Ordinary input properties do not need it.

For a table-valued input, declare a `DataTable` property with the existing SQL table type:

```csharp
public class BatchInput
{
    [SqlParameter("ids", TypeName = "dbo.IdList")]
    public System.Data.DataTable? Ids { get; set; }
}
```

The table type must already exist. Supply a `DataTable` with matching column types and order when passing rows.
Use an empty table for zero rows. A null property omits the parameter.
An unqualified `TypeName`, such as `IdList`, uses the procedure schema or the connector's default schema. Use a qualified name when the table type belongs to another schema.
