[<<< README](../README.md)

# SELECT Queries
Below is some examples of SQL SELECT queries.

## Typed Select Queries
```C#
// Get a specific user from the primary key
var query = User
    .BeginSelectPKAuto("js354");

// This is the same as above
var query = User
    .BeginSelect()
    .SelectAuto()
    .Where(x => x.Id, "js354", SqlOp.EQ);

// select only root columns
var query = User
    .BeginSelect()
    .SelectRootColumns()
    .OrderBy(x => x.Name, SqlDir.ASC);

// Explicitly select columns and relationships
var query = User
    .BeginSelect()
    .Top(100)
    .Distinct(false)
    .Select(x => x.Id)
    .Select(x => x.Email)
    .Select(x => x.Manger)
    .Select(x => x.Permissions)
    /**
     * The handling of the many relationship is automatically
     * handled to preserve the results and prevent
     */
    .Where(x => x.Permissions.First().name, "% admin", SqlOp.NOTLIKE)
    .OrderBy(x => x.Manger.Name, SqlDir.ASC)
    .OrderBy(x => x.Name, SqlDir.ASC);
```

### Execute the query
```C#
SqlResponse<List<User>> response = query.Execute();

// These works the same as the above however, it also gets the count of results without limits, top, etc
SqlSelectResponse<List<User>> response = query.ExecuteWithTotalCount();
```

## Select Aggregate Queries
Select Aggregate Queries are a more fre-form query that enable the developer to create soft typed queries.

```C#
public class MyResult
{
    public string Id;
    public string Name;
    public int PermissionCount;
}

var query = User
    .BeginSelectAggregate()
    .Select(x => x.Id)
    .Select(x => x.Name)
    .Select(SqlAggFunc.COUNT, x => x.Permissions, nameof(MyResult.PermissionCount))
    .OrderBy(nameof(MyResult.PermissionCount), SqlDir.DESC)
```
### Execute the query
```C#
// Execute query and get results as a list of JTokens
SqlResponse<List<JToken>> responseJToken = query.Execute();

// Execute query and deserialze results as a list of type 'MyResult'
SqlResponse<List<User>> responseTyped = query.Execute<MyResult>();

// limit results to first 10 with starting index 0
query.Limit(10, 0);

// These work the same as the two above however, they also get the count of results without limits, top, etc
SqlResponse<List<JToken>> responseTotalJtoken = query.ExecuteWithTotalCount();
SqlSelectResponse<List<User>> responseTotalTyped = query.ExecuteWithTotalCount<MyResult>();
```