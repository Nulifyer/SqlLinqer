using System.Data;
using System.Data.Common;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.Regression;

internal static partial class StoredProcedureRegressionTests
{
    private static void RunNoInput(Action<string, Action> test)
    {
        foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
        {
            test($"no-input procedures render schema and metadata without opening connections, {flavor}", () =>
            {
                var connector = new FakeConnector(new FakeDatabase(), flavor);
                connector.Options.DefaultSchema = "tenant";
                var query = new NoInputUsers().Render(connector);
                Check(query.Text.Contains("[tenant].[NoInputUsers]") && query.Parameters.Count == 2 && connector.Created == 0,
                    "Rendering lost schema, added inputs, or opened a connection");
                Check(new NoInputSchemaUsers().Render(flavor).Text.Contains("[dbo].[NoInputUsers]"), "Explicit schema was lost");
                connector.Options.DefaultSchema = "other";
                Check(new NoInputUsers().Render(connector).Text.Contains("[other].[NoInputUsers]"), "Cached definition retained a connector schema");
            });
            foreach (bool asynchronous in new[] { false, true })
            {
                test($"no-input procedures route default retained and explicit connectors, {flavor}, async={asynchronous}", () =>
                {
                    var previous = Default.Connector;
                    try
                    {
                        var database = MetadataDatabase(22L, rowsAffected: 3);
                        var connector = new FakeConnector(database, flavor);
                        var unused = new FakeConnector(new FakeDatabase(), flavor);
                        Default.Connector = connector;
                        using var cancellation = new CancellationTokenSource();
                        foreach (string route in new[] { "default", "retained", "explicit" })
                        {
                            var procedure = new NoInputUsers(route == "retained" ? connector : route == "explicit" ? unused : null);
                            if (route == "retained") Default.Connector = unused;
                            Check(procedure.Render(route == "explicit" ? connector : null).Text.Contains("[NoInputUsers]"), "Render routing failed");
                            var timeout = TimeSpan.FromSeconds(7);
                            var response = route == "explicit"
                                ? asynchronous ? procedure.ExecuteAsync(connector, cancellationToken: cancellation.Token, commandTimeout: timeout).GetAwaiter().GetResult()
                                    : procedure.Execute(connector, commandTimeout: timeout)
                                : asynchronous ? procedure.ExecuteAsync(cancellationToken: cancellation.Token, commandTimeout: timeout).GetAwaiter().GetResult()
                                    : procedure.Execute(commandTimeout: timeout);
                            Check(response.State == ResponseState.Valid && response.Result.Single().Name == "雪"
                                && response.TotalResults == 22 && response.ReturnCode == 17 && response.RowsAffected == 3, response.Error?.ToString());
                            var command = database.Executed.Last();
                            Check(command.CommandTimeout == 7 && command.Parameters.Count == 2
                                && command.Parameters.Cast<DbParameter>().All(p => p.Direction == ParameterDirection.Output),
                                "No-input execution added inputs or lost timeout");
                        }
                        Check(unused.Created == 0 && database.Executed.Count == 3 && database.Connections.All(c => c.IsDisposed),
                            "Execution used the wrong connector or leaked owned connections");
                        if (asynchronous) Check(database.LastOpenToken == cancellation.Token && database.LastCommandToken == cancellation.Token,
                            "No-input execution lost cancellation propagation");
                    }
                    finally { Default.Connector = previous; }
                });
                test($"no-input procedures preserve caller transactions and clear failed output, {flavor}, async={asynchronous}", () =>
                {
                    foreach (bool retained in new[] { false, true }) foreach (bool fail in new[] { false, true })
                    {
                        var database = fail ? new FakeDatabase { ReaderFactory = _ => Users(nullId: true).CreateDataReader() } : MetadataDatabase(22L);
                        var connector = new FakeConnector(database, flavor);
                        using var connection = new FakeConnection(database); connection.Open();
                        using var transaction = (FakeTransaction)connection.BeginTransaction();
                        var procedure = new NoInputUsers(retained ? connector : null);
                        var response = retained
                            ? asynchronous ? procedure.ExecuteAsync(transaction).GetAwaiter().GetResult() : procedure.Execute(transaction)
                            : asynchronous ? procedure.ExecuteAsync(connector, transaction).GetAwaiter().GetResult() : procedure.Execute(connector, transaction);
                        Check(response.State == (fail ? ResponseState.Error : ResponseState.Valid), response.Error?.ToString());
                        if (fail) Check(response.Result == null && response.TotalResults == null && response.ReturnCode == null && response.RowsAffected == null,
                            "Failure retained rows or metadata");
                        Check(database.Executed.Single().UsedTransaction == transaction && connector.Created == 0
                            && !connection.IsDisposed && !transaction.IsDisposed && transaction.Commits == 0 && transaction.Rollbacks == 0,
                            "No-input execution changed caller transaction ownership");
                    }
                });
            }
        }
        test("no-input scalar procedures execute without an input object", () =>
        {
            var database = new FakeDatabase
            {
                ReaderFactory = command =>
                {
                    var rows = new DataTable(); rows.Columns.Add("Value", typeof(int)); rows.Rows.Add(42);
                    return new ClosingReader(rows.CreateDataReader(), () => SetReturnCode(command, 0), -1);
                }
            };
            var connector = new FakeConnector(database, DbFlavor.SqlServer);
            var previous = Default.Connector;
            try
            {
                Default.Connector = connector;
                var procedure = new NoInputScalar();
                Check(procedure.Execute().GetResultOrThrow().Single() == 42, "Parameterless Execute failed");
                Check(procedure.ExecuteAsync().GetAwaiter().GetResult().GetResultOrThrow().Single() == 42, "Parameterless ExecuteAsync failed");
                Check(procedure.Render().Parameters.Count == 2, "Parameterless Render added inputs");
            }
            finally { Default.Connector = previous; }
        });
        test("no-input async procedures preserve cancellation token and roll back owned work", () =>
        {
            foreach (bool beforeStart in new[] { false, true })
            {
                var database = MetadataDatabase(22L);
                var connector = new FakeConnector(database, DbFlavor.SqlServer);
                using var cancellation = new CancellationTokenSource();
                if (beforeStart) cancellation.Cancel();
                else database.BeforeAsyncRead = _ => { cancellation.Cancel(); return Task.CompletedTask; };
                try
                {
                    new NoInputUsers(connector).ExecuteAsync(cancellationToken: cancellation.Token).GetAwaiter().GetResult();
                    throw new Exception("Expected cancellation");
                }
                catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token) { }
                if (beforeStart) Check(connector.Created == 0, "Pre-cancellation opened a connection");
                else Check(database.Connections.Single().IsDisposed && database.Connections.Single().Transactions.Single().Rollbacks == 1,
                    "Cancellation leaked owned resources");
            }
        });
        test("no-input procedures reject invalid mappings and unsupported providers before opening connections", () =>
        {
            Throws<InvalidOperationException>(() => new MissingNoInputMapping());
            Throws<InvalidOperationException>(() => new EmptyNoInputMapping());
            Throws<InvalidOperationException>(() => new EmptyNoInputSchema());
            foreach (var flavor in new[] { DbFlavor.PostgreSql, DbFlavor.MySql, DbFlavor.Sqlite, DbFlavor.DuckDb })
            {
                var connector = new FakeConnector(new FakeDatabase(), flavor);
                var procedure = new NoInputUsers(connector);
                Throws<NotSupportedException>(() => procedure.Render(flavor));
                Throws<NotSupportedException>(() => procedure.Execute());
                Throws<NotSupportedException>(() => procedure.ExecuteAsync().GetAwaiter().GetResult());
                Check(connector.Created == 0, "Unsupported provider opened a connection");
            }
        });
    }

    [SqlProcedure("NoInputUsers")]
    private sealed class NoInputUsers(BaseConnector connector = null) : SqlLinqerCallable<UserRow>(connector) { }
    [SqlProcedure("NoInputUsers", Schema = "dbo")]
    private sealed class NoInputSchemaUsers : SqlLinqerCallable<UserRow> { }
    [SqlProcedure("NoInputScalar")]
    private sealed class NoInputScalar : SqlLinqerCallable<int> { }
    private sealed class MissingNoInputMapping : SqlLinqerCallable<UserRow> { }
    [SqlProcedure("")]
    private sealed class EmptyNoInputMapping : SqlLinqerCallable<UserRow> { }
    [SqlProcedure("Invalid", Schema = " ")]
    private sealed class EmptyNoInputSchema : SqlLinqerCallable<UserRow> { }
}
