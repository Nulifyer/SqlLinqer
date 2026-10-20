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

## Batch limits and transactions

`Execute` and `ExecuteWithOutput` split insert rows according to `ConnectorOptions.ParameterLimit`.
SQL Server and Azure SQL also limit each command to 2,100 parameters and 1,000 `VALUES` rows.
SQLite default-only inserts use one row per command.

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