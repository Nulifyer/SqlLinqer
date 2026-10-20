[<<< README](../README.md)

# Building a Data Model
Below is an example set of data models. 

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

### Permission
An object to represent permissions granted to a user

```C#
using SqlLinqer.Modeling;

/**
 * the table and schema can be defined on the model. 
 * If the schema is not defined it will be inherited from the controller
 */
[SqlTable("Permissions", schema: "dbo")]    
// the 'SqlLinqerPrimaryKeyObject' should be inherited by models with a primary key.
public class Permission : SqlLinqerPrimaryKeyObject<Permission, int>
{
    // Columns can be defined as fields or properties.
    [SqlPrimaryKey(dbGenerated: true)]
    public int? id;

    [SqlColumn("name")]
    public string name;

    public Permission(string name)
    {
        this.name = name;
    }
}
```
### User
An object to represent a user

```C#
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

    /**
     * By default the name of the member is used.
     * You can change the database name with the 'SqlColumn' attribute.
     */
    [SqlColumn("manager")]
    public string ManagerId { get; set; }

    /**
     * Here are some different ways to define the relationships.
     */

    // As a one-to-one related model
    [SqlOneToOne(nameof(User.ManagerId))]
    public User Manger;

    // As a collection of the relational class    
    [SqlOneToMany(nameof(UserPermission.UserId))]
    public List<UserPermission> UserPermissions;

    // As a collection of the related class

    // with a relational model
    [SqlManyToMany(typeof(UserPermission), nameof(UserPermission.UserId), nameof(UserPermission.PermissionId))]
    public IEnumerable<Permission> Permissions;

    // without a relational model
    [SqlManyToMany("UserPermissions", "user_id", "permission_id")]
    public IEnumerable<Permission> Permissions2;

    public User()
    {
        Id = Guid.NewGuid().ToString();     // Create the ID when the object is created
    }
}
```

### UserPermission
An object to represent the relationship between Users and Permissions

```C#
using SqlLinqer.Modeling;

[SqlTable("UserPermissions")]
// the 'SqlLinqerObject' should be inherited by models with no primary key.
public class UserPermission : SqlLinqerObject<UserPermission>
{
    [SqlColumn("user_id")]
    public string UserId;

    [SqlColumn("permission_id")]
    public int PermissionId;

    [SqlOneToOne(nameof(UserId))]
    public User User;

    [SqlOneToOne(nameof(PermissionId))]
    public Permission Permission;

    public UserPermission(User user, Permission permission)
    {
        User = user;
        UserId = User.Id;

        Permission = permission;
        if (Permission.id == null)
            throw new ArgumentNullException("Permission has no ID");

        PermissionId = Permission.id.Value;
    }
}
```

## Auto query settings
By default auto settings (Auto select, insert, and update) use all the root columns. However, this can be explicitly defined. Relationships are opted out by default from auto select.

If an exclude is placed at the class level then the columns and relationships need to be opted in explicitly. The same is true for the reverse.

This allows the developer to create expected loading conditions for certain data models. Like a user object always loading the Manager and Permission from the database when auto selected, or controlling which columns get auto updated in an UPDATE query.

These are the various auto setting attributes. They can be placed on a data model class or one of the fields or properties.
```C#
[SqlAutoInsert]
[SqlAutoInsertExclude]

[SqlAutoSelect]
[SqlAutoSelectExclude]

[SqlAutoUpdate]
[SqlAutoUpdateExclude]
```

In this example all columns on the user will be auto selected except for the Manager ID and the Manager object will be loaded
```C#
public class User : SqlLinqerPrimaryKeyObject<User, string>
{
    [SqlAutoSelectExclude]
    public string ManagerId { get; set; }

    [SqlAutoSelect]
    public User Manger;
}
```