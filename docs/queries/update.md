[Documentation](../README.md) · [Project README](../../README.md)

# Update rows

These examples use the [sample models](../getting-started/model-mapping.md) and a [configured connector](../getting-started/connections.md).
Import `SqlLinqer` for operators and response states. See [response handling](../execution/responses-and-errors.md) for success and error examples.

## Update a user by primary key

Set the ID of the user to update:

```csharp
var currentUser = new User
{
    Id = "<existing-user-id>",
    Email = "newEmail@company.com"
};

var query = User.BeginUpdate(connector)
    .UpdateNonDefaults(currentUser)
    .Where(x => x.Id, currentUser.Id, SqlOp.EQ);
```

The instance method creates the same assignments and primary-key filter:

```csharp
var instanceQuery = currentUser.BeginUpdateNonDefaults(connector);
```

`UpdateNonDefaults` copies non-null values from the object. Use `UpdateAuto` to apply the model's automatic-update settings instead.
Value-type defaults such as `0` and `false` are included. `UpdateNonDefaults` does not treat them as omitted values.

## Set a column to null

Use an explicit assignment to clear a nullable column:

```csharp
var clearManagerQuery = User.BeginUpdate(connector)
    .Update(x => x.ManagerId, (string)null)
    .Where(x => x.Id, currentUser.Id, SqlOp.EQ);
```

To include null for `ManagerId` whenever `UpdateNonDefaults` runs, add this attribute to the existing model member:

```csharp
[SqlColumn("manager"), SqlUpdateDefaultAlways]
public string ManagerId { get; set; }
```

## Update rows that match a filter

