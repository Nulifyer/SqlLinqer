[<<< README](../README.md)

# Setup a connection 
The 'ReplicatorConnector' works as a connection factory, creating new connections based on the one you pass in.
```C#
using SqlLinqer.Connections;

// This is to pass to the replicator
var tempConn = new System.Data.SqlClient.SqlConnection("Data Source=fake_host;Initial Catalog=fake_database;User Id=fake_user;Password=fake_password;");

// Setup some options
var options = new ConnectorOptions()
{
    DefaultSchema = "dbo",
    ConnectionTimeout = TimeSpan.FromSeconds(10)
    CommandTimeout = TimeSpan.FromSeconds(120)
};

// Create the connector, this should be a static reference and should be reused by multiple queries
var connector = new ReplicatorConnector(tempConn, DbFlavor.SqlServer, options);
```

## Set the default connection for all queries
This saves time defining the connection everywhere and enables easier updating should the connection need to be changed.
```C#
SqlLinqer.Default.Connector = new ReplicatorConnector(tempConn, DbFlavor.SqlServer, options);
```

## Create your own connector
This example would be used for SQL Server user impersonation login
```C#
public class SqlServerImpersonationConnector : BaseConnector
{
    protected readonly string Server;
    protected readonly string Database;
    public SqlServerImpersonationConnector(string server, string database) : base(DbFlavor.SqlServer, new ConnectorOptions() {
        DefaultSchema = "dbo",
    })
    {
        Server = server;
        Database = database;
    }

    public override DbConnection CreateConnection()
    {
        var settings = new[]
        {
            ("Data Source", Server),
            ("Initial Catalog", Database),
            ("Integrated Security", "true"),
            ("Connect Timeout", this.Options.CommandTimeout.TotalSeconds.ToString()),
        };
        string connStr = string.Join(";", settings.Select(x => $"{x.Item1}={x.Item2}"));
        return new System.Data.SqlClient.SqlConnection(connStr);
    }

    protected override SqlResponse<T> ExecuteCommand<T>(RenderedQuery query, Func<SqlResponse<T>> action)
    {
        using (/* Impersonate */)
        {
            return base.ExecuteCommand(query, action);
        }
    }
}
```