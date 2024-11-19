[<<< README](../README.md)

# INSERT Queries
Below is some examples of SQL INSERT queries.

### Insert single object
```C#
var newUser = new User()
{
    Username = "js354",
    Name = "John Smith",
    Email = "myemail@company.com",
    ManagerId = "ab789",
};

var query = User
    .BeginInsertPK()        // Creates a new query
    .InsertAuto(newUser)    // Adds the object to insert
```

### Insert multiple objects
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
    .BeginInsertPK()        // Creates a new query
    .InsertAuto(newUsers)   // Adds the objects to insert
```

### Execute the query
For objects with primary keys, a primary key insert can be created. This will return the new primary keys along with the rest of the result.
```C#
// Execute insert in batches
SqlPrimaryKeysResponse<int, string> response = query.Execute();

// Execute insert as TVP
SqlPrimaryKeysResponse<int, string> responseTvp = query.ExecuteTVP();
```

### Create query form an instance
```C#
var newUser = new User()
{
    Username = "js354",
    Name = "John Smith",
    Email = "myemail@company.com",
    ManagerId = "ab789",
};

var response = newUser
    .BeginInsertPKAuto()
    .Execute();
```