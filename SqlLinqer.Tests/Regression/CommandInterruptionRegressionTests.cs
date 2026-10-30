using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SqlLinqer.Connections;
using SqlLinqer.Components.Render;

namespace SqlLinqer.Tests.Regression;

internal static class CommandInterruptionRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (string mode in new[] { "sync-timeout", "async-timeout", "async-cancel", "stream-timeout", "stream-cancel" })
            test("native interruption: SQLite " + mode, () => Interrupt(mode).GetAwaiter().GetResult());
        test("native interruption: SQLite rolls back owned writes and leaves caller transactions usable", Rollback);
    }

    private static async Task Interrupt(string mode)
    {
        int disposed = 0, interrupts = 0;
        var connector = new ConnectionFactoryConnector(() =>
        {
            var connection = new SqliteConnection("Data Source=:memory:;Pooling=False");
            connection.Disposed += (_, _) => Interlocked.Increment(ref disposed);
            return connection;
        }, DbFlavor.Sqlite, new ConnectorOptions
        {
            CommandTimeout = mode.Contains("timeout") ? TimeSpan.FromMilliseconds(100) : TimeSpan.Zero,
            CancelCommand = command =>
            {
                Interlocked.Increment(ref interrupts);
                SQLitePCL.raw.sqlite3_interrupt(((SqliteConnection)command.Connection!).Handle);
            }
        });
        using var cancel = new CancellationTokenSource();
        if (mode.Contains("cancel")) cancel.CancelAfter(100);
        const string source = "WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<1000000000000) SELECT ";
        // A separate watchdog interrupts the provider if the implementation regresses,
        // keeping this regression bounded without turning a hang into a passing test.
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // The callback is deliberately independent of CommandDeadline.
        var opened = new List<SqliteConnection>();
        var original = connector;
        var guarded = new ConnectionFactoryConnector(() =>
        {
            var connection = (SqliteConnection)original.CreateConnection();
            opened.Add(connection);
            return connection;
        }, DbFlavor.Sqlite, connector.Options);
        using var guard = watchdog.Token.Register(() =>
        {
            foreach (var connection in opened)
                try { SQLitePCL.raw.sqlite3_interrupt(connection.Handle); } catch (ObjectDisposedException) { }
        });
        var clock = Stopwatch.StartNew();
        bool cancelled = false, timedOut = false;
        try
        {
            if (mode.StartsWith("stream"))
                await foreach (var _ in guarded.ExecuteReaderModelAsyncStream<ValueRow>(new(source + "SUM(x) AS Value FROM n"), cancellationToken: cancel.Token)) { }
            else
            {
                var query = new RenderedQuery(source + "SUM(x) FROM n");
                var response = mode.StartsWith("sync") ? guarded.ExecuteReaderList<long>(query)
                    : await guarded.ExecuteReaderListAsync<long>(query, cancellationToken: cancel.Token);
                timedOut = response.Error?.InnerException is TimeoutException;
            }
        }
        catch (OperationCanceledException) { cancelled = true; }
        catch (SqlLinqer.Exceptions.SqlResponseException error) { timedOut = error.InnerException is TimeoutException; }
        Check(!watchdog.IsCancellationRequested && clock.Elapsed < TimeSpan.FromSeconds(3), "Interruption depended on the safety watchdog");
        Check(mode.Contains("cancel") ? cancelled : timedOut, "Cancellation/deadline outcome was lost");
        Check(disposed == 1 && interrupts > 0, "Interrupted connection was not disposed");
        int after = interrupts;
        cancel.Cancel();
        await Task.Delay(60);
        Check(interrupts == after, "Late cancellation reached a completed command");
        Check(guarded.ExecuteReaderList<int>(new("SELECT 42")).GetResultOrThrow().Single() == 42, "A subsequent connection was interrupted");
    }

    private static void Rollback()
    {
        string file = Path.Combine(Path.GetTempPath(), "sqllinqer-interrupt-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var db = new ConnectionFactoryConnector(() => new SqliteConnection($"Data Source={file};Pooling=False"), DbFlavor.Sqlite,
                new ConnectorOptions { CancelCommand = command => SQLitePCL.raw.sqlite3_interrupt(((SqliteConnection)command.Connection!).Handle) });
            db.ExecuteNonQuery(new("CREATE TABLE changes(Id INTEGER PRIMARY KEY)")).EnsureSuccess();
            const string sql = "INSERT INTO changes VALUES(1); WITH RECURSIVE n(x) AS (VALUES(1) UNION ALL SELECT x+1 FROM n WHERE x<1000000000) SELECT SUM(x) FROM n";
            using (var cancel = new CancellationTokenSource(100))
            {
                bool interrupted = false;
                try { db.ExecuteScalarAsync<long>(new(sql), cancellationToken: cancel.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { interrupted = true; }
                Check(interrupted, "Write batch did not cancel");
            }
            Check(db.ExecuteReaderList<int>(new("SELECT COUNT(*) FROM changes")).GetResultOrThrow().Single() == 0, "Cancelled owned batch committed its earlier insert");
            using var connection = db.CreateConnection(); connection.Open();
            using var transaction = connection.BeginTransaction();
            using (var cancel = new CancellationTokenSource(100))
            {
                try { db.ExecuteReaderListAsync<long>(new(sql[(sql.IndexOf(';') + 1)..]), transaction, cancel.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { }
            }
            Check(connection.State == System.Data.ConnectionState.Open, "Caller connection was disposed");
            Check(db.ExecuteReaderList<int>(new("SELECT 42"), transaction).GetResultOrThrow().Single() == 42, "Cancellation leaked into caller's next query");
            transaction.Rollback();
        }
        finally { File.Delete(file); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public sealed class ValueRow { public long Value { get; set; } }
}
