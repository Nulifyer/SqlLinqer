[README](../README.md)

# Build a data model

The query examples use these models for users, permissions, and their linking table.
Map models to existing tables with `SqlTable`. Use `SqlColumn` when a column name differs from its C# member name.

Use `SqlLinqerPrimaryKeyObject<T, TKey>` for a model with a primary key.
Use `SqlLinqerObject<T>` for a model without one.

## Share a class between the database and API

Enable the SQL model contract on the connector before running queries:

```csharp
var options = new ConnectorOptions { UseSqlModelJsonContract = true };
var connector = new ConnectionFactoryConnector(createConnection, dbFlavor, options);
```

Import `SqlLinqer.Connections`. `createConnection` must return a new provider connection on each call.

With this option enabled, SQL loading follows mapped C# member names and SqlLinqer attributes.
It ignores API JSON attributes, including `JsonIgnore`, `JsonPropertyName`, `JsonConverter`, and `JsonConstructor`.
Application serializer options do not affect database loading.

The option defaults to `false` in the current major version. Existing applications continue to use JSON attributes for loading.
Enable it explicitly when sharing a model between SQL and an API. Constructors and converters used only for API loading do not run in this mode.

```csharp
using System.Text.Json.Serialization;
using SqlLinqer.Modeling;

[SqlTable("Users")]
public class ApiUser : SqlLinqerPrimaryKeyObject<ApiUser, int>
{
    [SqlPrimaryKey]
    public int Id { get; set; }

    [SqlColumn("user_name")]
    [JsonPropertyName("displayName")]
    public string Name { get; set; }

    [JsonIgnore]
    public string InternalNote { get; set; }

    [SqlIgnore]
    public string ApiOnly { get; set; }
}
```

SQL loads `Name` from `user_name` and can load `InternalNote`. Normal API serialization writes `displayName` and omits `InternalNote`.
`ApiOnly` is excluded from SQL mappings but remains available to the API serializer.
Use `SqlIgnore` to exclude a database member. Use `SqlAutoSelectExclude` to omit it from automatic selection.

With the SQL model contract enabled, loading uses a public or private parameterless constructor when one exists.
Otherwise, a class needs one public constructor whose parameter names and types match its mapped members.
This rule supports anonymous projections and does not use `JsonConstructor` to select a constructor.
Private API members do not become database members through `JsonInclude`.

## Permission

This model has a database-generated integer key:

```csharp
using SqlLinqer.Modeling;

[SqlTable("Permissions", schema: "dbo")]
public class Permission : SqlLinqerPrimaryKeyObject<Permission, int>
{
    [SqlPrimaryKey(dbGenerated: true)]
    [SqlColumn("id")]
    public int? Id;

    [SqlColumn("name")]
    public string Name;

    public Permission() { }

    public Permission(string name)
    {
        Name = name;
    }
}
```

## User

This model generates its own ID and defines relationships to other models.
The relationships use `SqlAutoSelectExclude` so queries can select them explicitly.

```csharp
using System;
using System.Collections.Generic;
using SqlLinqer.Modeling;

[SqlTable("Users")]
public class User : SqlLinqerPrimaryKeyObject<User, string>
{
    [SqlPrimaryKey(dbGenerated: false)]
    [SqlColumn("id")]
    public string Id { get; set; }

    [SqlColumn("username")]
    public string Username { get; set; }

    [SqlColumn("name")]
    public string Name { get; set; }

    [SqlColumn("email")]
    public string Email { get; set; }

    [SqlColumn("manager")]
    public string ManagerId { get; set; }

    [SqlOneToOne(nameof(ManagerId)), SqlAutoSelectExclude]
    public User Manager;

    [SqlOneToMany(nameof(UserPermission.UserId)), SqlAutoSelectExclude]
    public List<UserPermission> UserPermissions;

    [SqlManyToMany(typeof(UserPermission), nameof(UserPermission.UserId), nameof(UserPermission.PermissionId)), SqlAutoSelectExclude]
    public IEnumerable<Permission> Permissions;

    public User()
    {
        Id = Guid.NewGuid().ToString();
    }
}
```

`ManagerId` references another user's `Id`. `UserPermissions` loads linking records. `Permissions` loads permission models through those records.

If you do not need a linking model, replace the `Permissions` attribute with this mapping:

```csharp
[SqlManyToMany("UserPermissions", "user_id", "permission_id"), SqlAutoSelectExclude]
public IEnumerable<Permission> Permissions;
```

The string arguments name the database table and columns. The model-based mapping uses C# member names through `nameof`.

## UserPermission

This model represents a user-permission link. Use its parameterless constructor when loading records or its other constructor when creating a link.

```csharp
using System;
using SqlLinqer.Modeling;

[SqlTable("UserPermissions")]
public class UserPermission : SqlLinqerObject<UserPermission>
{
    [SqlColumn("user_id")]
    public string UserId;

    [SqlColumn("permission_id")]
    public int PermissionId;

    [SqlOneToOne(nameof(UserId)), SqlAutoSelectExclude]
    public User User;

    [SqlOneToOne(nameof(PermissionId)), SqlAutoSelectExclude]
    public Permission Permission;

    public UserPermission() { }

    public UserPermission(User user, Permission permission)
    {
        if (user == null)
            throw new ArgumentNullException(nameof(user));
        if (permission == null)
            throw new ArgumentNullException(nameof(permission));
        if (!permission.Id.HasValue)
            throw new ArgumentException("Permission must have an ID.", nameof(permission));

        User = user;
        UserId = user.Id;
        Permission = permission;
        PermissionId = permission.Id.Value;
    }
}
```

## Control automatic operations

`SelectAuto`, `InsertAuto`, and `UpdateAuto` use the model's automatic-operation attributes.
Use an `Exclude` attribute to omit a member. Use an include attribute to select a relationship automatically.

For example, change these members on `User` to omit `ManagerId` from automatic selects and include `Permissions`:

```csharp
[SqlColumn("manager"), SqlAutoSelectExclude]
public string ManagerId { get; set; }

[SqlManyToMany(typeof(UserPermission), nameof(UserPermission.UserId), nameof(UserPermission.PermissionId)), SqlAutoSelect]
public IEnumerable<Permission> Permissions;
```
