[Documentation](../README.md) · [Project README](../../README.md)

# Query responses and errors

These examples use the [sample models](../getting-started/model-mapping.md) and a [configured connector](../getting-started/connections.md).
Import `System` and `SqlLinqer` for the examples below.

## Check state before reading the result

`State` tells you whether the query succeeded. `Result` contains its returned data.

- `ResponseState.Valid` means the query succeeded. Read `Result`. `Error` is null.
- `ResponseState.Error` means the query failed. Read `Error`. `Result` is reset to its type's default, such as null or `0`.

Use `State` to decide whether a query succeeded. An empty list or an affected-row count of `0` can be a successful result.

## Read selected rows

A select returns a list in `Result`. An empty list means no rows matched:

```csharp
var userId = "<existing-user-id>";
var response = User.BeginSelect(connector)
    .SelectRootColumns()
    .Where(x => x.Id, userId, SqlOp.EQ)
    .Execute(connector);

if (response.State == ResponseState.Error)
{
    Console.WriteLine(response.Error.Message);
}
else if (response.Result.Count == 0)
{
    Console.WriteLine("No user matched the ID.");
}
else
{
    foreach (var user in response.Result)
        Console.WriteLine(user.Name);
}
```

## Read the affected-row count

A write returns its affected-row count in `Result`. A successful update can affect zero rows:

```csharp
var updateResponse = User.BeginUpdate(connector)
    .Update(x => x.Email, "newEmail@company.com")
    .Where(x => x.Id, userId, SqlOp.EQ)
    .Execute(connector);

if (updateResponse.State != ResponseState.Valid)
    throw updateResponse.Error;

var affectedRows = updateResponse.Result;
Console.WriteLine($"Updated {affectedRows} rows.");
```

A failed write can also have `0` in `Result`, because `0` is the default integer value.
Check `State` before treating the count as a query result.

## Read returned primary keys

Primary-key writes also return the affected IDs in `TouchedPrimaryKeys`:

```csharp
var newUser = new User { Username = "js354", Email = "john@company.com" };
var insertResponse = newUser.BeginInsertAuto(connector).Execute(connector);
if (insertResponse.State != ResponseState.Valid)
    throw insertResponse.Error;

var insertedCount = insertResponse.Result;
var insertedIds = insertResponse.TouchedPrimaryKeys;
foreach (var id in insertedIds)
    Console.WriteLine($"Inserted user {id}.");
```

## Read a page and its total count

`ExecuteWithTotalCount` puts the selected page in `Result` and the count before paging in `TotalResults`:

```csharp
var pageResponse = User.BeginSelect(connector)
    .SelectRootColumns()
    .OrderBy(x => x.Id, SqlDir.ASC)
    .Page(1, 10)
    .ExecuteWithTotalCount(connector);

if (pageResponse.State != ResponseState.Valid)
    throw pageResponse.Error;

var users = pageResponse.Result;
var total = pageResponse.TotalResults;
Console.WriteLine($"Showing {users.Count} of {total} users.");
```

## Read stored-procedure rows and metadata

`StoredProcedureQuery<TInput, TOutput>.Execute()` returns `SqlStoredProcedureResponse<TOutput>`.
`Result` contains the output rows, as it does for a select.
`TotalResults` contains the optional unpaged count from a configured output parameter.
`ReturnCode` contains SQL RETURN, and `RowsAffected` contains the provider's affected-row count when available.
Unknown counts are `null`. A known zero is `0`.
A nonzero return code does not automatically indicate execution failure. Check `State` first.
Failure clears the rows and all metadata.

See [stored procedures](../queries/stored-procedures.md) for input and output model definitions and execution examples.
