using System.Data;
using System.Data.Common;
using DuckDB.NET.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;
using SqlLinqer.Connections;

namespace SqlLinqer.Tests.Regression;

internal static class ConnectionFactoryRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (var flavor in Enum.GetValues<DbFlavor>())
        {
            foreach (int timeout in new[] { 0, 91 })
            {
                test($"factory retains provider connection configuration, {flavor}, timeout={timeout}", () =>
                {
                    using var configured = Create(flavor, timeout);
                    string settings = configured.ConnectionString;
                    var connector = new ConnectionFactoryConnector(() => configured, flavor,
                        new ConnectorOptions { CommandTimeout = TimeSpan.FromSeconds(2) });
                    Check(ReferenceEquals(connector.CreateConnection(), configured), "Factory connection was replaced");
                    Check(configured.State == ConnectionState.Closed && configured.ConnectionString == settings,
                        "Factory connection was opened or its settings changed");
                    if (flavor is DbFlavor.SqlServer or DbFlavor.AzureSql or DbFlavor.PostgreSql or DbFlavor.MySql)
                        Check(configured.ConnectionTimeout == timeout, "Provider login timeout changed");
                    using var command = configured.CreateCommand();
                    if (flavor != DbFlavor.DuckDb)
                        Check(command.CommandTimeout == timeout, "Provider command default changed during connection creation");
                });
            }
        }
        test("factory uses a shared provider data source without replacing its configuration", () =>
        {
            using var source = NpgsqlDataSource.Create("Host=example;Database=example;Username=example;Timeout=91;Command Timeout=99;Pooling=false");
            var connector = new ConnectionFactoryConnector(source.CreateConnection, DbFlavor.PostgreSql);
            using var first = connector.CreateConnection();
            using var second = connector.CreateConnection();
            Check(!ReferenceEquals(first, second) && first.State == ConnectionState.Closed && second.State == ConnectionState.Closed,
                "Data source did not supply separate closed connections");
            Check(first.ConnectionTimeout == 91 && first.ConnectionString == second.ConnectionString,
                "Data source connection settings changed");
            using var command = first.CreateCommand();
            Check(command.CommandTimeout == 99, "Data source command default changed during connection creation");
        });
        foreach (string mode in new[] { "sync", "async", "stream" })
        {
            test($"factory owns fresh connections and respects caller transactions, {mode}", () => CheckExecution(mode).GetAwaiter().GetResult());
            foreach (string stage in new[] { "open", "execute" })
            {
                test($"factory connections are disposed after {stage} failure, {mode}", () =>
                {
                    var database = new FakeDatabase { JsonRows = ["{\"Id\":7}"], FailAt = stage };
                    var connector = new ConnectionFactoryConnector(() => new FakeConnection(database), DbFlavor.PostgreSql);
                    try { Read(connector, mode).GetAwaiter().GetResult(); }
                    catch (Exception error)
                    {
                        var cause = error;
                        while (cause.InnerException != null) cause = cause.InnerException;
                        Check(cause is InvalidOperationException && cause.Message == stage, "Execution failed for an unexpected reason");
                        Check(database.Connections.Count == 1 && database.Connections[0].IsDisposed,
                            "Failed execution retained an owned connection");
                        return;
                    }
                    throw new Exception("Fixture did not fail");
                });
            }
        }
    }

    private static async Task CheckExecution(string mode)
    {
        var database = new FakeDatabase { JsonRows = ["{\"Id\":7}"] };
        var connector = new ConnectionFactoryConnector(() => new FakeConnection(database) { ConnectionString = "factory-owned" },
            DbFlavor.PostgreSql, new ConnectorOptions { CommandTimeout = TimeSpan.FromSeconds(4) });
        for (int i = 0; i < 2; i++)
        {
            Check((await Read(connector, mode)).Single().Id == 7, "Factory execution returned the wrong result");
            Check(database.Connections.Count == i + 1 && database.Connections[i].IsDisposed
                && database.Connections[i].ConnectionString == "factory-owned", "Owned connection settings or lifetime changed");
        }
        Check(database.Executed.All(x => x.CommandTimeout == 4), "Command execution timeout stopped applying");
        using var caller = new FakeConnection(database);
        caller.Open();
        using var transaction = (FakeTransaction)caller.BeginTransaction();
        Check((await Read(connector, mode, transaction)).Single().Id == 7, "Caller transaction execution failed");
        Check(database.Connections.Count == 3 && !caller.IsDisposed && caller.State == ConnectionState.Open
            && transaction.Commits == 0 && transaction.Rollbacks == 0 && !transaction.IsDisposed,
            "Caller transaction invoked the factory or lost ownership");
    }

    private static async Task<List<FactoryRow>> Read(BaseConnector connector, string mode, DbTransaction transaction = null)
    {
        var query = new SqlLinqer.Components.Render.RenderedQuery("SELECT factory_test");
        if (mode == "stream")
        {
            var rows = new List<FactoryRow>();
            await foreach (var row in connector.ExecuteReaderJsonAsyncStream<FactoryRow>(query, transaction)) rows.Add(row);
            return rows;
        }
        var response = mode == "sync" ? connector.ExecuteReaderJson<FactoryRow>(query, transaction)
            : await connector.ExecuteReaderJsonAsync<FactoryRow>(query, transaction);
        if (response.State != ResponseState.Valid) throw response.Error;
        return response.Result;
    }

    private static DbConnection Create(DbFlavor flavor, int timeout)
    {
        const string password = "sample;=\"' value ";
        return flavor switch
        {
            DbFlavor.SqlServer or DbFlavor.AzureSql => new SqlConnection(new SqlConnectionStringBuilder
            {
                DataSource = "example",
                InitialCatalog = "example",
                UserID = "example",
                Password = password,
                ConnectTimeout = timeout,
                CommandTimeout = timeout,
                Pooling = false,
                Encrypt = SqlConnectionEncryptOption.Mandatory,
                TrustServerCertificate = false
            }.ConnectionString),
            DbFlavor.PostgreSql => new NpgsqlConnection(new NpgsqlConnectionStringBuilder
            {
                Host = "example",
                Database = "example",
                Username = "example",
                Password = password,
                Timeout = timeout,
                CommandTimeout = timeout,
                Pooling = false,
                KeepAlive = 17
            }.ConnectionString),
            DbFlavor.MySql => new MySqlConnection(new MySqlConnectionStringBuilder
            {
                Server = "example",
                Database = "example",
                UserID = "example",
                Password = password,
                ConnectionTimeout = (uint)timeout,
                DefaultCommandTimeout = (uint)timeout,
                Pooling = false
            }.ConnectionString),
            DbFlavor.Sqlite => new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = ":memory:", DefaultTimeout = timeout, Pooling = false }.ConnectionString),
            DbFlavor.DuckDb => new DuckDBConnection("Data Source=:memory:"),
            _ => throw new ArgumentOutOfRangeException(nameof(flavor))
        };
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class FactoryRow { public int Id { get; set; } }
}
