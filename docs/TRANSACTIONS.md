[README](../README.md)

# Run several writes in one transaction

Use a transaction when several changes must succeed together.
This example creates a user and grants an existing permission. If either write fails, it rolls back both changes.

The service uses the [sample models](BUILDING_A_DATAMODEL.md) and receives its connector through [constructor injection](SETUP_A_CONNECTION.md#inject-a-connector).

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

        using var connection = connector.CreateConnection();
        connection.Open();
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

Pass the same transaction to every query in the operation. Check each response before committing.
The service owns the connection and transaction, so it also handles commit, rollback, and disposal.

See [response handling](RESPONSES.md) for the difference between a successful result and an error.
