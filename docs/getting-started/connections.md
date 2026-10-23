[Documentation](../README.md) · [Project README](../../README.md)

# Connections and dependency injection

Install SqlLinqer and the ADO.NET provider for your database. These examples use SQL Server:

```sh
dotnet add package SqlLinqer
dotnet add package Microsoft.Data.SqlClient
```

Create a connector with a connection factory and reuse it across queries:

```csharp
using System;
using Microsoft.Data.SqlClient;
using SqlLinqer;
using SqlLinqer.Connections;

var connectionString = "Server=<server>;Database=<database>;Integrated Security=true;Encrypt=true;TrustServerCertificate=false;Connect Timeout=10";
var options = new ConnectorOptions
{
    DefaultSchema = "dbo",
    CommandTimeout = TimeSpan.FromSeconds(120)
};

var connector = new ConnectionFactoryConnector(
    () => new SqlConnection(connectionString),
    DbFlavor.SqlServer,
    options);
```

Replace the placeholders with your connection settings. Reuse `connector` across queries.
The factory configures login timeout, authentication, pooling, and other provider settings.
SqlLinqer uses the returned connection without rewriting its connection string or provider properties.
`ConnectorOptions.CommandTimeout` controls the commands SqlLinqer executes, independently of the login timeout.

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

Install the provider package in your application. Supply a factory that creates its connection class and choose the matching flavor.

## DuckDB

Add `DuckDB.NET.Data.Full` to your application and use `DbFlavor.DuckDb`:

```csharp
using DuckDB.NET.Data;
using SqlLinqer;
using SqlLinqer.Connections;

var connector = new ConnectionFactoryConnector(
    () => new DuckDBConnection("Data Source=application.duckdb"),
    DbFlavor.DuckDb);
```

The connector calls the factory as queries execute. File databases persist across those connections.
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

## Create queries from an injected connector

Connector factories work with ordinary mapped classes; inheriting a SqlLinqer model base class is optional:

```csharp
var users = connector.BeginSelect<User>()
    .SelectRootColumns()
    .Execute()
    .GetResultOrThrow();
```

`BeginSelectAuto<T>()`, `BeginSelectBuilder<T>()`, `BeginInsert<T>()`, `BeginUpdate<T>()`, `BeginDelete<T>()`, and `BeginTruncate<T>()` also bind the connector.
Use the two-type write overloads, such as `BeginInsert<User, int>()`, to retain affected primary keys in the response.
Construction copies the connector's current default schema and opens no connection. Create a separate query builder for each operation; builders are mutable.

## Set a default connector

Queries created without an explicit connector use the current default at execution time.
Set the default once during application startup:

```csharp
SqlLinqer.Default.Connector = connector;
```

## Create connections with a factory

Return a fresh connection on every factory call. The connector opens and disposes connections it requests.
Do not return a shared connection or a connection owned by another scope.
For several operations on a caller-owned connection, supply a transaction from that connection.

Configure provider properties in the factory when connections need authentication callbacks or application-managed credentials:

```csharp
var tokenConnectionString = "Server=<server>;Database=<database>;Encrypt=true;TrustServerCertificate=false;Connect Timeout=10";
var factoryConnector = new ConnectionFactoryConnector(
    () => new SqlConnection(tokenConnectionString) { AccessTokenCallback = existingCallback },
    DbFlavor.AzureSql,
    options);
```

Here, `existingCallback` is your application's SqlClient token callback. Reuse the same callback delegate for connections that should share a pool.
Keep the factory's server and database configuration consistent across a query's operations.
SQL Server TVP inserts also request separate connections for generated table types.

Providers with a data source can supply its connection-creation method directly. For example, with Npgsql 7 or later:

```csharp
using Npgsql;

using var dataSource = NpgsqlDataSource.Create(postgresConnectionString);
var postgresConnector = new ConnectionFactoryConnector(
    dataSource.CreateConnection,
    DbFlavor.PostgreSql);
```

Keep the data source alive while the connector is in use. Its owner disposes it after all operations finish.
Configure the data source once with the provider's builder when you need additional connection settings.

## Migrate template connections

Replace `new ReplicatorConnector(template, flavor, options)` with `new ConnectionFactoryConnector(() => new ProviderConnection(connectionString), flavor, options)`.
`ReplicatorConnector` and `ConnectorOptions.ConnectionTimeout` have been removed.
Move the login timeout into the provider's connection string or builder, such as SqlClient's `Connect Timeout`.
Set any authentication properties on every connection returned by the factory.

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

## Optional registration package

`SqlLinqer.Extensions.DependencyInjection` is available on nuget.org starting with `2.3.0-rc.2`. Install the matching core and integration versions. The core library remains independent of Microsoft DI.

With that package referenced, register a default connector:

```csharp
services.AddSqlLinqer(
    () => new SqlConnection(connectionString),
    DbFlavor.SqlServer,
    options => options.CommandTimeout = TimeSpan.FromSeconds(30));
```

Resolve `BaseConnector` through constructor injection. For another database, use a key:

```csharp
services.AddKeyedSqlLinqer("reporting",
    () => new SqlConnection(reportingConnectionString), DbFlavor.SqlServer);

var reporting = serviceProvider.GetRequiredKeyedService<BaseConnector>("reporting");
```

Each registration creates one singleton connector. Repeating the default registration or a key throws instead of replacing it silently. Configuration runs once when the connector is first resolved; database connections remain lazy.

The overload accepting `IServiceProvider` supports registered singleton data sources and logger factories:

```csharp
services.AddSqlLinqer(
    sp => sp.GetRequiredService<NpgsqlDataSource>().CreateConnection(),
    DbFlavor.PostgreSql);
```

Factory and configuration delegates resolve dependencies from the root service provider. Use singleton dependencies, and keep configuration unchanged after startup. Each factory invocation must return a fresh connection. SqlLinqer disposes those connections; the application or DI container owns the data source and its disposal. Queries supplied a caller transaction use its connection and leave its lifetime to the caller.

The [minimal API sample](../../samples/SqlLinqer.MinimalApi/README.md) combines registration, response helpers, filtering, paging, and streaming against a temporary SQLite database.
