[<<< README](../README.md)

# DELETE Queries
Below is some examples of SQL DELETE queries.

### Delete a single object with primary key
```C#
var theUser = new User()
{
    Username = "js354",
    Name = "John Smith",
    Email = "myemail@company.com",
    ManagerId = "ab789",
};

// Begin a DELETE query for this primary key
var query = User
    .BeginDelete()
    .Where(x => x.Id, theCurrentUser.Id, SqlOp.EQ);

// This is the same as the above except it gets the primary key from the object
var query = theUser.BeginDeletePK()
```

### Delete with a condition
```C#
// delete users who's email ends with '@company.com' domain
var query = User
    .BeginDelete()
    .Where(x => x.Email, "%@company.com", SqlOp.LIKE)
```

### Execute the query
```C#
SqlResponse<int> response = query.Execute();
```