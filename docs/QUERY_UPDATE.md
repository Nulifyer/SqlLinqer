[<<< README](../README.md)

# UPDATE Queries
Below is some examples of SQL UPDATE queries.

### Update a single instance of an object
```C#
var theCurrentUser = new User()
{
    Username = "js354",
    Name = "John Smith",
    Email = "myemail@company.com",
    ManagerId = "ab789",
};

theCurrentUser.Email = "newEmail@company.com";
theCurrentUser.ManagerId = null;

// Begin a DELETE query for this primary key
var query = User
    .BeginUpdate()
    .UpdateNonDefaults(theCurrentUser)
    .Where(x => x.Id, theCurrentUser.Id, SqlOp.EQ);

/**
 * Update the record with the current object's primary key
 * only with the columns that don't have default values.
 * This is the same as above.
 */
var query = theCurrentUser.BeginUpdatePKNonDefaults();
```

Columns can be defined to always update when doing a 'Update non-defaults'
```C#
public class User : SqlLinqerPrimaryKeyObject<User, string>
{
    /**
     * This column will always be updated when running a 'Update non-defaults' query 
     * even if the value is NULL
     */    
    [SqlUpdateDefaultAlways]
    public string ManagerId { get; set; }
}
```

### Update a with a condition
```C#
var query = User
    .BeginUpdate()
    .Update(x => x.Email, "noreply@company.com")
    .Where(x => x.Email, "%@company.com", SqlOp.LIKE);
```

### Update a with a operation
```C#
var query = User.BeginUpdate();
query
    .Update(x => x.Email, query.NewSimpleMathOperation(SqlMathOperation.ADD, 1))
    .Where(x => x.Email, "%@company.com", SqlOp.LIKE);
```

Custom operations are possible
```C#
using SqlLinqer.Components.Render;
using SqlLinqer.Extensions.EnumExtensions;

public class ReplaceEmailDomain : IUpdateOperation
{        
    public string NewDomain;
    
    public ReplaceEmailDomain(string newDomain)
    {
        NewDomain = newDomain;
    }
    
    public virtual string Render(IUpdateStatement statement, ParameterCollection existingCollection, DbFlavor flavor)
    {
        string placeholder = existingCollection.AddParameter(Value);
        return $"CONCAT(SUBSTRING({statement.GetTarget(flavor)}, 1, CHARINDEX('@', {statement.GetTarget(flavor)}) - 1), {placeholder})";
    }
}

var query = User.BeginUpdate();
query
    .Update(x => x.Email, new ReplaceEmailDomain("@newcompany.com"))
    .Where(x => x.Email, "%@company.com", SqlOp.LIKE);
```

### Execute the query
```C#
SqlResponse<int> response = query.Execute();
```