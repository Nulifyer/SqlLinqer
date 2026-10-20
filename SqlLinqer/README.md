# SQL Linqer

### What is it used for?

This library is used to create application using a type of SQL database without the need to write queries as strings and maintain a strong set of data models that represent the data. This makes it much easier to develope complex database queries without the need of the developer to perform complex operations and it easier to update the code as all the developer needs to do is update the one data model and all queries will be updated.

Set `ConnectorOptions.UseSqlModelJsonContract = true` to load SQL results independently of API JSON attributes and converters.
This opt-in mode lets one class use SqlLinqer attributes for database mappings and JSON attributes for API serialization. The default preserves existing JSON loading behavior.

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

## Azure SQL Database

Use `DbFlavor.AzureSql` with a SQL Server-compatible provider. Azure SQL uses SQL Server rendering while preserving the connector flavor. Provider setup, encrypted SQL and Microsoft Entra connection examples, token callback limitations and the optional live test suite are documented in [SqlLinqer.AzureSqlTests](https://github.com/Nulifyer/SqlLinqer/tree/master/SqlLinqer.AzureSqlTests).

For Azure SQL authentication supplied through connection properties, use `ConnectionFactoryConnector` with a factory that creates a fresh provider connection and attaches its callback or credential. The factory owns login configuration; the connector applies command timeout and disposes connections it creates. See the [Azure SQL tests and connection guide](../SqlLinqer.AzureSqlTests/README.md) for required-live verification, scoped TVP/procedure tests, and remaining live validation.

## SQLite

Use SQLite 3.38 or newer with JSON support and `DbFlavor.Sqlite`. Reference `Microsoft.Data.Sqlite` in the application; the netstandard2.0 core library has no provider dependency.

```csharp
using var example = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=example.db;Default Timeout=15");
var connector = new SqlLinqer.Connections.ReplicatorConnector(example, DbFlavor.Sqlite);
```

Create the schema before querying. A model schema names `main`, `temp`, or an attached database. Null leaves the table unqualified; explicit schemas are preserved. Attachments belong to a connection, so use a caller transaction for operations that share an attachment. For memory databases, use a named `Mode=Memory;Cache=Shared` database and retain an open keeper connection.

Typed SELECTs support root and aggregate JSON, nested objects and arrays, and LIMIT/OFFSET pagination. Writes support outputs and primary keys through RETURNING. Joined writes require a primary key; updates support target and joined-source column assignments. Caller transactions remain caller-owned. Owned writes use serializable transactions. CommandTimeout sets command lock waits; ConnectionTimeout is ignored and the provider's Default Timeout is preserved.

Store exact decimals as TEXT. DateTime.Kind does not round-trip. Direct byte[] projections use hex JSON and a SQLite-only converter. Joined updates support source-column assignments with SQLite's native arbitrary-source policy for duplicate matches. Word-boundary LIKE supports escaped patterns and the library's ASCII boundaries. Stored procedures, TVPs, TruncateQuery, and extension-only functions are unsupported. The repository's SqlLinqer.SqliteTests executable and README provide setup, coverage, and the full limitations.

## DuckDB

Use `DbFlavor.DuckDb = 4` with `DuckDB.NET.Data.Full` in the application. Typed JSON, nested relationships, paging, aggregates, returned rows and keys, joined writes, and command cancellation are supported. Joined updates reject multiple source matches for a target key. Native cancellation aborts a supplied transaction, which its caller must roll back. See the [DuckDB guide](https://github.com/Nulifyer/SqlLinqer/blob/master/docs/DUCKDB.md) for setup and provider limitations.
