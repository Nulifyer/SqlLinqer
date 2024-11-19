# SQL Linqer

### What is it used for?

This library is used to create application using a type of SQL database without the need to write queries as strings and maintain a strong set of data models that represent the data. This makes it much easier to develope complex database queries without the need of the developer to perform complex operations and it easier to update the code as all the developer needs to do is update the one data model and all queries will be updated.

### Why not just use Microsoft's Enity Framework (EF)?

This is more of a personal preference. They way this library behaves is difference than EF and might not have all of the functionality that EF does. EF can sometimes be cumbersome to handle as it forces a certain developer style on the stucture of the data and I find this style of use to be easier. This includes modification, extending the functionality, and allowing for more complex queries.

## How to

See source repo for working examples.

### Build a data model:

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

    [SqlColumn("email")]
    public string Email { get; set; }

    public User()
    {
        Id = Guid.NewGuid().ToString();
    }
}
```

### Create/Insert Queries
```C#
var newUsers = new[] 
{
    new User()
    {
        Username = "ab789",
        Name = "Big Boss",
        Email = "boss@company.com",
        ManagerId = null,
    },
    new User()
    {
        Username = "js354",
        Name = "John Smith",
        Email = "myemail@company.com",
        ManagerId = "ab789",
    },
};

var query = User
    .BeginInsertAuto()      // Creates a new query
    .InsertAuto(newUsers)   // Adds the objects to insert
    .Execute(connector);
```

### Read/Select Queries
```C#
// select only root columns
var query = User
    .BeginSelect()
    .SelectRootColumns()
    .OrderBy(x => x.Name, SqlDir.ASC)
    .Execute(connector);
```

### Update Queries
```C#
var query = User
    .BeginUpdate()
    .Update(x => x.Email, "noreply@company.com")
    .Where(x => x.Email, "%@company.com", SqlOp.LIKE)
    .Execute();
```

### Delete Queries
```C#
var query = User
    .BeginDelete()
    .Where(x => x.Email, "%@company.com", SqlOp.LIKE)
    .Execute(connector);
```