[Documentation](../README.md) · [Project README](../../README.md)

# Connections and dependency injection

Install SqlLinqer and the ADO.NET provider for your database. These examples use SQL Server:

```sh
dotnet add package SqlLinqer
dotnet add package Microsoft.Data.SqlClient
```

Create a connector from a connection string and reuse it across queries:

```csharp
using System;
using Microsoft.Data.SqlClient;
using SqlLinqer;
using SqlLinqer.Connections;

var connectionString = "Server=<server>;Database=<database>;Integrated Security=true;Encrypt=true;TrustServerCertificate=false";
using var template = new SqlConnection(connectionString);
var options = new ConnectorOptions
{
    DefaultSchema = "dbo",
    ConnectionTimeout = TimeSpan.FromSeconds(10),
    CommandTimeout = TimeSpan.FromSeconds(120)
};

var connector = new ReplicatorConnector(template, DbFlavor.SqlServer, options);
```

Replace the placeholders with your connection settings. Reuse `connector` across queries.

## Use another database

Choose the matching provider connection class and `DbFlavor`:

| Database | Provider package | Connection class | Flavor |
| --- | --- | --- | --- |
| SQL Server | `Microsoft.Data.SqlClient` | `SqlConnection` | `DbFlavor.SqlServer` |
| Azure SQL | `Microsoft.Data.SqlClient` | `SqlConnection` | `DbFlavor.AzureSql` |
| PostgreSQL | `Npgsql` | `NpgsqlConnection` | `DbFlavor.PostgreSql` |
| MySQL | `MySql.Data` | `MySqlConnection` | `DbFlavor.MySql` |
| SQLite | `Microsoft.Data.Sqlite` | `SqliteConnection` | `DbFlavor.Sqlite` |
| DuckDB | `DuckDB.NET.Data.Full` | `DuckDBConnection` | `DbFlavor.DuckDb` |

Install the provider package in your application and construct its connection with your database's connection string.
Pass that connection and the matching flavor to `ReplicatorConnector`, as in the SQL Server example.

## DuckDB

Add `DuckDB.NET.Data.Full` to your application and use `DbFlavor.DuckDb`:

```csharp
using DuckDB.NET.Data;
using SqlLinqer;
using SqlLinqer.Connections;

using var template = new DuckDBConnection("Data Source=application.duckdb");
var connector = new ReplicatorConnector(template, DbFlavor.DuckDb);
```

`ReplicatorConnector` creates connections as queries execute. File databases persist across those connections.
A plain `Data Source=:memory:` database belongs to one connection.
To query it, keep that connection open and pass a transaction from it to every query.
Use `IsolationLevel.Snapshot` when creating a DuckDB transaction. SqlLinqer uses this isolation level for transactions it creates.
The application owns the supplied transaction's commit, rollback, and disposal.

## Retain a connector on a query

Pass the connector when creating a query. The query retains it for execution, including aggregate builders and writes:

```csharp
var query = User.BeginSelect(connector).SelectRootColumns();
var response = query.Execute();
```

Pass another connector to override the retained connector for one call:

```csharp
var otherResponse = query.Execute(otherConnector);
var originalResponse = query.Execute();
```

The override controls the SQL dialect, connection, and execution options. It does not replace the retained connector.
The query keeps its model mappings and schema selected during construction. The target database must support those mappings.
`Render(otherConnector)` also uses the supplied connector's SQL dialect without changing the retained connector.

## Set a default connector

Queries created without an explicit connector use the current default at execution time.
Set the default once during application startup:

```csharp
SqlLinqer.Default.Connector = connector;
```

## Create connections with a factory

Use `ConnectionFactoryConnector` to configure each new connection in a factory:

```csharp
var factoryConnector = new ConnectionFactoryConnector(
    () => new SqlConnection(connectionString),
    DbFlavor.SqlServer,
    options);
```

Return a fresh connection on every factory call.
Use this approach when each connection needs properties such as a SqlClient `AccessTokenCallback` or application-managed credentials.
`ReplicatorConnector` copies the provider type and connection string, so it does not preserve these additional connection properties.
Set them in the factory on every new connection.
Configure login timeout in the factory's connection string. `ConnectorOptions.CommandTimeout` still applies to commands.

## Inject a connector

Register the connector with dependency injection and receive it through a constructor.
This example uses `Microsoft.Extensions.DependencyInjection`:

```sh
dotnet add package Microsoft.Extensions.DependencyInjection
```

Create a repository that accepts `BaseConnector`. It uses the [sample `User` model](model-mapping.md):

```csharp
using System.Collections.Generic;
using SqlLinqer;
using SqlLinqer.Connections;

public class UserRepository
{
    private readonly BaseConnector connector;

    public UserRepository(BaseConnector connector)
    {
        this.connector = connector;
    }

    public List<User> GetUsers()
    {
        var response = User.BeginSelect(connector)
            .SelectRootColumns()
            .Execute();

        if (response.State != ResponseState.Valid)
            throw response.Error;

        return response.Result;
    }
}
```

Register the `connector` created above as a singleton, then resolve the repository:

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<BaseConnector>(connector);
services.AddTransient<UserRepository>();

using var serviceProvider = services.BuildServiceProvider();
var repository = serviceProvider.GetRequiredService<UserRepository>();
var users = repository.GetUsers();
```

The container supplies the connector to each repository. The shared connector creates connections as queries execute.
