[README](../README.md)

# Call a typed stored procedure

`SqlLinqerCallable<TCallable, TArgs, TResult>` maps a procedure to typed arguments and results.
This interface supports SQL Server and Azure SQL. Other database flavors throw `NotSupportedException` before opening a connection.
Scalar functions and table-valued function composition are not implemented by this interface.

## Define the contract

Put `SqlProcedure` on the callable class. Its name identifies one SQL object; supply the schema separately.
If `Schema` is omitted, the call uses `ConnectorOptions.DefaultSchema`.
If both schemas are omitted, SQL Server resolves the unqualified name.

```csharp
using System.Collections.Generic;
using SqlLinqer.Modeling;

[SqlProcedure("SearchUsers", Schema = "dbo")]
public sealed class SearchUsers
    : SqlLinqerCallable<SearchUsers, SearchUsers.Args, SearchUsers.Result>
{
    public class Args
    {
        [SqlParameter("manager_id")]
        public int? ManagerId { get; set; }

        [SqlParameter("name", Size = 200)]
        public SqlArgument<string> Name { get; set; }

        [SqlParameter("counter")]
        public int Counter { get; set; }
    }

    public class Result
    {
        [SqlResultSet(0)]
        public List<UserSummary> Users { get; set; }

        [SqlOutputParameter("total_count")]
        public int TotalCount { get; set; }

        [SqlOutputParameter("counter")]
        public int Counter { get; set; }

        [SqlReturnValue]
        public int ReturnCode { get; set; }
    }
}

public class UserSummary
{
    public int Id { get; set; }

    [SqlColumn("display_name")]
    public string Name { get; set; }
}
```

This definition expects the following procedure contract. The procedure must already exist in the database.

```sql
CREATE PROCEDURE dbo.SearchUsers
    @manager_id int,
    @counter int OUTPUT,
    @total_count int OUTPUT,
    @name nvarchar(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, display_name
    FROM dbo.Users
    WHERE ManagerId = @manager_id AND (@name IS NULL OR display_name = @name);
    SET @total_count = @@ROWCOUNT;
    SET @counter = @counter + 1;
    RETURN 0;
END;
```

Nested `Args` and `Result` classes are optional. External classes work with the same generic base.
Different callables may share those classes. The first generic argument identifies the concrete callable and its procedure mapping.
The callable needs no private constructor. The result model needs a public parameterless constructor.

## Execute a call

```csharp
var args = new SearchUsers.Args { ManagerId = 12, Counter = 5 };
var call = SearchUsers.Call(args);
var response = call.Execute(connector);

if (response.State == SqlLinqer.ResponseState.Valid)
{
    var users = response.Result.Users;
    int total = response.Result.TotalCount;
    int updatedCounter = response.Result.Counter;
    int returnCode = response.Result.ReturnCode;
}
```

`Call` reads argument member values once and creates an invocation without executing SQL.
Later assignments to argument properties do not change that invocation.
Reference values, including arrays and `DataTable`, are not deep-copied. Keep them unchanged until execution finishes.
Metadata is cached per callable definition. Each execution creates its own command, parameter bindings, and result object.

Pass `Execute(connector, transaction)` to use an existing transaction.
The connector never commits, rolls back, or disposes that caller-owned transaction or connection.
Without a supplied transaction, execution opens a connection and transaction, then commits after loading all results and outputs.
Execution or mapping failures roll back an owned transaction and return an error response.
`Execute()` uses `SqlLinqer.Default.Connector`.

Definition and argument errors throw from `Call`. Unsupported flavors and invalid connector settings throw before execution.
Database, command-binding, and result-mapping errors follow the existing `SqlResponse<TResult>` error handling.
A nonzero procedure return code does not automatically make `response.State` an error.
Return codes, output values, and affected-row counts have separate meanings. This interface does not expose affected-row counts.

## Map parameters

