[Documentation](../README.md) · [Project README](../../README.md)

# Insert rows

These examples use the [sample models](../getting-started/model-mapping.md) and a [configured connector](../getting-started/connections.md).
Import `SqlLinqer` for response states. See [response handling](../execution/responses-and-errors.md) for `State`, `Result`, and `Error` examples.

## Insert one user

`BeginInsert` creates the query. `InsertAuto` reads the columns from the object.

```csharp
var newUser = new User
{
    Username = "js354",
    Name = "John Smith",
    Email = "myemail@company.com"
};

var query = User.BeginInsert(connector).InsertAuto(newUser);
var response = query.Execute();
if (response.State != ResponseState.Valid)
    throw response.Error;

var insertedCount = response.Result;
var insertedIds = response.TouchedPrimaryKeys;
```

`Result` contains the affected-row count. `TouchedPrimaryKeys` contains the inserted IDs.

## Insert multiple users

Pass a collection to `InsertAuto`:

```csharp
var newUsers = new[]
{
    new User { Username = "ab789", Name = "Big Boss", Email = "boss@company.com" },
    new User { Username = "ml123", Name = "Morgan Lee", Email = "morgan@company.com" }
};

var batchQuery = User.BeginInsert(connector).InsertAuto(newUsers);
var batchResponse = batchQuery.Execute();
```

## Batch limits and transactions

`Execute` and `ExecuteWithOutput` split insert rows according to `ConnectorOptions.ParameterLimit`.
SQL Server and Azure SQL also limit each command to 2,100 parameters and 1,000 `VALUES` rows.
SQLite and DuckDB default-only inserts use one row per command.

All batches use one connection and transaction. SqlLinqer commits once, after all batches succeed.
A failed batch rolls back the transaction that SqlLinqer created. A rollback failure does not replace the original error.
If you supply a transaction, you must commit or roll it back. SqlLinqer leaves its connection open.

A row that exceeds the parameter budget returns an error response before connecting.
A non-positive `ParameterLimit` disables the configured budget; SQL Server limits still apply.
Each batch is a separate SQL statement, so statement-level triggers run once per batch.
Returned rows and primary keys are collected across batches. Their order within each batch follows the provider result order.
A failed operation does not return partial results.

Batching applies during execution. `Render` still renders all supplied rows as one statement for inspection.
Applications that execute rendered SQL themselves must enforce their own command limits.
`ExecuteTvp` uses its existing table-valued parameter path.

## Insert from an instance

`BeginInsertAuto` reads the instance and creates an insert with its automatic-insert columns:

```csharp
var anotherUser = new User
{
    Username = "rk456",
    Name = "Riley Kim",
    Email = "riley@company.com"
};

var instanceResponse = anotherUser
    .BeginInsertAuto(connector)
    .Execute();
```

## Use a table-valued parameter

On SQL Server, use `ExecuteTvp` as an alternative to `Execute` for a table-valued parameter insert:

```csharp
var tvpResponse = batchQuery.ExecuteTvp();
```

By default, `ExecuteTvp` reuses a persistent SQL table type for each SQL column contract.
The connector must create fresh connections to the same server and write database
when type DDL needs a separate connection.

### Reuse type definitions across requests and application nodes

Reuse is enabled automatically. Optionally select the schema for reusable types:

```csharp
var connector = new ConnectionFactoryConnector(
    () => new SqlConnection(connectionString),
    DbFlavor.SqlServer,
    new ConnectorOptions
    {
        ReusableTvpSchema = "dbo"
    });

var response = batchQuery.ExecuteTvp(connector);
```

The configured schema must already exist. When `ReusableTvpSchema` is null,
SqlLinqer uses the insert table schema, connector default schema, or `dbo`.
Configure options before sharing a connector. Set `ReuseTvpTypes = false` to
create and remove a type per execution. Idle managed types are also cleaned up automatically by default.

A reusable type has a deterministic `SqlLinqerTvp_v1_<sha256>` name. Its identity
comes from the rendered SQL column definition, including names, order, SQL types,
and decimal scale. Identical contracts from different CLR models share a type.
Different column sets or inferred decimal scales get different types. Line-ending
differences between application platforms do not change the name. Managed reuse
uses the standard mapped TVP definition. Deploy custom table-type definitions as
named types when they require different nullability, indexes, or constraints.

First use acquires a database application lock, creates the type when absent,
and validates the actual catalog definition. Creation and ownership properties
commit together before the insert transaction starts. Owned executions use the
already-open connection, so a cold start works with one pool slot. When a caller
supplies a transaction, provisioning and lease renewal use a separate connection
and require pool headroom.

Other processes use the same lock and deterministic name. Concurrent cold
requests on one connector share one provisioning operation. A connector caches
successful resolution for warm requests, so they do not repeat full catalog
validation. Managed executions still renew tracked leases and acquire an execution
guard before binding the TVP. Provisioning does not change the write connection's
NOCOUNT setting.

The provisioner needs permission to create types and add ownership properties in
the selected schema. A colliding unowned name or incompatible definition fails
before the insert. SqlLinqer never silently replaces an existing type.

Reusable definitions survive successful requests, failed writes, cancellation,
transaction rollback, and application shutdown. Parameter data is still owned and
cleaned up per request. Automatic expiration reclaims idle definitions after their leases and retention expire.

