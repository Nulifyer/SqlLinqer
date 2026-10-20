[README](../README.md)

# Change relationships

Change a relationship by updating its foreign key or inserting or deleting a linking row.
These methods use the [sample models](BUILDING_A_DATAMODEL.md). Add them to the injected `UserRepository` from the [connector guide](SETUP_A_CONNECTION.md#inject-a-connector).
Import `SqlLinqer` for operators and response states.

## Assign a manager

Update the user's `ManagerId` with an existing manager's ID. Pass null to remove the manager:

```csharp
public int AssignManager(string userId, string managerId)
{
    var response = User.BeginUpdate(connector)
        .Update(x => x.ManagerId, managerId)
        .Where(x => x.Id, userId, SqlOp.EQ)
        .Execute(connector);

    if (response.State != ResponseState.Valid)
        throw response.Error;

    return response.Result;
}
```

## Grant a permission

Insert a linking row using an existing user's ID and permission ID:

```csharp
public int GrantPermission(string userId, int permissionId)
{
    var link = new UserPermission
    {
        UserId = userId,
        PermissionId = permissionId
    };

    var response = link.BeginInsertAuto(connector).Execute(connector);
    if (response.State != ResponseState.Valid)
        throw response.Error;

    return response.Result;
}
```

## Remove a permission

Filter by both IDs to delete the specific link:

```csharp
public int RevokePermission(string userId, int permissionId)
{
    var response = UserPermission.BeginDelete(connector)
        .Where(x => x.UserId, userId, SqlOp.EQ)
        .Where(x => x.PermissionId, permissionId, SqlOp.EQ)
        .Execute(connector);

    if (response.State != ResponseState.Valid)
        throw response.Error;

    return response.Result;
}
```

Each method returns the affected-row count. A successful operation can affect zero rows.
To combine changes into one operation, use a [transaction](TRANSACTIONS.md).
