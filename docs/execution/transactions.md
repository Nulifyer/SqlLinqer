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

Without a transaction of yours, every insert, update, and delete runs in a transaction that SqlLinqer opens, commits on success, and rolls back on failure. This holds on every database and for every terminal, including the ones that return rows.

Generated SELECT commands and page totals do not open an owned write transaction. If you pass a transaction, both reads use it and leave its lifetime to you. Without one, a page and its total are separate reads and can observe different data during concurrent writes.

Raw `ExecuteScalar` commands retain their owned transaction behavior because a scalar command can also perform writes. For a compiled count, pass the object returned by `query.Capture().RenderForCount(connector.DbFlavor)` directly to scalar execution. Copying only its SQL text into a raw command discards that execution metadata.

[Hand-written SQL](../queries/raw-sql.md#control-the-owned-transaction) from `BeginSql` follows the same rules.
Its `WithoutTransaction()` method runs a command without the owned transaction.

A [row lock](../queries/select.md#lock-rows-for-update) lasts until its transaction ends. Pass your transaction to a select that calls `ForUpdate` or `ForShare`.

## Keep several queries on one connection

Some state belongs to a database session and not to a transaction.
Examples are a PostgreSQL advisory lock, a temporary table, and a session setting.
`connector.OnConnection(connection)` returns a `SessionConnector` that runs every command on one open connection:

```csharp
using var connection = await connector.OpenConnectionAsync(cancellationToken);
var session = connector.OnConnection(connection);

await session.BeginSql("SELECT pg_advisory_lock(@key)", new { key })
    .WithoutTransaction()
    .ExecuteAsync(cancellationToken: cancellationToken);

var pending = (await session.BeginSelect<TranslationBatch>()
    .SelectRootColumns()
    .Where(x => x.State, "queued", SqlOp.EQ)
    .ExecuteAsync(cancellationToken: cancellationToken))
    .GetResultOrThrow();

await session.BeginUpdate<TranslationBatch>()
    .Update(x => x.State, "running")
    .Where(x => x.State, "queued", SqlOp.EQ)
    .ExecuteAsync(cancellationToken: cancellationToken);
```

The session connector follows the connector it came from, with these differences:

- Every builder, stored-procedure call, stream, and `GetServerTime` call runs on the connection you supplied.
- SqlLinqer never opens, closes, or disposes that connection. You dispose it.
- If the connection is not open, each command returns an error response.

A write still opens its own transaction on the connection and commits or rolls it back.
For that reason, the connection must not have an active transaction of yours. If it has one, pass that transaction to each terminal.
A connection runs one command at a time. Do not use the session connector from several tasks at once, and finish a stream before you run the next command.

The session connector shares the options and the command hooks of the connector it came from. Its `Source` and `Connection` properties return both parts.
Do not register it as a shared or default connector. Create it where you open the connection, and let it go with the connection.
SQL Server table type management for `ExecuteTvp` still uses separate connections.

This is the only way to run SqlLinqer commands on a connection of yours without a transaction of yours.
A command that must not run in any transaction uses [`WithoutTransaction()`](../queries/raw-sql.md#control-the-owned-transaction) on `BeginSql`.

See [response handling](responses-and-errors.md) for the difference between a successful result and an error.