Public argument fields and readable properties become inputs. `SqlParameter` is optional when the SQL name matches the member name.
Use `SqlIgnore` for members that are not parameters. Parameter names may include a leading `@`.
Calls bind by SQL parameter name; `Order` only controls their order in rendered SQL.

The declared CLR type supplies `DbType`, including for null arguments. Enum inputs use their underlying numeric type.
Use `DbType`, `SqlDbType`, `Size`, `Precision`, and `Scale` on `SqlParameter` or `SqlOutputParameter` to override provider metadata.
`SqlDbType` takes precedence over `DbType`. Variable-length string and binary outputs require `Size`, with `-1` for MAX types.

An input and output with the same SQL name form one input/output parameter.
Their CLR types and explicitly supplied metadata must agree.
The initial value comes from `Args`; the final value goes into `Result`. Execution does not assign output values to `Args`.
Output values are read after closing the reader.

The input attribute type is `SqlLinqer.Modeling.SqlParameterAttribute`, written as `[SqlParameter]` in declarations.
Its full name avoids a collision with the provider's `SqlParameter` class.

### Omit an argument or pass SQL NULL

Use `SqlArgument<T>` when a procedure parameter has a database default:

```csharp
var useDefault = new SearchUsers.Args { Name = SqlArgument<string>.Omitted };
var passNull = new SearchUsers.Args { Name = new SqlArgument<string>(null) };
var passValue = new SearchUsers.Args { Name = "Alice" };
```

An unassigned `SqlArgument<T>` is omitted. An ordinary nullable member always supplies its value, including SQL NULL.
Input/output parameters cannot be omitted. Their initial values must be supplied.

### Supply a table-valued parameter

```csharp
public class BatchArgs
{
    [SqlParameter("ids", TypeName = "dbo.IdList")]
    public System.Data.DataTable Ids { get; set; }
}
```

The table type must already exist. The call binds a Structured input parameter and does not create or drop SQL types.
Supply a non-null `DataTable` with the matching column types and order. Use an empty table for zero rows.

## Map results

Each public result member needs exactly one `SqlResultSet`, `SqlOutputParameter`, or `SqlReturnValue` attribute.
Use `SqlIgnore` for other members. Result members must be writable. One `Int32` member may capture the procedure's RETURN code.

Result-set indexes must be unique and contiguous from zero.
Collections may be arrays, `List<T>`, or interfaces assignable from `List<T>`, including `IReadOnlyList<T>`.
Scalar element types require exactly one result column.
The reader buffers every set. Empty sets produce empty collections; missing or extra tabular sets produce error responses.
Update counts do not consume result-set indexes.

Row models reuse `SqlColumn` and `SqlIgnore` without needing `SqlTable`, a primary key, or model inheritance.
Column matching ignores case. Every mapped row member must have exactly one matching result column; extra columns are ignored.
SQL NULL cannot populate a non-nullable value type.
Row construction uses a parameterless constructor or one public constructor whose parameters match mapped member names and types.
Members not supplied by the constructor must be writable.
Relationship members are not loaded from these flat result sets. Use a dedicated row model when the procedure returns a projection.

Row loading uses SqlLinqer mappings directly and ignores API JSON attributes, as all SQL result loading does.
Existing `BeginStoredProcedure`, `ExecuteJson`, and `ExecuteDataTable` calls retain their previous behavior.

`call.Render(connector)` returns parameterized SQL and binding metadata without opening a connection.
Diagnostic text from `GetTextWithParameterValues()` retains placeholders for output and input/output bindings.
Keep output bindings intact when executing rendered calls; the invocation's `Execute` method also performs typed result mapping.

## Validation

The regression suite covers result sets, nulls, output timing, argument isolation, and transaction ownership with fake connections.
The offline SQL Server/Azure SQL suite parses generated T-SQL and imports metadata into real SqlClient parameters without connecting.
The full container runner verifies procedure execution against SQL Server. Direct Azure execution is optional within the same suite. See [SQL Server/Azure SQL tests](../SqlLinqer.Tests/SqlServer/README.md).
