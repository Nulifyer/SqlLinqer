[Documentation](../README.md) · [Project README](../../README.md)

# Delete rows

These examples use the [sample models](../getting-started/model-mapping.md) and a [configured connector](../getting-started/connections.md).
Import `SqlLinqer` for operators and response states. See [response handling](../execution/responses-and-errors.md) for success and error examples.

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
var response = query.Execute();
```

If `response.State` is `ResponseState.Valid`, `response.Result` contains the affected-row count.
`response.TouchedPrimaryKeys` contains the deleted keys. Otherwise, inspect `response.Error`.

## Joined deletes in DuckDB

DuckDB joined deletes require a mapped primary key. Each target row is deleted once even if several source rows match it.
