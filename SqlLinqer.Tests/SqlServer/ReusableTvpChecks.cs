using System.Data;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.SqlClient;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;
using static SqlLinqer.Tests.SqlServer.LiveSuite;

namespace SqlLinqer.Tests.SqlServer;

internal static class ReusableTvpChecks
{
    internal static int RunWorker(string[] args)
    {
        try
        {
            if (args.Length != 4) throw new ArgumentException("Invalid TVP worker arguments");
            string schema = args[0], gate = args[1]; int id = int.Parse(args[2]);
            var flavor = Enum.Parse<DbFlavor>(args[3]);
            File.WriteAllText(gate + ".ready." + id, "ready");
            var clock = Stopwatch.StartNew();
            while (!File.Exists(gate) && clock.Elapsed < TimeSpan.FromSeconds(30)) Thread.Sleep(10);
            if (!File.Exists(gate)) throw new TimeoutException("TVP worker gate timed out");
            var settings = Environment.GetEnvironmentVariable("SQLLINQER_TVP_WORKER_CONNECTION");
            var connector = NewConnector(settings, schema, flavor);
            Require(Valid(Insert(schema, id).ExecuteTvp(connector)) == 1, "Worker insert count differs");
            Console.WriteLine("REUSE_WORKER_OK " + id);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL reusable TVP worker: " + error); return 1; }
    }

    private static CountingConnector NewConnector(string settings, string schema, DbFlavor flavor) => new(settings, schema, flavor);
    private static InsertQuery<ReusableRow> Insert(string schema, int id, decimal amount = 1.25m)
        => new InsertQuery<ReusableRow>(schema).InsertAuto(new ReusableRow { Id = id, Name = "reuse 雪", Amount = amount, Payload = new byte[] { 1, 2 } });

    internal static void RunLive(SqlConnection connection, string settings, string schema, DbFlavor flavor, Action<string, Action> live)
    {
        var connector = NewConnector(settings, schema, flavor);
        var table = $"[{schema}].[ReusableRows]";
        NonQuery(connection, $"CREATE TABLE {table} (Id int PRIMARY KEY, Name nvarchar(100) NULL, Amount decimal(38,8) NOT NULL DEFAULT 0 CHECK (Amount >= 0), Payload varbinary(max) NULL);");
        int Rows() => Scalar(connection, $"SELECT COUNT(*) FROM {table}");
        List<SqlTvpTypeInfo> Types() => Valid(connector.GetReusableTvpTypes()).Where(t => t.Schema == schema).ToList();
        void Cleanup()
        {
            foreach (var type in Types()) Require(Valid(connector.DropReusableTvpType(type)) == 1, "Managed fixture cleanup failed");
        }
        void Case(string name, Action action) => live(name, () =>
        {
            try { action(); }
            finally { Cleanup(); NonQuery(connection, $"DELETE FROM {table}"); connector.Ensures = 0; }
        });
        try
        {
            Case("reusable TVP independent processes converge on one committed type", () =>
            {
                int before = Rows();
                string directory = Path.Combine(Path.GetTempPath(), "sqllinqer-reuse-workers-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                string gate = Path.Combine(directory, "go");
                var processes = new List<(Process Process, Task<string> Output, Task<string> Error)>();
                try
                {
                    for (int i = 1; i <= 3; i++)
                    {
                        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                        foreach (string arg in new[] { schema, gate, (100 + i).ToString(), flavor.ToString() }) start.ArgumentList.Add(arg);
                        start.Environment["SQLLINQER_TVP_REUSE_WORKER"] = "1";
                        start.Environment["SQLLINQER_TVP_WORKER_CONNECTION"] = settings;
                        var process = Process.Start(start);
                        processes.Add((process, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync()));
                    }
                    var clock = Stopwatch.StartNew();
                    while (Directory.GetFiles(directory, "*.ready.*").Length != 3 && clock.Elapsed < TimeSpan.FromSeconds(30)) Thread.Sleep(10);
                    Require(Directory.GetFiles(directory, "*.ready.*").Length == 3, "Workers did not reach the shared start gate");
                    File.WriteAllText(gate, "go");
                    foreach (var child in processes)
                    {
                        Require(child.Process.WaitForExit(30000), "Worker timed out");
                        Require(child.Process.ExitCode == 0 && child.Output.GetAwaiter().GetResult().Contains("REUSE_WORKER_OK"), child.Error.GetAwaiter().GetResult());
                    }
                    Require(Types().Count == 1 && Rows() == before + 3, "Concurrent nodes duplicated the type or lost writes");
                }
                finally
                {
                    foreach (var child in processes)
                    {
                        if (!child.Process.HasExited) child.Process.Kill(entireProcessTree: true);
                        child.Process.WaitForExit(); child.Process.Dispose();
                    }
                    Directory.Delete(directory, recursive: true);
                }
            });
            foreach (bool asynchronous in new[] { false, true })
            {
                Case($"default TVPs avoid warm DDL preserve values and share across CLR models, async={asynchronous}", () =>
                {
                    connector.Ensures = 0;
                    for (int i = 0; i < 10; i++)
                    {
                        var query = Insert(schema, 200 + i);
                        Require(Valid(asynchronous ? query.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : query.ExecuteTvp(connector)) == 1, "Warm insert failed");
                    }
                    Require(connector.Ensures == 1 && Types().Count == 1, "Warm requests repeated provisioning");
                    var same = new InsertQuery<EquivalentRow>(schema).InsertAuto(new EquivalentRow { Id = 220, Name = "other CLR", Amount = 9.75m });
                    Require(Valid(asynchronous ? same.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : same.ExecuteTvp(connector)) == 1
                        && connector.Ensures == 1 && Types().Count == 1, "Identity depends on CLR class rather than the SQL contract");
                    using var read = new SqlCommand($"SELECT Id,Name,Amount,Payload FROM {table} WHERE Id=200", connection);
                    using var reader = read.ExecuteReader();
                    Require(reader.Read() && reader.GetInt32(0) == 200 && reader.GetString(1) == "reuse 雪" && reader.GetDecimal(2) == 1.25m
                        && ((byte[])reader.GetValue(3)).SequenceEqual(new byte[] { 1, 2 }), "Reused TVP changed values");
                });
                Case($"reusable SQL shape versions distinguish decimal scales and column subsets, async={asynchronous}", () =>
                {
                    var whole = Insert(schema, 300, 1m); var fractional = Insert(schema, 301, 1.125m);
                    Require(Valid(asynchronous ? whole.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : whole.ExecuteTvp(connector)) == 1, "Whole number insert failed");
                    Require(Valid(asynchronous ? fractional.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : fractional.ExecuteTvp(connector)) == 1, "Fractional insert failed");
                    var subset = new InsertQuery<SubsetRow>(schema).InsertAuto(new SubsetRow { Id = 302, Name = "subset" });
                    Require(Valid(asynchronous ? subset.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : subset.ExecuteTvp(connector)) == 1, "Subset insert failed");
                    Require(Types().Count == 3 && Types().Select(t => t.Name).Distinct().Count() == 3, "Different versions overwrote a shared type");
                });
                Case($"reusable TVPs survive write rollback and preserve caller ownership, async={asynchronous}", () =>
                {
                    int before = Rows();
                    Require(Valid(Insert(schema, 400).ExecuteTvp(connector)) == 1, "Warmup failed");
                    var original = Types().Single();
                    using (var tx = connection.BeginTransaction())
                    {
                        var query = Insert(schema, 401);
                        Require(Valid(asynchronous ? query.ExecuteTvpAsync(connector, tx).GetAwaiter().GetResult() : query.ExecuteTvp(connector, tx)) == 1
                            && tx.Connection == connection, "Caller ownership differs");
                        tx.Rollback();
                    }
                    var failed = Insert(schema, 402, -1.25m);
                    var response = asynchronous ? failed.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : failed.ExecuteTvp(connector);
                    Require(response.State == ResponseState.Error && Rows() == before + 1 && Types().Single().TypeId == original.TypeId,
                        "Failed request removed the shared type or committed rows");
                });
            }
            Case("reusable cold starts work with one pool slot and coalesce concurrent requests", () =>
            {
                var pool = new SqlConnectionStringBuilder(settings) { MaxPoolSize = 1, MinPoolSize = 0, ApplicationName = "SqlLinqer reuse pool " + Guid.NewGuid().ToString("N") };
                var limited = NewConnector(pool.ConnectionString, schema, flavor);
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var tasks = Enumerable.Range(0, 8).Select(i => Insert(schema, 450 + i).ExecuteTvpAsync(limited, cancellationToken: cancellation.Token)).ToArray();
                Task.WhenAll(tasks).GetAwaiter().GetResult();
                Require(tasks.All(t => t.Result.State == ResponseState.Valid && t.Result.Result == 1) && limited.Ensures == 1 && Types().Count == 1,
                    "Cold reuse required an extra pool slot or repeated provisioning");
                using var poolIdentity = new SqlConnection(pool.ConnectionString);
                SqlConnection.ClearPool(poolIdentity);
            });
            Case("same-connector cold requests share one provisioning operation", () =>
            {
                var concurrent = NewConnector(settings, schema, flavor);
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var tasks = Enumerable.Range(0, 12).Select(i => Insert(schema, 470 + i).ExecuteTvpAsync(concurrent, cancellationToken: cancellation.Token)).ToArray();
                Task.WhenAll(tasks).GetAwaiter().GetResult();
                Require(tasks.All(t => t.Result.State == ResponseState.Valid) && concurrent.Ensures == 1 && Types().Count == 1,
                    "Concurrent requests on one connector repeated provisioning");
            });
            Case("explicit named TVPs share a procedure type without taking ownership", () =>
            {
                NonQuery(connection, $"CREATE TYPE [{schema}].[BorrowedRows] AS TABLE (Id int NOT NULL, Name nvarchar(100) NULL, Amount decimal(18,4) NULL, Payload varbinary(max) NULL);");
                try
                {
                    foreach (bool asynchronous in new[] { false, true })
                    {
                        var query = new InsertWithPrimaryKeyQuery<ReusableRow, int>(schema).UseTvpType("BorrowedRows", schema)
                            .InsertAuto(new ReusableRow { Id = asynchronous ? 501 : 500, Name = "borrowed", Amount = 2.125m });
                        var response = asynchronous ? query.ExecuteTvpAsync(connector).GetAwaiter().GetResult() : query.ExecuteTvp(connector);
                        Require(Valid(response) == 1 && response.TouchedPrimaryKeys.Single() == (asynchronous ? 501 : 500), "Named TVP changed output keys");
                    }
                    Require(Types().Count == 0 && Scalar(connection, $"SELECT COUNT(*) FROM sys.table_types WHERE name=N'BorrowedRows' AND schema_id=SCHEMA_ID(N'{schema}')") == 1,
                        "Named type was adopted, removed, or replaced");
                }
                finally { NonQuery(connection, $"DROP TYPE [{schema}].[BorrowedRows]"); }
            });
            foreach (bool asynchronous in new[] { false, true })
                Case($"reusable maintenance protects procedure dependencies and rejects stale type identities, async={asynchronous}", () =>
            {
                Require(Valid(Insert(schema, 600).ExecuteTvp(connector)) == 1, "Provisioning failed");
                var first = asynchronous
                    ? Valid(connector.GetReusableTvpTypesAsync().GetAwaiter().GetResult()).Single(t => t.Schema == schema)
                    : Types().Single();
                SqlResponse<int> Drop(SqlTvpTypeInfo info) => asynchronous
                    ? connector.DropReusableTvpTypeAsync(info).GetAwaiter().GetResult()
                    : connector.DropReusableTvpType(info);
                NonQuery(connection, $"CREATE PROCEDURE [{schema}].[PinnedReuse] @Rows [{first.Schema}].[{first.Name}] READONLY AS SELECT COUNT(*) FROM @Rows;");
                try { Require(Drop(first).State == ResponseState.Error && Types().Count == 1, "Cleanup dropped a procedure-referenced type"); }
                finally { NonQuery(connection, $"DROP PROCEDURE [{schema}].[PinnedReuse]"); }
                Require(Valid(Drop(first)) == 1 && Types().Count == 0, "Retirement did not remove the selected type");
                Require(Valid(Drop(first)) == 0, "Repeated cleanup is not idempotent");
                Require(Valid(Insert(schema, 601).ExecuteTvp(connector)) == 1, "Provisioning after cleanup failed");
                var current = Types().Single();
                Require(current.Generation != first.Generation && Drop(first).State == ResponseState.Error
                    && Types().Single().TypeId == current.TypeId, "Stale cleanup removed a replacement type");
                var recycledId = new SqlTvpTypeInfo
                {
                    Database = current.Database,
                    Schema = current.Schema,
                    Name = current.Name,
                    ContractHash = current.ContractHash,
                    TypeId = current.TypeId,
                    Generation = first.Generation
                };
                Require(Drop(recycledId).State == ResponseState.Error && Types().Single().Generation == current.Generation,
                    "Cleanup relied only on a recyclable numeric type ID");
            });
            Case("reusable type collisions are rejected without adopting unowned objects", () =>
            {
                Require(Valid(Insert(schema, 700).ExecuteTvp(connector)) == 1, "Provisioning failed");
                var type = Types().Single();
                Require(Valid(connector.DropReusableTvpType(type)) == 1, "Cleanup failed");
                NonQuery(connection, $"CREATE TYPE [{schema}].[{type.Name}] AS TABLE (Id bigint NULL);");
                try
                {
                    var response = Insert(schema, 701).ExecuteTvp(connector);
                    Require(response.State == ResponseState.Error && Types().Count == 0, "Unowned type was adopted");
                    var unowned = new SqlTvpTypeInfo
                    {
                        Database = type.Database,
                        Schema = type.Schema,
                        Name = type.Name,
                        ContractHash = type.ContractHash,
                        TypeId = Scalar(connection, $"SELECT TYPE_ID(N'[{schema}].[{type.Name}]')"),
                        Generation = type.Generation
                    };
                    Require(connector.DropReusableTvpType(unowned).State == ResponseState.Error, "Cleanup accepted an unowned type with a matching name and ID");
                    using var command = new SqlCommand($"EXEC sys.sp_addextendedproperty @name=N'SqlLinqer.TvpContract.v1',@value=@Hash,@level0type=N'SCHEMA',@level0name=@Schema,@level1type=N'TYPE',@level1name=@Name; DECLARE @Generation nvarchar(36)=CONVERT(nvarchar(36),NEWID()); EXEC sys.sp_addextendedproperty @name=N'SqlLinqer.TvpGeneration.v1',@value=@Generation,@level0type=N'SCHEMA',@level0name=@Schema,@level1type=N'TYPE',@level1name=@Name", connection);
                    command.Parameters.AddWithValue("@Hash", type.ContractHash); command.Parameters.AddWithValue("@Schema", schema); command.Parameters.AddWithValue("@Name", type.Name); command.ExecuteNonQuery();
                    response = Insert(schema, 702).ExecuteTvp(connector);
                    Require(response.State == ResponseState.Error && response.Error.ToString().Contains("columns do not match"), "An ownership marker bypassed schema validation or failed before comparing the catalog");
                }
                finally { NonQuery(connection, $"DROP TYPE [{schema}].[{type.Name}]"); }
            });
            Case("reusable provisioning uses the caller database instead of the factory default", () =>
            {
                var master = new SqlConnectionStringBuilder(settings) { InitialCatalog = "master" };
                var other = NewConnector(master.ConnectionString, schema, flavor);
                using (var tx = connection.BeginTransaction())
                {
                    Require(Valid(Insert(schema, 800).ExecuteTvp(other, tx)) == 1, "Caller database provisioning failed");
                    tx.Rollback();
                }
                Require(Types().Count == 1 && Valid(other.GetReusableTvpTypes(connection.Database)).Any(t => t.Schema == schema), "Type was provisioned in the wrong database");
            });
            foreach (bool caller in new[] { false, true })
                Case($"cancelled reusable provisioning retains the committed type but executes no write, caller={caller}", () =>
                {
                    using var cancellation = new CancellationTokenSource();
                    var cancelling = NewConnector(settings, schema, flavor); cancelling.CancelAfterEnsure = cancellation;
                    using var transaction = caller ? connection.BeginTransaction() : null;
                    int before = caller ? Scalar(connection, $"SELECT COUNT(*) FROM {table}", transaction) : Rows();
                    try { Insert(schema, 900).ExecuteTvpAsync(cancelling, transaction, cancellationToken: cancellation.Token).GetAwaiter().GetResult(); throw new Exception("Expected cancellation"); }
                    catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token) { }
                    if (caller)
                    {
                        Require(transaction.Connection == connection && Scalar(connection, "SELECT XACT_STATE()", transaction) == 1,
                            "Cancellation completed the caller's transaction");
                        transaction.Rollback();
                    }
                    Require(Rows() == before && Types().Count == 1, "Cancellation wrote rows or deleted a reusable contract");
                });
        }
        finally { Cleanup(); NonQuery(connection, $"DROP TABLE IF EXISTS [{schema}].[_SqlLinqerTvpLeases_v1]; DROP TABLE {table}"); }
    }

    [SqlTable("ReusableRows")]
    private sealed class ReusableRow
    {
        [SqlPrimaryKey(false)] public int Id { get; set; }
        public string Name { get; set; }
        public decimal Amount { get; set; }
        public byte[] Payload { get; set; }
    }
    [SqlTable("ReusableRows")]
    private sealed class EquivalentRow { public int Id { get; set; } public string Name { get; set; } public decimal Amount { get; set; } public byte[] Payload { get; set; } }
    [SqlTable("ReusableRows")]
    private sealed class SubsetRow { public int Id { get; set; } public string Name { get; set; } }

    private sealed class CountingConnector(string settings, string schema, DbFlavor flavor)
        : BaseConnector(flavor, new ConnectorOptions { DefaultSchema = schema, ReusableTvpSchema = schema, CommandTimeout = TimeSpan.FromSeconds(30) })
    {
        internal int Ensures;
        internal CancellationTokenSource CancelAfterEnsure;
        public override System.Data.Common.DbConnection CreateConnection() => new SqlConnection(settings);
        protected override SqlResponse<T> ExecuteCommand<T>(RenderedQuery query, Func<SqlResponse<T>> action)
        {
            if (query.CommandRole == "TvpTypeEnsure") Interlocked.Increment(ref Ensures);
            return base.ExecuteCommand(query, action);
        }
        protected override async Task<SqlResponse<T>> ExecuteCommandAsync<T>(RenderedQuery query, Func<Task<SqlResponse<T>>> action, CancellationToken cancellationToken)
        {
            if (query.CommandRole == "TvpTypeEnsure") Interlocked.Increment(ref Ensures);
            var response = await base.ExecuteCommandAsync(query, action, cancellationToken);
            if (query.CommandRole == "TvpTypeEnsure" && response.State == ResponseState.Valid) CancelAfterEnsure?.Cancel();
            return response;
        }
    }
}
