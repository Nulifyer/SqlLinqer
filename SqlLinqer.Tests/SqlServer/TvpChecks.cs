using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;
using static SqlLinqer.Tests.SqlServer.LiveSuite;

namespace SqlLinqer.Tests.SqlServer;

internal static class TvpChecks
{
    internal static void RunLive(BaseConnector connector, SqlConnection connection, string connectionString, string schema,
        Action<string, Action> live)
    {
        string typeCountSql = $"SELECT COUNT(*) FROM sys.table_types WHERE schema_id = SCHEMA_ID(N'{schema}')";
        string rowCountSql = $"SELECT COUNT(*) FROM [{schema}].[Rows]";
        int TypeCount() => Scalar(connection, typeCountSql);
        int RowCount() => Scalar(connection, rowCountSql);
        InsertQuery<ScopedRow> Insert(decimal amount) => new InsertQuery<ScopedRow>(schema).InsertAuto(new ScopedRow { Name = "async TVP 雪", Amount = amount });

        live("async TVP identity keys output values and committed type cleanup", () =>
        {
            int types = TypeCount(), rows = RowCount();
            var result = new InsertWithPrimaryKeyQuery<ScopedRow, int>(schema).InsertAuto(new[]
            {
                new ScopedRow { Name = "async 雪", Amount = 1.125m, Optional = null },
                new ScopedRow { Name = null, Amount = 2.25m, Optional = 19 },
            }).ExecuteTvpAsync(connector).GetAwaiter().GetResult();
            Require(Valid(result) == 2 && result.TouchedPrimaryKeys.Distinct().Count() == 2, "Async TVP keys differ");
            using var output = Valid(Insert(3.125m).Output(x => x.Id).Output(x => x.Name).Output(x => x.Amount)
                .ExecuteTvpWithOutputAsync(connector).GetAwaiter().GetResult());
            Require(output.Rows.Count == 1 && (string)output.Rows[0]["Name"] == "async TVP 雪"
                && (decimal)output.Rows[0]["Amount"] == 3.125m, "Async TVP output values differ");
            Require(TypeCount() == types && RowCount() == rows + 3, "Async owned TVP leaked a type or lost rows");
        });
        live("async TVP caller transaction rolls back rows without restoring generated types", () =>
        {
            int types = TypeCount(), rows = RowCount();
            using (var tx = connection.BeginTransaction())
            {
                Require(Valid(Insert(4).ExecuteTvpAsync(connector, tx).GetAwaiter().GetResult()) == 1, "Async caller insert failed");
                using var output = Valid(Insert(5).Output(x => x.Id).ExecuteTvpWithOutputAsync(connector, tx).GetAwaiter().GetResult());
                Require(output.Rows.Count == 1 && tx.Connection == connection && Scalar(connection, rowCountSql, tx) == rows + 2,
                    "Async TVP lost caller ownership");
                var failed = Insert(-1).ExecuteTvpAsync(connector, tx).GetAwaiter().GetResult();
                Require(failed.State == ResponseState.Error && tx.Connection == connection, "Async caller failure lost ownership");
                Require(Scalar(connection, typeCountSql, tx) == types, "Async caller operation leaked a generated type");
                tx.Rollback();
            }
            Require(TypeCount() == types && RowCount() == rows, "Caller rollback restored a type or retained inserted rows");
        });
        foreach (bool asynchronous in new[] { false, true })
        {
            live($"TVP type DDL follows the caller database when the factory defaults elsewhere, async={asynchronous}", () =>
            {
                int types = TypeCount(), rows = RowCount();
                var settings = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
                var otherDefault = new ConnectionFactoryConnector(() => new SqlConnection(settings.ConnectionString), connector.DbFlavor,
                    new ConnectorOptions { DefaultSchema = schema, CommandTimeout = TimeSpan.FromSeconds(30) });
                using (var tx = connection.BeginTransaction())
                {
                    var result = asynchronous ? Insert(5).ExecuteTvpAsync(otherDefault, tx).GetAwaiter().GetResult()
                        : Insert(5).ExecuteTvp(otherDefault, tx);
                    Require(Valid(result) == 1 && Scalar(connection, typeCountSql, tx) == types
                        && Scalar(connection, rowCountSql, tx) == rows + 1, "Type metadata used the factory database instead of the caller database");
                    tx.Rollback();
                }
                Require(TypeCount() == types && RowCount() == rows, "Caller database override leaked a type or rows");
            });
            live($"TVP cleans generated type after SQL Server aborts the caller transaction, async={asynchronous}", () =>
            {
                int types = TypeCount(), rows = RowCount();
                NonQuery(connection, "SET XACT_ABORT ON");
                try
                {
                    using var tx = connection.BeginTransaction();
                    var failed = asynchronous ? Insert(-1).ExecuteTvpAsync(connector, tx).GetAwaiter().GetResult()
                        : Insert(-1).ExecuteTvp(connector, tx);
                    Require(failed.State == ResponseState.Error && connection.State == ConnectionState.Open,
                        "Server-aborted write did not retain the caller connection");
                    var active = tx.Connection == null ? null : tx;
                    Require(Scalar(connection, "SELECT XACT_STATE()", active) <= 0, "SQL Server did not abort the transaction");
                    Require(Scalar(connection, typeCountSql, active) == types, "Server-aborted caller transaction prevented type cleanup");
                    if (active != null) tx.Rollback();
                }
                finally { NonQuery(connection, "SET XACT_ABORT OFF"); }
                Require(TypeCount() == types && RowCount() == rows, "Doomed caller rollback leaked a type or rows");
            });
        }
        foreach (bool caller in new[] { false, true })
        {
            live($"async TVP cancellation after type creation cleans metadata, caller={caller}", () =>
            {
                int types = TypeCount(), rows = RowCount();
                using var cancellation = new CancellationTokenSource();
                var cancelling = new CancelAfterCreateConnector(connectionString, schema, connector.DbFlavor, cancellation);
                using var tx = caller ? connection.BeginTransaction() : null;
                bool cancelled = false;
                try { Insert(6).ExecuteTvpAsync(cancelling, tx, cancellation.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException error)
                {
                    Require(error.CancellationToken == cancellation.Token, "Cancellation token was replaced");
                    cancelled = true;
                }
                Require(cancelled, "TVP cancellation became an error response");
                if (caller)
                {
                    Require(tx.Connection == connection && Scalar(connection, "SELECT XACT_STATE()", tx) == 1,
                        "Cancellation completed the caller transaction");
                    tx.Rollback();
                }
                Require(TypeCount() == types && RowCount() == rows, "Cancellation leaked a generated type or wrote rows");
                Require(cancelling.Connections.All(x => x.State == ConnectionState.Closed), "Cancellation leaked an owned connection");
            });
        }
    }

    private sealed class CancelAfterCreateConnector(string connectionString, string schema, DbFlavor flavor,
        CancellationTokenSource cancellation)
        : BaseConnector(flavor, new ConnectorOptions { DefaultSchema = schema, CommandTimeout = TimeSpan.FromSeconds(30) })
    {
        internal readonly List<SqlConnection> Connections = [];
        public override DbConnection CreateConnection()
        {
            var connection = new SqlConnection(connectionString);
            Connections.Add(connection);
            return connection;
        }
        protected override async Task<SqlResponse<T>> ExecuteCommandAsync<T>(RenderedQuery query,
            Func<Task<SqlResponse<T>>> action, CancellationToken cancellationToken)
        {
            var response = await base.ExecuteCommandAsync(query, action, cancellationToken).ConfigureAwait(false);
            if (query.Text.StartsWith("CREATE TYPE") && response.State == ResponseState.Valid) cancellation.Cancel();
            return response;
        }
    }
}