### Use an existing named type

An insert can use the same application-owned type as a stored procedure:

```csharp
var response = batchQuery
    .UseTvpType("OrderRows", "dbo")
    .ExecuteTvp(connector);
```

Pass the unqualified name first and its schema second. Without a schema, the
insert table schema, connector default, or `dbo` applies. This selection overrides
the managed reuse setting. The type must already exist. SqlLinqer validates column
names, order, and native SQL types on first use. SQL Server enforces its lengths,
nullability, numeric precision, and other constraints when binding values.
Named application types are never created, adopted, or removed by this path.
Stored-procedure inputs continue to use their declared `TypeName`.

### Clean up idle managed types online

Lease tracking and opportunistic cleanup are enabled by default. You can adjust the schema and durations:

```csharp
var options = new ConnectorOptions
{
    ReusableTvpSchema = "dbo",
    TvpLeaseDuration = TimeSpan.FromMinutes(5),
    TvpTypeRetention = TimeSpan.FromDays(1),
    TvpCleanupInterval = TimeSpan.FromHours(1)
};
```

These are the default durations. Set `AutoCleanupTvpTypes = false` to disable
automatic cleanup while retaining reuse. Lease and cleanup intervals must be positive.
Retention may be zero. Each duration supports up to 365 days and rounds up to
whole seconds. Configure options before sharing the connector.

Upgrade **all consumers of these managed types** to the lease-aware version before
enabling online cleanup. Older versions do not acquire the required execution
guards. Application-owned named types remain outside this protocol.

SqlLinqer creates one owned `_SqlLinqerTvpLeases_v1` table in each enabled registry
schema. The provisioner therefore also needs permission to create that table and
add its ownership property. Executing nodes need permission to read, insert,
update, and delete its lease rows. Cleanup needs permission to drop managed types.
A table with the same name and no ownership marker causes an error without
adopting or replacing it.

Each connector has a unique node identity. Before each managed execution, it
renews a database lease for that contract and node using database UTC time.
Renewal commits separately from the write transaction. Other lease-aware nodes
participate once the registry exists, even if they do not initiate automatic
cleanup. There is no timer or background heartbeat. Idle nodes let leases expire
and revalidate or recreate types when they next execute.

An expired lease selects a cleanup candidate only after its retention period.
Every node's retention must have elapsed. The write transaction also holds a
shared application lock through commit or rollback. Cleanup requests the same
lock exclusively with no wait, so an active transaction protects its type even
if its lease expires. Caller-owned transactions retain that guard until the caller
completes the transaction. Nodes paused before acquiring the guard recheck type
existence and retry provisioning before sending any rows. Writes are never retried.

The first managed execution on an enabled connector can check for expired types.
Later executions check at most once per configured interval for each database and
schema. A check removes at most 100 eligible types and prunes up to 1,000 orphan
lease rows. It rechecks leases, ownership, type ID, and generation while holding
the exclusive lock. Procedure dependencies prevent expiration. Opportunistic
cleanup errors appear through the query observer and do not replace a write's
result. Lease registration and execution-guard errors stop the write.

For startup or a scheduler, call cleanup explicitly and inspect its response:

```csharp
var removed = connector.CleanupExpiredTvpTypes().GetResultOrThrow();
// Or await connector.CleanupExpiredTvpTypesAsync(cancellationToken: token).
```

The optional `schema` and `databaseName` arguments select another registry.
Cleanup does not create a missing registry. Each removal commits independently,
so cancellation or a later error does not undo completed removals. Repeated calls
can drain more than 100 candidates. If no application requests arrive, cleanup
requires an explicit call or an external scheduler.

Removing a type also removes its generation's lease rows. Renewals prune expired
registrations from other nodes for the same contract. The small registry table
itself persists for future use. Untracked types without leases remain untouched.

### Retire managed types during maintenance

Inspect managed types with `GetReusableTvpTypes` or its asynchronous equivalent.
Select retired contracts using the deployment's supported-contract inventory:

```csharp
var types = connector.GetReusableTvpTypes().GetResultOrThrow();
foreach (var type in types.Where(t => retiredContractHashes.Contains(t.ContractHash)))
{
    connector.DropReusableTvpType(type).GetResultOrThrow();
}
```

This API is **maintenance-only**. Stop all consumers before dropping types, then
restart other connectors before resuming requests. This force-removal API ignores
live leases. Use `CleanupExpiredTvpTypes` for online expiration. A last-used
timestamp alone cannot establish safe retirement.

Cleanup rechecks ownership, the type ID, and a per-creation generation token.
A stale record cannot remove a replacement type, even if the numeric ID is reused.
SQL Server rejects removal of a type referenced by a stored procedure.
Already-absent types return zero. The connector performing cleanup clears its
resolution cache. Other processes must restart to discard their own caches.
Failed executions invalidate the executing connector's cache so later calls
revalidate. Writes are never automatically retried.

Use the optional `databaseName` argument to inspect a database other than the
connector default. Returned records identify that database, and cleanup targets
it. Application-owned types and per-execution types are excluded from this
inventory. This is explicit retirement cleanup, not automatic retirement detection.