Add a filter before executing an update. A query without a filter updates every row in the table.
`SqlOp.LIKE` [ignores case](select.md#add-optional-search-filters) on every database, so a pattern can match more rows than a case-sensitive match would.

```csharp
var domainQuery = User.BeginUpdate(connector)
    .Update(x => x.Email, "noreply@company.com")
    .Where(x => x.Email, "%@company.com", SqlOp.LIKE);
```

## Apply a numeric operation

Use `BasicMathOperation` to change a numeric column. For example, define a counter model:

```csharp
using SqlLinqer.Modeling;

[SqlTable("UsageCounters")]
public class UsageCounter : SqlLinqerPrimaryKeyObject<UsageCounter, int>
{
    [SqlPrimaryKey]
    public int Id { get; set; }

    public int Count { get; set; }
}
```

```csharp
var counterQuery = UsageCounter.BeginUpdate(connector)
    .Update(x => x.Count, new SqlLinqer.Components.Update.BasicMathOperation(
        SqlLinqer.Components.Update.BasicMathOperation.MathOp.Add, 1))
    .Where(x => x.Id, 1, SqlOp.EQ);
```

## Set a column to the server time

Use `ServerTimeOperation` when the database clock, not the application clock, must supply a timestamp:

```csharp
var touchQuery = User.BeginUpdate(connector)
    .Update(x => x.UpdatedUtc, new SqlLinqer.Components.Update.ServerTimeOperation())
    .Where(x => x.Id, currentUser.Id, SqlOp.EQ);
```

| Database | Assigned expression | Result |
| --- | --- | --- |
| SQL Server, Azure SQL | `SYSUTCDATETIME()` | The current UTC time. |
| MySQL | `UTC_TIMESTAMP(6)` | The current UTC time. Use a `DATETIME` column. |
| SQLite | `strftime('%Y-%m-%d %H:%M:%f', 'now')` | The current UTC time, in the text format that Microsoft.Data.Sqlite writes for a bound `DateTime`. A `DateTimeOffset` member also receives `+00:00`. Server-assigned and application-assigned times therefore sort and compare together. |
| PostgreSQL | `CURRENT_TIMESTAMP` | The exact instant in a `timestamptz` column. A plain `timestamp` column stores the session time zone's local time. |
| DuckDB | `now()` | The same as PostgreSQL. |

The operation also works in an [insert conflict update](insert.md#update-a-row-that-already-exists).
On PostgreSQL and DuckDB the assigned time is the time the transaction started, so every statement in one transaction assigns the same value.

To read the server time, call `GetServerTime` or `GetServerTimeAsync` on the connector:

```csharp
var asOfUtc = (await connector.GetServerTimeAsync(transaction, cancellationToken)).GetResultOrThrow();
```

The result is a `DateTimeOffset` with a zero offset on every database. The read opens no transaction of its own.
Inside a PostgreSQL transaction, the value is the time the transaction started.

## Keep an existing value

Use `SetIfNullOperation` to assign a value only when the column is NULL. A column that already has a value keeps it:

```csharp
var startQuery = connector.BeginUpdate<TranslationBatch>()
    .Update(x => x.State, "running")
    .Update(x => x.StartedUtc, new SqlLinqer.Components.Update.SetIfNullOperation(now))
    .Where(x => x.Id, batchId, SqlOp.EQ);
```

It renders `started_utc = COALESCE(started_utc, @value)` on every database.
The operation also works in an [insert conflict update](insert.md#update-a-row-that-already-exists).

## Define a custom operation

Inherit from `UpdateOperation` to define an assignment. `Render` returns the whole assignment, such as `column = expression`.
Its `statement` argument gives you the two ways to name the column:

- `statement.RenderTarget(flavor)` renders the left side of the assignment.
- `statement.UpdateColumn.Render(flavor)` renders the column's existing value for the right side.

Use each in its place. PostgreSQL, SQLite, and DuckDB reject a table-qualified column on the left side, and `RenderTarget` renders the form each database accepts.

This operation converts the existing value to upper case on every database:

```csharp
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Update;
using SqlLinqer.Queries.Core;

public class UpperCase : UpdateOperation
{
    public override string Render(
        UpdateQuery query,
        UpdateStatement statement,
        ParameterCollection parameters,
        DbFlavor flavor)
    {
        var target = statement.RenderTarget(flavor);
        var current = statement.UpdateColumn.Render(flavor);
        return $"{target} = UPPER({current})";
    }
}
```

Bind application values with `parameters.AddParameter(value)` and place the returned placeholder in the expression.
Check `flavor` and throw `NotSupportedException` when the expression uses a function that only some databases provide.
This SQL Server operation replaces an email domain:

```csharp
public class ReplaceEmailDomain : UpdateOperation
{
    private readonly string newDomain;

    public ReplaceEmailDomain(string newDomain)
    {
        this.newDomain = newDomain;
    }

    public override string Render(
        UpdateQuery query,
        UpdateStatement statement,
        ParameterCollection parameters,
        DbFlavor flavor)
    {
        if (flavor != DbFlavor.SqlServer && flavor != DbFlavor.AzureSql)
            throw new NotSupportedException("This operation requires SQL Server or Azure SQL.");

        var target = statement.RenderTarget(flavor);
        var current = statement.UpdateColumn.Render(flavor);
        var placeholder = parameters.AddParameter(newDomain);
        return $"{target} = CONCAT(SUBSTRING({current}, 1, CHARINDEX('@', {current}) - 1), {placeholder})";
    }
}
```

Import `System` for `NotSupportedException`. Apply the operation to emails that have the old domain:

```csharp
var replaceDomainQuery = User.BeginUpdate(connector)
    .Update(x => x.Email, new ReplaceEmailDomain("@newcompany.com"))
    .Where(x => x.Email, "%@company.com", SqlOp.LIKE);
```

## Execute an update

```csharp
var response = query.Execute();
```

If `response.State` is `ResponseState.Valid`, `response.Result` contains the affected-row count. Otherwise, inspect `response.Error`.

To read the changed rows, select columns with `Output` and call `ExecuteWithOutput<TRow>()`:

```csharp
var changed = User.BeginUpdate(connector)
    .Update(x => x.Email, "noreply@company.com")
    .Where(x => x.Email, "%@company.com", SqlOp.LIKE)
    .Output(x => x.Id)
    .ExecuteWithOutput<string>()
    .GetResultOrThrow();
```

`TRow` is a scalar type for one output column, or a class mapped by column name.
Every mapped member of that class must be one of the output columns. MySQL does not return changed rows.

## Joined updates in DuckDB

DuckDB joined updates require a mapped primary key and at most one source match per target row.
If several source rows match, the statement fails instead of choosing one arbitrarily.
Constrain the relationship or its filters to produce one source match per target key.
