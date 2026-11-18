[Documentation](../README.md) · [Project README](../../README.md)

# Selects, filtering, paging, and aggregates

These examples use the [sample models](../getting-started/model-mapping.md) and a [configured connector](../getting-started/connections.md).
Import `System`, `System.Linq`, and `SqlLinqer` for the examples below. See [response handling](../execution/responses-and-errors.md) for success and error examples.

## Select by primary key

Pass an existing user's `Id` to select one user:

```csharp
var userId = "<existing-user-id>";
var byId = User.BeginSelectAuto(userId, connector);
```

The following query applies the same selection and filter:

```csharp
var explicitById = User.BeginSelect(connector)
    .SelectAuto()
    .Where(x => x.Id, userId, SqlOp.EQ);
```

## Select distinct parents

Call `.Distinct()` to remove duplicate parent rows before paging and loading nested data.
The database compares selected scalar values and values required to correlate selected relationships.
For example, `.Select(x => x.Name).Select(x => x.Permissions).Distinct()` keeps separate users when their relationship IDs differ, even if their names match.
Repeated permissions remain in each collection. Parent DISTINCT does not apply recursively.
With DISTINCT, order by a selected scalar column or a required relationship key.
There is currently no typed fluent operation to configure DISTINCT on one selected child collection.
For a flat `.Select(x => x.Name).Distinct()` projection, only the selected name participates.

## Select root columns

`SelectRootColumns` selects the user's columns without loading relationships:

```csharp
var rootQuery = User.BeginSelect(connector)
    .SelectRootColumns()
    .OrderBy(x => x.Name, SqlDir.ASC);
```

## Select relationships

Select relationship members to load related models. See [relationship updates](relationship-changes.md) to change their links.
SqlLinqer maps native provider values into complete roots and their selected nested members. Server providers and DuckDB use one graph command; SQLite uses prepared local child commands on the same connection.
A missing object becomes `null`; a collection with no matches becomes an empty collection.
Selecting relationships does not execute an additional query for each returned model.
Use `First()` to reference a column through a collection relationship:

```csharp
var relatedQuery = User.BeginSelect(connector)
    .Top(100)
    .Select(x => x.Id)
    .Select(x => x.Email)
    .Select(x => x.Manager)
    .Select(x => x.Permissions)
    .Where(x => x.Permissions.First().Name, "% admin", SqlOp.NOTLIKE)
    .OrderBy(x => x.Manager.Name, SqlDir.ASC)
    .ThenBy(x => x.Name, SqlDir.ASC);
```

See [sync and async execution](../execution/sync-and-async.md) for asynchronous methods and cancellation.
See [SQL inspection and query reuse](sql-and-query-reuse.md) to inspect generated SQL or keep a query definition for reuse.

## Execute a typed select

`Execute` returns a list of the model type. Check the response state before reading the result:

```csharp
var response = rootQuery.Execute();
if (response.State == ResponseState.Valid)
{
    foreach (var user in response.Result)
        Console.WriteLine(user.Name);
}
else
{
    throw response.Error;
}
```

Use `ExecuteWithTotalCount` to get the results and the count before paging or limits:

```csharp
var pagedQuery = User.BeginSelect(connector)
    .SelectRootColumns()
    .OrderBy(x => x.Id, SqlDir.ASC)
    .Page(1, 10);

var pageResponse = pagedQuery.ExecuteWithTotalCount();
if (pageResponse.State != ResponseState.Valid)
    throw pageResponse.Error;

var page = pageResponse.Result;
var total = pageResponse.TotalResults;
```

Pages start at `1`. `Limit(limit, offset)` uses a zero-based offset.
The page and total use two commands. The total counts root matches before paging, including when the requested page is empty.
Add a unique final sort field for stable paging when other sort values tie.

## Build an aggregate projection

Use `BuildQuery` to define a result model. This example counts each user's permissions:

```csharp
public class MyResult
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int PermissionCount { get; set; }
}
```

```csharp
var aggregateQuery = User.BeginSelectBuilder(connector)
    .BuildQuery(x => new MyResult
    {
        Id = x.Select(p => p.Id),
        Name = x.Select(p => p.Name),
        PermissionCount = x.Count(p => p.Permissions.First().Id)
    })
    .OrderBy(x => x.PermissionCount, SqlDir.DESC)
    .ThenBy(x => x.Id, SqlDir.ASC);

var aggregateResponse = aggregateQuery.Execute();
```

`Execute` returns the type defined in `BuildQuery`. Use `new { ... }` for an anonymous result type.
SqlLinqer groups the non-aggregate selections automatically when you add aggregates such as `Count`.
In this example, the selected user ID and name identify each group.
Sort a projection by its selected scalar members. Add a unique final sort field, such as `Id`, to keep paging stable when values tie.


Apply a limit before requesting the total count:

```csharp
aggregateQuery.Limit(10, 0);
var aggregatePage = aggregateQuery.ExecuteWithTotalCount();
```

## Count or sum only some rows in each group

