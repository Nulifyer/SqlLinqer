[README](../README.md)

# Select rows

These examples use the [sample models](BUILDING_A_DATAMODEL.md) and a [configured connector](SETUP_A_CONNECTION.md).
Import `System`, `System.Linq`, and `SqlLinqer` for the examples below. See [response handling](RESPONSES.md) for success and error examples.

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

## Select root columns

`SelectRootColumns` selects the user's columns without loading relationships:

```csharp
var rootQuery = User.BeginSelect(connector)
    .SelectRootColumns()
    .OrderBy(x => x.Name, SqlDir.ASC);
```

## Select relationships

Select relationship members to load related models. See [relationship updates](QUERY_RELATIONSHIPS.md) to change their links.
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

## Execute a typed select

`Execute` returns a list of the model type. Check the response state before reading the result:

```csharp
var response = rootQuery.Execute(connector);
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

var pageResponse = pagedQuery.ExecuteWithTotalCount(connector);
if (pageResponse.State != ResponseState.Valid)
    throw pageResponse.Error;

var page = pageResponse.Result;
var total = pageResponse.TotalResults;
```

Pages start at `1`. `Limit(limit, offset)` uses a zero-based offset.

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

var aggregateResponse = aggregateQuery.Execute(connector);
```

`Execute` returns the type defined in `BuildQuery`. Use `new { ... }` for an anonymous result type.

Apply a limit before requesting the total count:

```csharp
aggregateQuery.Limit(10, 0);
var aggregatePage = aggregateQuery.ExecuteWithTotalCount(connector);
```

## Add optional search filters

Add the following methods to the injected `UserRepository` from the [connector guide](SETUP_A_CONNECTION.md#inject-a-connector).
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
        .Execute(connector);

    if (response.State != ResponseState.Valid)
        throw response.Error;

    return response.Result;
}
```

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

    var response = query.Execute(connector);
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
        .Execute(connector);

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
    var response = User.BeginPrimaryKeyExists(userId, connector).Execute(connector);
    if (response.State != ResponseState.Valid)
        throw response.Error;

    return response.Result;
}
```

A successful `false` result means the ID was not found. A failed query has `State` set to `ResponseState.Error`.
