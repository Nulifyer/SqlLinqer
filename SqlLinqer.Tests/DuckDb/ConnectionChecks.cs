using System.Data;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using static SqlLinqer.Tests.DuckDb.CheckAssertions;

namespace SqlLinqer.Tests.DuckDb;

internal static class ConnectionChecks
{
    internal static void Deadlines(BaseConnector connector, Action<string, Action> Test)
    {
        long Scalar(string sql) => Result(connector.ExecuteScalar<long>(new RenderedQuery(sql)));
        Test("JSON and tabular readers honor command deadlines", () =>
        {
            var timed = new ConnectionFactoryConnector(connector.CreateConnection, DbFlavor.DuckDb, new ConnectorOptions { CommandTimeout = TimeSpan.FromMilliseconds(10) });
            var json = timed.ExecuteReaderModel<NumericSummary>(new RenderedQuery("SELECT sum(sin(i::DOUBLE)) AS Value FROM range(9223372036854775807) t(i)"));
            Expect(json.State == ResponseState.Error && json.Error.InnerException is TimeoutException, "JSON execution did not cancel");
            var table = timed.ExecuteReaderDataTable(new RenderedQuery("SELECT sum(sin(i::DOUBLE)) FROM range(9223372036854775807) t(i)"));
            Expect(table.State == ResponseState.Error && table.Error.InnerException is TimeoutException, "Tabular execution did not cancel");
        });
        Test("command deadline interrupts native work and permits caller rollback and connection reuse", () =>
        {
            using var connection = connector.CreateConnection(); connection.Open();
            using (var tx = connection.BeginTransaction(IsolationLevel.Snapshot))
            {
                Result(connector.ExecuteNonQuery(new RenderedQuery("UPDATE people SET Note='deadline rollback' WHERE Id=100"), tx));
                var timed = new ConnectionFactoryConnector(connector.CreateConnection, DbFlavor.DuckDb, new ConnectorOptions { CommandTimeout = TimeSpan.FromMilliseconds(10) });
                var failure = timed.ExecuteScalar<double>(new RenderedQuery("SELECT sum(sin(i::DOUBLE)) FROM range(9223372036854775807) t(i)"), tx);
                Expect(failure.State == ResponseState.Error && failure.Error.InnerException is TimeoutException, "Native query was not interrupted as timeout");
                Expect(connection.State == ConnectionState.Open && tx.Connection == connection, "Deadline disposed caller transaction");
                var aborted = connector.ExecuteScalar<int>(new RenderedQuery("SELECT count(*) FROM people"), tx);
                Expect(aborted.State == ResponseState.Error && aborted.Error.ToString().Contains("aborted", StringComparison.OrdinalIgnoreCase), "Interrupted native transaction did not report aborted state");
                tx.Rollback();
            }
            using (var tx = connection.BeginTransaction(IsolationLevel.Snapshot))
            {
                for (int i = 0; i < 20; i++)
                    Expect(Result(connector.ExecuteScalar<int>(new RenderedQuery("SELECT 42"), tx)) == 42, "Late deadline interrupted reused connection");
                tx.Commit();
            }
            Expect(Scalar("SELECT count(*) FROM people WHERE Id=100 AND Note IS NULL") == 1, "Deadline rollback failed");
            var owned = new ConnectionFactoryConnector(connector.CreateConnection, DbFlavor.DuckDb, new ConnectorOptions { CommandTimeout = TimeSpan.FromMilliseconds(10) });
            var ownedError = owned.ExecuteNonQuery(new RenderedQuery("UPDATE people SET Note='deadline rollback' WHERE Id=100; SELECT sum(sin(i::DOUBLE)) FROM range(9223372036854775807) t(i)"));
            Expect(ownedError.State == ResponseState.Error && ownedError.Error.InnerException is TimeoutException && Scalar("SELECT count(*) FROM people WHERE Id=100 AND Note IS NULL") == 1, "Owned deadline failed to roll back");
            var unlimited = new ConnectionFactoryConnector(connector.CreateConnection, DbFlavor.DuckDb, new ConnectorOptions { CommandTimeout = TimeSpan.Zero });
            Expect(Result(unlimited.ExecuteScalar<int>(new RenderedQuery("SELECT 42"))) == 42, "Zero deadline failed");
        });
    }
}
