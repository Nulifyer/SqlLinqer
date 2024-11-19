[<<< README](../README.md)

# Building a Data Model
Below is an example set of data models. 

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