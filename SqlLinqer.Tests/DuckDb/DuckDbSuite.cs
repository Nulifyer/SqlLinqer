using System.Data;
using System.Diagnostics;
using DuckDB.NET.Data;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;
using static SqlLinqer.Tests.DuckDb.CheckAssertions;

namespace SqlLinqer.Tests.DuckDb;

internal static class DuckDbSuite
{
    internal static int Run()
    {
        int passed = 0, failed = 0;
        void Test(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine($"PASS {name}"); }
            catch (Exception error) { failed++; Console.WriteLine($"FAIL {name}\n{error}"); }
        }
        string path = Path.Combine(Path.GetTempPath(), $"sqlinqer-duckdb-{Guid.NewGuid():N}.db");
        using var example = new DuckDBConnection($"Data Source={path}");
        var connector = new ReplicatorConnector(example, DbFlavor.DuckDb);
        void Sql(string sql) => Result(connector.ExecuteNonQuery(new RenderedQuery(sql)));
        long Scalar(string sql) => Result(connector.ExecuteScalar<long>(new RenderedQuery(sql)));
        try
        {
            Console.WriteLine($"Provider {typeof(DuckDBConnection).Assembly.GetName().Version}; native {Result(connector.ExecuteScalar<string>(new RenderedQuery("SELECT version()")))}; {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
            Sql("CREATE SEQUENCE person_ids START 100; CREATE TABLE people (Id INTEGER PRIMARY KEY DEFAULT nextval('person_ids'), Name VARCHAR, Enabled BOOLEAN, Kind INTEGER, Token UUID, Born TIMESTAMP, Amount DECIMAL(18,4), Payload BLOB, Note VARCHAR, CategoryId INTEGER); CREATE TABLE categories (Id INTEGER, Name VARCHAR); CREATE TABLE children (Id INTEGER PRIMARY KEY, PersonId INTEGER, Name VARCHAR); INSERT INTO categories VALUES (1, 'group'); INSERT INTO children VALUES (1,100,'z'),(2,100,'a'),(3,100,'m');");
            Sql("CREATE TABLE envelopes (Id INTEGER, PersonId INTEGER); INSERT INTO envelopes VALUES (1,100),(2,-1)");
            var person = new Person { Name = "O'Brien_%\\", Enabled = true, Kind = Kind.Second, Token = Guid.NewGuid(), Born = new DateTime(2024, 2, 29, 12, 34, 56), Amount = 123.4567m, Payload = [0, 1, 127, 128, 255], CategoryId = 1 };
            MappingChecks.Run(connector, person, Test);
            QueryChecks.Run(connector, person, Test);
            WriteChecks.Update(connector, Test);
            QueryChecks.Aggregates(connector, Test);
            WriteChecks.Defaults(connector, Test);
            QueryChecks.Predicates(connector, Test);
            MappingChecks.WideScalars(connector, Test);
            ConnectionChecks.Deadlines(connector, example, Test);
            WriteChecks.Delete(connector, Test);
            Test("stored procedures, TVPs and unsupported functions reject before execution", () =>
            {
                Reject(() => new SqlLinqer.Tests.Regression.ProcedureProbe().Render(new SqlLinqer.Tests.Regression.ProcedureProbe.Input(), DbFlavor.DuckDb));
                Reject(() => new InsertQuery<Person>().InsertAuto(person).ExecuteTvp(connector));
                Reject(() => new ParameterCollection(DbFlavor.DuckDb).AddTvpParameter(new DataTable(), "unused"));
                Reject(() => new SelectQueryInitializer<Person>().BuildQuery(x => new { Value = x.SOUNDDEX(p => p.Name) }).Render(DbFlavor.DuckDb));
            });
            Test("replicated file connections persist with no injected timeout", () =>
            {
                using var connection = connector.CreateConnection();
                Expect(connection.ConnectionString == example.ConnectionString && !connection.ConnectionString.Contains("Timeout"), "Connection string changed");
                connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT count(*) FROM people";
                Expect(Convert.ToInt64(command.ExecuteScalar()) == 2 && File.Exists(path), "File persistence failed");
            });
            Test("TRUNCATE native statement preserves sequence", () =>
            {
                Expect(new TruncateQuery<Person>().Render(DbFlavor.DuckDb).Text.StartsWith("TRUNCATE TABLE"), "Wrong truncate SQL");
                Expect(Result(new TruncateQuery<Person>().Execute(connector)) == 2 && Scalar("SELECT count(*) FROM people") == 0, "TRUNCATE failed");
                var added = new InsertWithPrimaryKeyQuery<Person, int>().InsertAuto(new Person()).Execute(connector);
                Expect(Result(added) == 1 && added.TouchedPrimaryKeys.Single() > 103, "TRUNCATE reset sequence unexpectedly");
            });
            Test("plain memory databases are independent; caller transaction keeps memory alive", () =>
            {
                using var memory = new DuckDBConnection("Data Source=:memory:"); memory.Open();
                using (var command = memory.CreateCommand()) { command.CommandText = "CREATE TABLE people (Id INTEGER, Name VARCHAR); INSERT INTO people VALUES (1,'memory');"; command.ExecuteNonQuery(); }
                var memConnector = new ReplicatorConnector(memory, DbFlavor.DuckDb);
                using var transaction = memory.BeginTransaction(IsolationLevel.Snapshot);
                Expect(Result(new SelectQuery<MemoryPerson>().SelectRootColumns().Execute(memConnector, transaction)).Single().Name == "memory", "Memory transaction lost database");
                using var separate = memConnector.CreateConnection(); separate.Open();
                using var probe = separate.CreateCommand(); probe.CommandText = "SELECT count(*) FROM information_schema.tables WHERE table_name='people'";
                Expect(Convert.ToInt64(probe.ExecuteScalar()) == 0, "Expected isolated memory connection");
                transaction.Rollback();
            });
            SqlLinqer.Tests.Regression.CompatibleDatabaseTests.Run(connector, Test);
            SqlLinqer.Tests.Regression.SqlJsonContractChecks.Run(connector, Test);
            // Optional diagnostic only: elapsed time depends on the host and is not a regression assertion.
            if (Environment.GetEnvironmentVariable("SQLLINQER_DUCKDB_TIMEOUT_PROBE") == "1")
            {
                using var connection = connector.CreateConnection(); connection.Open();
                using var command = connection.CreateCommand(); command.CommandTimeout = 1;
                command.CommandText = "SELECT sum(sin(i::DOUBLE)) FROM range(150000000) t(i)";
                var timer = Stopwatch.StartNew(); command.ExecuteScalar(); timer.Stop();
                Console.WriteLine($"CommandTimeout={command.CommandTimeout}; elapsed={timer.Elapsed.TotalSeconds:F3}s");
            }
        }
        finally { File.Delete(path); File.Delete(path + ".wal"); }
        SqlLinqer.Tests.Regression.AsyncProviderChecks.Run(DbFlavor.DuckDb, Test);
        Console.WriteLine($"{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