Pass a filter to `Count`, `CountBig`, or `Sum` to aggregate only the rows that match it.
This report counts all views for each document and, separately, the views that came from a QR code:

```csharp
var report = connector.BeginSelectBuilder<ReaderEvent>()
    .BuildQuery(q => new
    {
        DocumentId = q.Select(e => e.DocumentId),
        Views = q.Count(),
        QrViews = q.Count(filter => filter.Where(e => e.Source, "qr", SqlOp.EQ)),
        QrBytes = q.Sum(e => e.Bytes, filter => filter.Where(e => e.Source, "qr", SqlOp.EQ))
    })
    .Where(e => e.Kind, "view", SqlOp.EQ)
    .OrderBy(row => row.DocumentId, SqlDir.ASC);
```

The filter uses the same `Where` methods as a query. Chain several calls to require all of them.
The query's own `Where` selects the rows in the report. An aggregate's filter narrows only that aggregate.

`Count`, `CountBig`, `Sum`, `Avg`, `Min`, and `Max` accept a filter as their last argument.
A group without matching rows returns `0` for a filtered count and for a filtered sum.
A filtered `Avg`, `Min`, or `Max` returns NULL for that group. Cast the call to a nullable type to receive it:

```csharp
LargestQr = (int?)q.Max(e => e.Bytes, filter => filter.Where(e => e.Source, "qr", SqlOp.EQ))
```

PostgreSQL, SQLite, and DuckDB render `FILTER (WHERE ...)`. SQL Server, Azure SQL, and MySQL render `CASE WHEN` inside the aggregate.
The results are the same.

## Aggregate independent collections

`CountRelated` and `SumRelated` are available starting with `SqlLinqer` `2.3.0-rc.2`. For an application's mapped `WorkOrder.Tasks` and `WorkOrder.Notes` collections:

```csharp
var readiness = connector.BeginSelectBuilder<WorkOrder>()
    .BuildQuery(q => new
    {
        Id = q.Select(order => order.Id),
        Tasks = q.CountRelated(order => order.Tasks),
        Notes = q.CountRelated(order => order.Notes),
        CompletedTasks = q.SumRelated(order => order.Tasks, task => task.Completed)
    })
    .OrderBy(row => row.Id, SqlDir.ASC);
```

Each aggregate uses its own correlated subquery and preserves the mapped join conditions. Two tasks and three notes produce counts of two and three. `SumRelated` sums the child values once per parent, and both helpers return zero for empty collections. Counts return `long`; choose a sum projection type compatible with the database's arithmetic and your data range. The helpers support direct mapped one-to-many collections reached through single-object relationships. They reject earlier collections, many-to-many links, and LINQ operators on the collection expression. They are per-parent values, not totals across a grouped report.

Ordinary `Count` and `Sum` retain SQL join semantics. Aggregating two joined collections can produce a Cartesian product: two tasks and three notes create six rows. Distinct child IDs can correct counts, but `SUM(DISTINCT value)` can discard legitimate repeated amounts. Use independent collection aggregates when that is the intended row meaning. Dynamic adapters can construct `SelectRelatedAggregate` from public relationship metadata and optional child predicates.

## Add optional search filters

