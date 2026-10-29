using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.SqlClient;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using static SqlLinqer.Tests.SqlServer.LiveSuite;

namespace SqlLinqer.Tests.SqlServer;

internal static class TvpLeaseChecks
{
    internal static int RunWorker(string[] args)
    {
        try
        {
            if (args.Length != 4) throw new ArgumentException("Invalid lease worker arguments");
            string schema = args[0], gate = args[1]; int id = int.Parse(args[2]);
            var connector = new LeaseConnector(Environment.GetEnvironmentVariable("SQLLINQER_TVP_WORKER_CONNECTION"), schema, Enum.Parse<DbFlavor>(args[3]));
            connector.Options.TvpLeaseDuration = TimeSpan.FromSeconds(1); connector.Options.TvpTypeRetention = TimeSpan.Zero;
            Valid(new InsertQuery<Row>(schema).InsertAuto(new Row { Id = id, Name = "worker" }).ExecuteTvp(connector));
            File.WriteAllText(gate + ".ready", "registered");
            var clock = Stopwatch.StartNew();
            while (!File.Exists(gate) && clock.Elapsed < TimeSpan.FromSeconds(30)) Thread.Sleep(10);
            if (!File.Exists(gate)) throw new TimeoutException("Lease worker resume gate timed out");
            Valid(new InsertQuery<Row>(schema).InsertAuto(new Row { Id = id + 1000, Name = "resumed" }).ExecuteTvp(connector));
            Console.WriteLine("LEASE_WORKER_OK"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    internal static void RunLive(SqlConnection connection, string settings, string parentSchema, DbFlavor flavor, Action<string, Action> live)
    {
        string schema = parentSchema + "_leases";
        string leases = $"[{schema}].[_SqlLinqerTvpLeases_v1]";
        var a = new LeaseConnector(settings, schema, flavor);
        InsertQuery<Row> Insert(int id) => new InsertQuery<Row>(schema).InsertAuto(new Row { Id = id, Name = "lease 雪" });
        List<SqlTvpTypeInfo> Types() => Valid(a.GetReusableTvpTypes()).Where(t => t.Schema == schema).ToList();
        int LeaseRows() => Scalar(connection, $"SELECT COUNT(*) FROM {leases}");
        void Expire()
        {
            using var clock = new SqlConnection(settings); clock.Open();
            NonQuery(clock, $"UPDATE {leases} SET LeaseUntil=DATEADD(day,-2,SYSUTCDATETIME()),RetireAfter=DATEADD(day,-1,SYSUTCDATETIME())");
        }
        int Cleanup(bool async) => Valid(async ? a.CleanupExpiredTvpTypesAsync().GetAwaiter().GetResult() : a.CleanupExpiredTvpTypes());
        void Case(string name, Action action) => live(name, () =>
        {
            try { action(); }
            finally
            {
                a.Before = null; a.After = null;
                foreach (var type in Types()) Valid(a.DropReusableTvpType(type));
                NonQuery(connection, $"DELETE FROM [{schema}].[LeaseRows]");
            }
        });
        NonQuery(connection, $"CREATE SCHEMA [{schema}]");
        try
        {
            NonQuery(connection, $"CREATE TABLE [{schema}].[LeaseRows] (Id int PRIMARY KEY,Name nvarchar(100) NULL)");
            Case("startup TVP cleanup is harmless before a registry exists", () =>
            {
                Require(Valid(a.CleanupExpiredTvpTypes()) == 0 && Valid(a.CleanupExpiredTvpTypesAsync().GetAwaiter().GetResult()) == 0
                    && Scalar(connection, $"SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'{schema}') AND name=N'_SqlLinqerTvpLeases_v1'") == 0,
                    "Startup cleanup created a registry or changed an untracked database");
            });
            Case("concurrent cold contracts create one owned lease registry without deadlocks", () =>
            {
                for (int round = 0; round < 5; round++)
                {
                    foreach (var type in Types()) Valid(a.DropReusableTvpType(type));
                    NonQuery(connection, $"DROP TABLE IF EXISTS {leases}");
                    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var writes = Enumerable.Range(0, 12).Select(i =>
                    {
                        var b = new LeaseConnector(settings, schema, flavor);
                        int id = 200 + round * 12 + i;
                        return i % 2 == 0 ? Insert(id).ExecuteTvpAsync(b, cancellationToken: cancellation.Token)
                            : new InsertQuery<IdRow>(schema).InsertAuto(new IdRow { Id = id }).ExecuteTvpAsync(b, cancellationToken: cancellation.Token);
                    }).ToArray();
                    Task.WhenAll(writes).GetAwaiter().GetResult();
                    Require(writes.All(t => Valid(t.Result) == 1) && Types().Count == 2 && LeaseRows() == 12,
                        "Concurrent cold definitions duplicated registry objects, lost nodes, or failed writes");
                }
                Require(Scalar(connection, $"SELECT COUNT(*) FROM [{schema}].[LeaseRows]") == 60, "Repeated cold starts lost or duplicated rows");
            });
            foreach (bool async in new[] { false, true })
            {
                SqlResponse<int> Execute(int id, BaseConnector connector = null, DbTransaction tx = null) => async
                    ? Insert(id).ExecuteTvpAsync(connector ?? a, tx).GetAwaiter().GetResult()
                    : Insert(id).ExecuteTvp(connector ?? a, tx);
                Case($"TVP leases retain live nodes and retention, delete idle types and rows, async={async}", () =>
                {
                    Require(Valid(Execute(1)) == 1 && Types().Count == 1 && LeaseRows() == 1, "Lease registration failed");
                    Require(Cleanup(async) == 0, "Live lease was deleted");
                    NonQuery(connection, $"UPDATE {leases} SET LeaseUntil=DATEADD(day,-1,SYSUTCDATETIME()),RetireAfter=DATEADD(day,1,SYSUTCDATETIME())");
                    Require(Cleanup(async) == 0, "Retention was ignored");
                    Expire();
                    Require(Cleanup(async) == 1 && Types().Count == 0 && LeaseRows() == 0 && Cleanup(async) == 0,
                        "Idle type, lease rows, or idempotence differ");
                });
                Case($"TVP leases preserve another node and renew before cached execution, async={async}", () =>
                {
                    var b = new LeaseConnector(settings, schema, flavor);
                    Valid(Execute(10)); Valid(Execute(11, b));
                    Require(LeaseRows() == 2 && Types().Count == 1, "Nodes did not share a type with independent registrations");
                    NonQuery(connection, $"UPDATE TOP (1) {leases} SET RetireAfter=DATEADD(day,-1,SYSUTCDATETIME())");
                    Require(Cleanup(async) == 0, "Cleanup ignored the other node's live lease");
                    var old = Types().Single(); Expire(); Require(Cleanup(async) == 1, "Expired nodes blocked cleanup");
                    Valid(Execute(12, b));
                    Require(Types().Single().Generation != old.Generation && LeaseRows() == 1
                        && Scalar(connection, $"SELECT COUNT(*) FROM [{schema}].[LeaseRows]") == 3,
                        "A resumed node trusted stale metadata or lost/duplicated data");
                });
                Case($"TVP cleanup skips an expired but executing transaction without waiting, async={async}", () =>
                {
                    Valid(Execute(20));
                    using var guarded = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
                    a.After = query =>
                    {
                        if (query.CommandRole != "TvpExecutionGuard") return;
                        guarded.Set(); Require(release.Wait(TimeSpan.FromSeconds(20)), "Execution barrier timed out");
                    };
                    var pending = Task.Run(() => Execute(21));
                    try
                    {
                        Require(guarded.Wait(TimeSpan.FromSeconds(20)), "Write did not acquire its execution guard");
                        Expire(); var clock = Stopwatch.StartNew();
                        Require(Cleanup(async) == 0 && clock.Elapsed < TimeSpan.FromSeconds(5) && Types().Count == 1,
                            "Cleanup blocked on or removed an active type");
                    }
                    finally { release.Set(); pending.Wait(TimeSpan.FromSeconds(20)); a.After = null; }
                    Require(Valid(pending.GetAwaiter().GetResult()) == 1, "Concurrent cleanup broke the write");
                    Require(Cleanup(async) == 1 && LeaseRows() == 0, "The type was not reclaimable after execution ended");
                });
                Case($"TVP guard protects caller ownership through completion and permits repeated use, async={async}", () =>
                {
                    Valid(Execute(30));
                    using (var tx = connection.BeginTransaction())
                    {
                        Valid(Execute(31, tx: tx));
                        // A cold node must validate and renew while another transaction protects the same type.
                        var b = new LeaseConnector(settings, schema, flavor);
                        Valid(Execute(32, b, tx));
                        Expire();
                        Require(Cleanup(async) == 0 && tx.Connection == connection, "Cleanup completed or ignored the caller transaction");
                        tx.Rollback();
                    }
                    Require(Cleanup(async) == 1 && Scalar(connection, $"SELECT COUNT(*) FROM [{schema}].[LeaseRows]") == 1,
                        "Rollback leaked locks, removed committed data, or committed caller writes");
                });
                Case($"paused TVP execution recreates a type deleted between renewal and guarding, async={async}", () =>
                {
                    Valid(Execute(35)); var old = Types().Single();
                    using var renewed = new ManualResetEventSlim(); using var resume = new ManualResetEventSlim();
                    int once = 0;
                    a.After = query =>
                    {
                        if (query.CommandRole != "TvpLeaseRenew" || Interlocked.Exchange(ref once, 1) != 0) return;
                        renewed.Set(); Require(resume.Wait(TimeSpan.FromSeconds(20)), "Renewal barrier timed out");
                    };
                    var pending = Task.Run(() => Execute(36));
                    try
                    {
                        Require(renewed.Wait(TimeSpan.FromSeconds(20)), "Write did not commit lease renewal");
                        Expire(); Require(Cleanup(async) == 1 && Types().Count == 0, "Paused pre-execution node blocked expiry");
                    }
                    finally { resume.Set(); pending.Wait(TimeSpan.FromSeconds(20)); a.After = null; }
                    Require(Valid(pending.GetAwaiter().GetResult()) == 1 && Types().Single().Generation != old.Generation
                        && Scalar(connection, $"SELECT COUNT(*) FROM [{schema}].[LeaseRows]") == 2,
                        "Resuming after deletion failed or sent duplicate writes");
                });
                Case($"TVP cleanup rechecks leases renewed after candidate discovery, async={async}", () =>
                {
                    Valid(Execute(40)); var generation = Types().Single().Generation; Expire(); bool renewed = false;
                    var b = new LeaseConnector(settings, schema, flavor);
                    b.Options.AutoCleanupTvpTypes = false; // Isolate renewal from another collector recreating the type.
                    a.Before = query =>
                    {
                        if (query.CommandRole != "TvpTypeExpire" || renewed) return;
                        renewed = true; Valid(Execute(41, b));
                    };
                    Require(Cleanup(async) == 0 && renewed && Types().Count == 1 && Types().Single().Generation == generation && LeaseRows() >= 1,
                        "Cleanup trusted an expired snapshot after lease renewal");
                });
                Case($"TVP cleanup rejects a replacement generation after candidate discovery, async={async}", () =>
                {
                    Valid(Execute(50)); Expire(); var old = Types().Single(); bool replaced = false;
                    var b = new LeaseConnector(settings, schema, flavor);
                    a.Before = query =>
                    {
                        if (query.CommandRole != "TvpTypeExpire" || replaced) return;
                        replaced = true; Valid(b.DropReusableTvpType(old)); Valid(Execute(51, b));
                    };
                    Require(Cleanup(async) == 0 && replaced && Types().Single().Generation != old.Generation,
                        "Stale cleanup deleted a replacement generation");
                });
                Case($"TVP cleanup preserves pinned and application types, async={async}", () =>
                {
                    Valid(Execute(60)); var type = Types().Single();
                    NonQuery(connection, $"CREATE TYPE [{schema}].[ApplicationRows] AS TABLE (Id int); CREATE TABLE [{schema}].[Unrelated] (Id int)");
                    NonQuery(connection, $"CREATE PROCEDURE [{schema}].[Pinned] @Rows [{schema}].[{type.Name}] READONLY AS SELECT COUNT(*) FROM @Rows");
                    try
                    {
                        Expire(); Require(Cleanup(async) == 0 && Types().Count == 1, "Cleanup removed a procedure dependency");
                        Require(Scalar(connection, $"SELECT COUNT(*) FROM sys.table_types WHERE schema_id=SCHEMA_ID(N'{schema}')") == 2
                            && Scalar(connection, $"SELECT COUNT(*) FROM [{schema}].[Unrelated]") == 0, "Cleanup touched application objects");
                    }
                    finally { NonQuery(connection, $"DROP PROCEDURE [{schema}].[Pinned]; DROP TYPE [{schema}].[ApplicationRows]; DROP TABLE [{schema}].[Unrelated]"); }
                    Require(Cleanup(async) == 1 && LeaseRows() == 0, "Formerly pinned type was never reclaimed");
                });
                Case($"TVP failure and cancellation leave no execution locks or committed rows, async={async}", () =>
                {
                    Valid(Execute(70));
                    Require(Execute(70).State == ResponseState.Error, "Expected duplicate key failure");
                    if (async)
                    {
                        using var cancellation = new CancellationTokenSource();
                        a.After = query => { if (query.CommandRole == "TvpExecutionGuard") cancellation.Cancel(); };
                        try { Insert(71).ExecuteTvpAsync(a, cancellationToken: cancellation.Token).GetAwaiter().GetResult(); throw new Exception("Expected cancellation"); }
                        catch (OperationCanceledException) { }
                        finally { a.After = null; }
                    }
                    Expire(); Require(Cleanup(async) == 1 && LeaseRows() == 0
                        && Scalar(connection, $"SELECT COUNT(*) FROM [{schema}].[LeaseRows]") == 1, "Failure leaked a guard or committed cancelled data");
                });
            }
            Case("concurrent TVP collectors reclaim each type once and prune abandoned lease rows", () =>
            {
                Valid(Insert(80).ExecuteTvp(a));
                Valid(new InsertQuery<IdRow>(schema).InsertAuto(new IdRow { Id = 81 }).ExecuteTvp(a));
                Require(Types().Count == 2, "Deployment contracts did not coexist"); Expire();
                var collectors = Enumerable.Range(0, 8).Select(_ => new LeaseConnector(settings, schema, flavor).CleanupExpiredTvpTypesAsync()).ToArray();
                Task.WhenAll(collectors).GetAwaiter().GetResult();
                Require(collectors.Sum(t => Valid(t.Result)) == 2 && Types().Count == 0 && LeaseRows() == 0, "Concurrent collectors duplicated work or leaked rows");
                NonQuery(connection, $"INSERT INTO {leases} VALUES (N'gone',NEWID(),NEWID(),SYSUTCDATETIME(),SYSUTCDATETIME())");
                Require(Valid(a.CleanupExpiredTvpTypes()) == 0 && LeaseRows() == 0, "Externally removed type left an orphan lease");
            });
            Case("expired node registrations renew concurrently without row-lock deadlocks or duplicate writes", () =>
            {
                var nodes = Enumerable.Range(0, 12).Select(_ => new LeaseConnector(settings, schema, flavor)).ToArray();
                for (int i = 0; i < nodes.Length; i++)
                {
                    nodes[i].Options.AutoCleanupTvpTypes = false;
                    Valid(Insert(10000 + i).ExecuteTvp(nodes[i]));
                }
                for (int round = 1; round <= 6; round++)
                {
                    Expire();
                    var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    var writes = nodes.Select((node, i) => Task.Run(async () =>
                    {
                        await start.Task;
                        return await Insert(10000 + round * 100 + i).ExecuteTvpAsync(node, cancellationToken: cancellation.Token);
                    })).ToArray();
                    start.SetResult(); Task.WhenAll(writes).GetAwaiter().GetResult();
                    Require(writes.All(t => Valid(t.Result) == 1) && LeaseRows() == 12 && Types().Count == 1,
                        "Concurrent expiry renewal deadlocked, lost a node, or replaced a shared type");
                }
                Require(Scalar(connection, $"SELECT COUNT(*) FROM [{schema}].[LeaseRows]") == 84,
                    "Renewal stress lost or duplicated committed rows");
            });
            Case("independent schemas share catalog coordination during concurrent provisioning and cleanup", () =>
            {
                string parallel = schema + "_parallel";
                var b = new LeaseConnector(settings, parallel, flavor);
                NonQuery(connection, $"CREATE SCHEMA [{parallel}]");
                try
                {
                    NonQuery(connection, $"DROP TABLE IF EXISTS {leases}; CREATE TABLE [{parallel}].[LeaseRows] (Id int PRIMARY KEY,Name nvarchar(100))");
                    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var writes = Enumerable.Range(0, 12).Select(i => i % 2 == 0
                        ? Insert(3000 + i).ExecuteTvpAsync(a, cancellationToken: cancellation.Token)
                        : new InsertQuery<Row>(parallel).InsertAuto(new Row { Id = 3000 + i }).ExecuteTvpAsync(b, cancellationToken: cancellation.Token)).ToArray();
                    Task.WhenAll(writes).GetAwaiter().GetResult();
                    Require(writes.All(t => Valid(t.Result) == 1), "Concurrent schema provisioning failed");
                    Expire();
                    NonQuery(connection, $"UPDATE [{parallel}].[_SqlLinqerTvpLeases_v1] SET RetireAfter=DATEADD(day,-1,SYSUTCDATETIME())");
                    var collectors = new[] { a.CleanupExpiredTvpTypesAsync(), b.CleanupExpiredTvpTypesAsync() };
                    Task.WhenAll(collectors).GetAwaiter().GetResult();
                    Require(collectors.Sum(t => Valid(t.Result)) == 2 && Types().Count == 0 && LeaseRows() == 0
                        && Scalar(connection, $"SELECT COUNT(*) FROM [{parallel}].[_SqlLinqerTvpLeases_v1]") == 0,
                        "Cross-schema cleanup deadlocked or left managed types or leases behind");
                }
                finally
                {
                    foreach (var type in Valid(b.GetReusableTvpTypes()).Where(t => t.Schema == parallel)) Valid(b.DropReusableTvpType(type));
                    NonQuery(connection, $"DROP TABLE IF EXISTS [{parallel}].[_SqlLinqerTvpLeases_v1]; DROP TABLE [{parallel}].[LeaseRows]; DROP SCHEMA [{parallel}]");
                }
            });
            Case("deployment shapes coexist and retire independently while rollback can recreate an old contract", () =>
            {
                Valid(Insert(85).ExecuteTvp(a)); var old = Types().Single();
                var b = new LeaseConnector(settings, schema, flavor);
                Valid(new InsertQuery<IdRow>(schema).InsertAuto(new IdRow { Id = 86 }).ExecuteTvp(b));
                Require(Types().Count == 2, "New deployment replaced the old contract");
                using (var expire = new SqlCommand($"UPDATE {leases} SET RetireAfter=DATEADD(day,-1,SYSUTCDATETIME()) WHERE TypeName=@name", connection))
                { expire.Parameters.AddWithValue("@name", old.Name); expire.ExecuteNonQuery(); }
                Require(Valid(a.CleanupExpiredTvpTypes()) == 1 && Types().Single().Name != old.Name && LeaseRows() == 1,
                    "Retiring the old contract removed the live deployment");
                Valid(Insert(87).ExecuteTvp(a));
                Require(Types().Count == 2 && Types().Single(t => t.Name == old.Name).Generation != old.Generation && LeaseRows() == 2,
                    "Rollback could not restore a retired contract");
            });
            Case("database-clock expiry triggers bounded opportunistic cleanup on later use", () =>
            {
                var b = new LeaseConnector(settings, schema, flavor);
                b.Options.TvpLeaseDuration = TimeSpan.FromSeconds(1);
                b.Options.TvpTypeRetention = TimeSpan.Zero;
                b.Options.TvpCleanupInterval = TimeSpan.FromSeconds(1);
                Valid(Insert(90).ExecuteTvp(b)); var old = Types().Single();
                NonQuery(connection, "WAITFOR DELAY '00:00:02'");
                Valid(new InsertQuery<IdRow>(schema).InsertAuto(new IdRow { Id = 91 }).ExecuteTvp(b));
                Require(Types().Count == 1 && Types().Single().Name != old.Name && LeaseRows() == 1,
                    "Real expiry did not reclaim the idle deployment contract on later use");
            });
            Case("lease registry survives pool-size-one cold concurrent use and cleanup", () =>
            {
                var pool = new SqlConnectionStringBuilder(settings) { MaxPoolSize = 1, ApplicationName = "TVP lease " + Guid.NewGuid().ToString("N") };
                var b = new LeaseConnector(pool.ConnectionString, schema, flavor);
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var writes = Enumerable.Range(0, 8).Select(i => Insert(100 + i).ExecuteTvpAsync(b, cancellationToken: cancel.Token)).ToArray();
                Task.WhenAll(writes).GetAwaiter().GetResult();
                Require(writes.All(t => Valid(t.Result) == 1) && LeaseRows() == 1, "Lease management exhausted the connection pool or duplicated node registrations");
                Expire(); Require(Valid(b.CleanupExpiredTvpTypesAsync(cancellationToken: cancel.Token).GetAwaiter().GetResult()) == 1 && LeaseRows() == 0,
                    "Cleanup required an extra pool slot");
                using var identity = new SqlConnection(pool.ConnectionString); SqlConnection.ClearPool(identity);
            });
            Case("crashed process leases expire and a paused independent process safely resumes", () =>
            {
                string directory = Path.Combine(Path.GetTempPath(), "sqllinqer-lease-workers-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var children = new List<(Process Process, string Gate, Task<string> Output, Task<string> Error)>();
                try
                {
                    foreach (int id in new[] { 120, 121 })
                    {
                        string gate = Path.Combine(directory, id.ToString());
                        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                        foreach (var arg in new[] { schema, gate, id.ToString(), flavor.ToString() }) start.ArgumentList.Add(arg);
                        start.Environment["SQLLINQER_TVP_LEASE_WORKER"] = "1";
                        start.Environment["SQLLINQER_TVP_WORKER_CONNECTION"] = settings;
                        var process = Process.Start(start);
                        children.Add((process, gate, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync()));
                        var clock = Stopwatch.StartNew();
                        while (!File.Exists(gate + ".ready") && !process.HasExited && clock.Elapsed < TimeSpan.FromSeconds(20)) Thread.Sleep(10);
                        Require(File.Exists(gate + ".ready"), process.HasExited ? children[^1].Error.GetAwaiter().GetResult() : "Worker did not register");
                    }
                    Require(Types().Count == 1 && LeaseRows() == 2, "Independent processes did not share one type with separate leases");
                    var old = Types().Single();
                    children[0].Process.Kill(entireProcessTree: true); children[0].Process.WaitForExit();
                    NonQuery(connection, "WAITFOR DELAY '00:00:02'");
                    Require(Valid(a.CleanupExpiredTvpTypes()) == 1 && Types().Count == 0 && LeaseRows() == 0,
                        "Process death left unreclaimable leases or types");
                    File.WriteAllText(children[1].Gate, "resume");
                    Require(children[1].Process.WaitForExit(20000) && children[1].Process.ExitCode == 0
                        && children[1].Output.GetAwaiter().GetResult().Contains("LEASE_WORKER_OK"),
                        children[1].Process.HasExited ? children[1].Error.GetAwaiter().GetResult() : "Resumed worker timed out");
                    Require(Types().Single().Generation != old.Generation && LeaseRows() == 1
                        && Scalar(connection, $"SELECT COUNT(*) FROM [{schema}].[LeaseRows]") == 3,
                        "Independent process trusted its stale cache or duplicated writes after resuming");
                }
                finally
                {
                    foreach (var child in children)
                    {
                        if (!child.Process.HasExited) child.Process.Kill(entireProcessTree: true);
                        child.Process.WaitForExit(); child.Process.Dispose();
                    }
                    Directory.Delete(directory, recursive: true);
                }
            });
            Case("TVP registry collision leaves application tables intact and provisioning atomic", () =>
            {
                string occupied = schema + "_other";
                NonQuery(connection, $"CREATE SCHEMA [{occupied}]");
                try
                {
                    NonQuery(connection, $"CREATE TABLE [{occupied}].[_SqlLinqerTvpLeases_v1] (Value int); INSERT INTO [{occupied}].[_SqlLinqerTvpLeases_v1] VALUES (123); CREATE TABLE [{occupied}].[LeaseRows] (Id int PRIMARY KEY,Name nvarchar(100) NULL)");
                    var b = new LeaseConnector(settings, occupied, flavor);
                    Require(new InsertQuery<Row>(occupied).InsertAuto(new Row { Id = 1 }).ExecuteTvp(b).State == ResponseState.Error
                        && b.CleanupExpiredTvpTypes().State == ResponseState.Error, "A colliding application table was adopted");
                    Require(Scalar(connection, $"SELECT Value FROM [{occupied}].[_SqlLinqerTvpLeases_v1]") == 123
                        && Scalar(connection, $"SELECT COUNT(*) FROM [{occupied}].[LeaseRows]") == 0
                        && Scalar(connection, $"SELECT COUNT(*) FROM sys.table_types WHERE schema_id=SCHEMA_ID(N'{occupied}')") == 0,
                        "Registry collision changed application data or leaked partially provisioned types");
                }
                finally { NonQuery(connection, $"DROP TABLE [{occupied}].[_SqlLinqerTvpLeases_v1]; DROP TABLE [{occupied}].[LeaseRows]; DROP SCHEMA [{occupied}]"); }
            });
            Case("opportunistic cleanup failures preserve writes while explicit cleanup reports the error", () =>
            {
                Valid(Insert(130).ExecuteTvp(a)); Expire();
                var b = new LeaseConnector(settings, schema, flavor); int failures = 0;
                b.Before = query =>
                {
                    if (query.CommandRole == "TvpTypeExpire") { failures++; throw new InvalidOperationException("Deliberate cleanup failure"); }
                };
                Require(Valid(new InsertQuery<IdRow>(schema).InsertAuto(new IdRow { Id = 131 }).ExecuteTvp(b)) == 1
                    && failures == 1 && Types().Count == 2, "Automatic maintenance failed the write or removed an unexpected contract");
                Require(b.CleanupExpiredTvpTypes().State == ResponseState.Error && failures == 2,
                    "Explicit cleanup hid its failure");
                b.Before = null;
                Require(Valid(b.CleanupExpiredTvpTypes()) == 1 && Types().Count == 1 && LeaseRows() == 1,
                    "A failed cleanup attempt prevented later reclamation");
            });
            Case("TVP cleanup bounds each pass and removes lease rows with all retired generations", () =>
            {
                // Seed a deployment history through SQL, with the same ownership and generation records as provisioned types.
                NonQuery(connection, $"""
                    DECLARE @i int=0,@name sysname,@hash nvarchar(64),@generation nvarchar(36),@sql nvarchar(max);
                    WHILE @i<105 BEGIN
                        SET @hash=LOWER(CONVERT(nvarchar(64),HASHBYTES('SHA2_256',CONVERT(nvarchar(20),@i)),2));
                        SET @name=N'SqlLinqerTvp_v1_'+@hash;
                        SET @generation=CONVERT(nvarchar(36),NEWID());
                        SET @sql=N'CREATE TYPE [{schema}].'+QUOTENAME(@name)+N' AS TABLE (Id int NULL)'; EXEC(@sql);
                        EXEC sys.sp_addextendedproperty @name=N'SqlLinqer.TvpContract.v1',@value=@hash,@level0type=N'SCHEMA',@level0name=N'{schema}',@level1type=N'TYPE',@level1name=@name;
                        EXEC sys.sp_addextendedproperty @name=N'SqlLinqer.TvpGeneration.v1',@value=@generation,@level0type=N'SCHEMA',@level0name=N'{schema}',@level1type=N'TYPE',@level1name=@name;
                        INSERT INTO {leases} VALUES (@name,@generation,NEWID(),DATEADD(day,-2,SYSUTCDATETIME()),DATEADD(day,-1,SYSUTCDATETIME()));
                        SET @i+=1;
                    END;
                    """);
                Require(Types().Count == 105 && Valid(a.CleanupExpiredTvpTypes()) == 100 && Types().Count == 5 && LeaseRows() == 5,
                    "A cleanup pass exceeded its bound or left lease rows behind");
                Require(Valid(a.CleanupExpiredTvpTypesAsync().GetAwaiter().GetResult()) == 5 && Types().Count == 0 && LeaseRows() == 0,
                    "Later cleanup did not drain the remaining retired contracts");
            });
            Case("TVP cleanup targets the requested database rather than the factory default", () =>
            {
                Valid(Insert(140).ExecuteTvp(a)); Expire();
                var master = new SqlConnectionStringBuilder(settings) { InitialCatalog = "master" };
                var b = new LeaseConnector(master.ConnectionString, schema, flavor);
                Require(Valid(b.CleanupExpiredTvpTypes(databaseName: connection.Database)) == 1 && Types().Count == 0 && LeaseRows() == 0,
                    "Explicit database selection did not reclaim the requested type");
            });
        }
        finally
        {
            foreach (var type in Types()) Valid(a.DropReusableTvpType(type));
            NonQuery(connection, $"DROP TABLE IF EXISTS {leases}; DROP TABLE IF EXISTS [{schema}].[LeaseRows]; DROP SCHEMA [{schema}]");
        }
    }

    [SqlTable("LeaseRows")]
    private sealed class Row { public int Id { get; set; } public string Name { get; set; } }
    [SqlTable("LeaseRows")]
    private sealed class IdRow { public int Id { get; set; } }

    private sealed class LeaseConnector(string settings, string schema, DbFlavor flavor)
        : BaseConnector(flavor, new ConnectorOptions { ReusableTvpSchema = schema, DefaultSchema = schema, CommandTimeout = TimeSpan.FromSeconds(10) })
    {
        internal Action<RenderedQuery> Before, After;
        public override DbConnection CreateConnection() => new SqlConnection(settings);
        protected override SqlResponse<T> ExecuteCommand<T>(RenderedQuery query, Func<SqlResponse<T>> action)
        {
            Before?.Invoke(query); var result = base.ExecuteCommand(query, action); After?.Invoke(query); return result;
        }
        protected override async Task<SqlResponse<T>> ExecuteCommandAsync<T>(RenderedQuery query, Func<Task<SqlResponse<T>>> action, CancellationToken token)
        {
            Before?.Invoke(query); var result = await base.ExecuteCommandAsync(query, action, token); After?.Invoke(query); return result;
        }
    }
}
