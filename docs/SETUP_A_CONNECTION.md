[README](../README.md)

# Set up a connection

These examples use SQL Server. Add its provider to your application:

```sh
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

## Set a default connector

Set the default once during application startup to use parameterless `Execute()` calls:

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

## Inject a connector

Register the connector with dependency injection and receive it through a constructor.
This example uses `Microsoft.Extensions.DependencyInjection`:

```sh
dotnet add package Microsoft.Extensions.DependencyInjection
```

Create a repository that accepts `BaseConnector`. It uses the [sample `User` model](BUILDING_A_DATAMODEL.md):

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
            .Execute(connector);

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
