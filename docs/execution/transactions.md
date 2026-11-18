[Documentation](../README.md) · [Project README](../../README.md)

# Transactions

Use a transaction when several changes must succeed together.
This example creates a user and grants an existing permission. If either write fails, it rolls back both changes.

The service uses the [sample models](../getting-started/model-mapping.md) and receives its connector through [constructor injection](../getting-started/connections.md#inject-a-connector).

```csharp
using SqlLinqer;
using SqlLinqer.Connections;

public class UserRegistrationService
{
    private readonly BaseConnector connector;

    public UserRegistrationService(BaseConnector connector)
    {
        this.connector = connector;
    }

    public string CreateUserWithPermission(string username, string email, int permissionId)
    {
        var user = new User { Username = username, Email = email };

        using var connection = connector.OpenConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            var userResponse = user.BeginInsertAuto(connector)
                .Execute(transaction);
            if (userResponse.State != ResponseState.Valid)
                throw userResponse.Error;

            var link = new UserPermission
            {
                UserId = user.Id,
                PermissionId = permissionId
            };

            var permissionResponse = link.BeginInsertAuto(connector)
                .Execute(transaction);
            if (permissionResponse.State != ResponseState.Valid)
                throw permissionResponse.Error;

            transaction.Commit();
            return user.Id;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
```

`OpenConnection` creates a connection and opens it. If the open fails, it disposes the connection and throws the provider's exception.
`OpenConnectionAsync(cancellationToken)` is the asynchronous form.

Pass the same transaction to every query in the operation. Check each response before committing.
The service owns the connection and transaction, so it also handles commit, rollback, and disposal.

Generated SELECT commands and page totals do not open an owned write transaction. If you pass a transaction, both reads use it and leave its lifetime to you. Without one, a page and its total are separate reads and can observe different data during concurrent writes.

Raw `ExecuteScalar` commands retain their owned transaction behavior because a scalar command can also perform writes. For a compiled count, pass the object returned by `query.Capture().RenderForCount(connector.DbFlavor)` directly to scalar execution. Copying only its SQL text into a raw command discards that execution metadata.

[Hand-written SQL](../queries/raw-sql.md#control-the-owned-transaction) from `BeginSql` follows the same rules.
Its `WithoutTransaction()` method runs a command without the owned transaction.
It can also run on one open connection that you supply, where SqlLinqer opens no transaction.

A [row lock](../queries/select.md#lock-rows-for-update) lasts until its transaction ends. Pass your transaction to a select that calls `ForUpdate`.

See [response handling](responses-and-errors.md) for the difference between a successful result and an error.