Add the following methods to the injected `UserRepository` from the [connector guide](../getting-started/connections.md#inject-a-connector).
Import `System.Collections.Generic` for their return types.

Build the query first, then add filters for supplied values. Pass null for a filter you do not need:

```csharp
public List<User> SearchUsers(
    string name,
    string email,
    string managerId,
    int page = 1,
    int pageSize = 20)
{
    var query = User.BeginSelect(connector).SelectRootColumns();

    if (!string.IsNullOrWhiteSpace(name))
        query.Where(x => x.Name, $"%{name}%", SqlOp.LIKE);

    if (!string.IsNullOrWhiteSpace(email))
        query.Where(x => x.Email, $"%{email}%", SqlOp.LIKE);

    if (!string.IsNullOrWhiteSpace(managerId))
        query.Where(x => x.ManagerId, managerId, SqlOp.EQ);

    var response = query
        .OrderBy(x => x.Id, SqlDir.ASC)
        .Page(page, pageSize)
        .Execute();

    if (response.State != ResponseState.Valid)
        throw response.Error;

    return response.Result;
}
```

`SqlOp.LIKE` and `SqlOp.NOTLIKE` ignore case on every database:

| Database | SQL | Case handling |
| --- | --- | --- |
| PostgreSQL, DuckDB | `ILIKE`, `NOT ILIKE` | Always ignores case. |
| SQL Server, Azure SQL, MySQL | `LIKE`, `NOT LIKE` | Ignores case under the default collation. A column with a case-sensitive collation still matches case-sensitively. |
| SQLite | `LIKE`, `NOT LIKE` | Ignores case for ASCII letters only. |

The whole-word operators, such as `SqlOp.LIKEWORD`, follow the same case handling.
For a case-sensitive pattern match, use [hand-written SQL](raw-sql.md).

## Group conditions

Use `NewWhere` to group name and email matches with OR. The manager filter applies to the entire group.
This expresses `(name matches OR email matches) AND manager matches`:

```csharp
public List<User> FindManagedUsers(string searchText, string managerId)
{
    var query = User.BeginSelect(connector).SelectRootColumns();
    var pattern = $"%{searchText}%";
    var matches = query.NewWhere(SqlWhereOp.OR)
        .Where(x => x.Name, pattern, SqlOp.LIKE)
        .Where(x => x.Email, pattern, SqlOp.LIKE);

    query.Where(matches)
        .Where(x => x.ManagerId, managerId, SqlOp.EQ);

    var response = query.Execute();
    if (response.State != ResponseState.Valid)
        throw response.Error;

    return response.Result;
}
```

## Filter by several IDs

Use `SqlArrayOp.IN` to match any ID in a collection:

```csharp
public List<User> GetUsersByIds(string[] userIds)
{
    if (userIds.Length == 0)
        return new List<User>();

    var response = User.BeginSelect(connector)
        .SelectRootColumns()
        .Where(x => x.Id, userIds, SqlArrayOp.IN)
        .Execute();

    if (response.State != ResponseState.Valid)
        throw response.Error;

    return response.Result;
}
```

## Check whether a user exists

Use `BeginPrimaryKeyExists` to check an ID without loading a model:

```csharp
public bool UserExists(string userId)
{
    var response = User.BeginPrimaryKeyExists(userId, connector).Execute();
    if (response.State != ResponseState.Valid)
        throw response.Error;

    return response.Result;
}
```

A successful `false` result means the ID was not found. A failed query has `State` set to `ResponseState.Error`.

## Check whether any row matches a filter

Use `ExecuteExists` on a filtered select to ask whether at least one row matches:

```csharp
var leaseIsCurrent = (await connector.BeginSelect<DocumentLease>()
    .Where(x => x.DocumentId, id, SqlOp.EQ)
    .Where(x => x.Generation, generation, SqlOp.EQ)
    .Where(x => x.TokenHash, token, SqlOp.EQ)
    .ExecuteExistsAsync(transaction, cancellationToken))
    .GetResultOrThrow();
```

The check uses the query's filters, including filters through relationships.
Selected fields, ordering, paging, and row locks do not affect the answer.
The check loads no rows and opens no transaction of its own. Pass a transaction to see that transaction's uncommitted changes.
`query.Capture().RenderForExists(connector.DbFlavor)` returns the SQL for inspection.

## Lock rows for update

Call `ForUpdate` to lock the selected rows until your transaction ends.
Pass the transaction when you execute. Without one, the database releases the lock as soon as the read completes.

```csharp
var next = connector.BeginSelect<TranslationBatch>()
    .SelectRootColumns()
    .Where(x => x.State, "queued", SqlOp.EQ)
    .OrderBy(x => x.CreatedUtc, SqlDir.ASC)
    .Top(1)
    .ForUpdate(SqlRowLock.SKIPLOCKED)
    .Execute(transaction)
    .GetResultOrThrow();
```

| `SqlRowLock` | Behavior when another transaction holds a selected row |
| --- | --- |
| `WAIT` (default) | Waits for the other transaction to finish. |
| `SKIPLOCKED` | Leaves the locked row out of the result. |
| `NOWAIT` | Returns an error response immediately. |

| Database | SQL |
| --- | --- |
| PostgreSQL, MySQL | `FOR UPDATE`, `FOR UPDATE SKIP LOCKED`, `FOR UPDATE NOWAIT` |
| SQL Server, Azure SQL | `WITH (UPDLOCK, ROWLOCK)` table hints, with `READPAST` or `NOWAIT` |
| SQLite, DuckDB | Not supported. Rendering throws `NotSupportedException`. |

The lock applies to rows of the root table. When the query joins other tables, PostgreSQL and MySQL render `FOR UPDATE OF` the root table.
A locking select cannot select relationships or use `Distinct`. Load related rows with a separate query.

### Claim the next queued row

On PostgreSQL, SQL Server, and Azure SQL, one update can claim the next row and return its key.
Build the locking select as a subquery and filter the update with it:

```csharp
var next = new SelectQuery<TranslationBatch>()
    .Select(x => x.Id)
    .Where(x => x.State, "queued", SqlOp.EQ)
    .OrderBy(x => x.CreatedUtc, SqlDir.ASC)
    .Top(1)
    .ForUpdate(SqlRowLock.SKIPLOCKED);

var claimed = connector.BeginUpdate<TranslationBatch, Guid>()
    .Update(x => x.State, "running")
    .Update(x => x.ClaimToken, claimToken)
    .Where(x => x.Id, next, SqlArrayOp.IN)
    .Execute(transaction);

var claimedId = claimed.TouchedPrimaryKeys.SingleOrDefault();
```

Two workers that run this at the same time claim different rows. A worker that finds no free row updates nothing.
Import `SqlLinqer.Queries.Typed` for `SelectQuery<T>`.

MySQL cannot update a table that the same statement selects from in a subquery.
On MySQL, run the locking select and then update the returned key, both in one transaction.
