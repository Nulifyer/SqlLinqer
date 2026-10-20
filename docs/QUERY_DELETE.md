[README](../README.md)

# Delete rows

These examples use the [sample models](BUILDING_A_DATAMODEL.md) and a [configured connector](SETUP_A_CONNECTION.md).
Import `SqlLinqer` for operators and response states. See [response handling](RESPONSES.md) for success and error examples.

## Delete one user by primary key

Set the ID of the user to delete:

```csharp
var user = new User { Id = "<existing-user-id>" };

var query = User.BeginDelete(connector)
    .Where(x => x.Id, user.Id, SqlOp.EQ);
```

The instance method uses the primary key from the object:

```csharp
var instanceQuery = user.BeginDeleteAuto(connector);
```

Both queries target the same row. Execute one of them.

## Delete rows that match a filter

Add a filter before executing a delete. A query without a filter deletes every row in the table.

```csharp
var domainQuery = User.BeginDelete(connector)
    .Where(x => x.Email, "%@company.com", SqlOp.LIKE);
```

## Execute a delete

```csharp
var response = query.Execute(connector);
```

If `response.State` is `ResponseState.Valid`, `response.Result` contains the affected-row count.
`response.TouchedPrimaryKeys` contains the deleted keys. Otherwise, inspect `response.Error`.
