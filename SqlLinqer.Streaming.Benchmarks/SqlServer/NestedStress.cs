using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Components.Select;

// Benchmark-only models and version adapter. No dependency or hooks in production mapping.
internal static class NestedStress
{
    // Configure the same internal query shape in both binaries before timing.
    private static readonly System.Reflection.FieldInfo SelectsField = typeof(SqlLinqer.Queries.Core.SelectQuery)
        .GetField("Selects", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    private static readonly System.Reflection.FieldInfo DistinctField = typeof(SqlLinqer.Queries.Core.SelectQuery)
        .GetField("Distinct", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    internal sealed record Fixture(string Name, int Users, int Groups, int Permissions, int Grants, int Width, bool Distinct = false, bool NestedDistinct = false);
    internal static async Task Run(string[] args)
    {
        var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(args[1]))!;
        string settings = Environment.GetEnvironmentVariable("SQLLINQER_PROBE_SQLSERVER") ?? throw new Exception("Missing settings");
        using var connection = new SqlConnection(settings) { StatisticsEnabled = true };
        await connection.OpenAsync();
        if (args[0] == "setup") { await Setup(connection, fixture); return; }
        string mode = args[0];
        var connector = new ConnectionFactoryConnector(() => new SqlConnection(settings), DbFlavor.SqlServer);
        var streamMethod = typeof(BaseConnector).GetMethods().Single(m => m.IsGenericMethodDefinition &&
            (m.Name == "ExecuteReaderModelAsyncStream" || m.Name == "ExecuteReaderJsonAsyncStream") && m.GetParameters().Length == 4);
        var stream = (Func<RenderedQuery, DbTransaction, CancellationToken, TimeSpan?, IAsyncEnumerable<StressUser>>)
            streamMethod.MakeGenericMethod(typeof(StressUser)).CreateDelegate(typeof(Func<RenderedQuery, DbTransaction, CancellationToken, TimeSpan?, IAsyncEnumerable<StressUser>>), connector);
        var bufferMethod = typeof(BaseConnector).GetMethods().Single(m => m.IsGenericMethodDefinition &&
            (m.Name == "ExecuteReaderModelAsync" || m.Name == "ExecuteReaderJsonAsync") && m.GetParameters().Length == 4);
        var buffer = (Func<RenderedQuery, DbTransaction, CancellationToken, TimeSpan?, Task<SqlResponse<List<StressUser>>>>)
            bufferMethod.MakeGenericMethod(typeof(StressUser)).CreateDelegate(typeof(Func<RenderedQuery, DbTransaction, CancellationToken, TimeSpan?, Task<SqlResponse<List<StressUser>>>>), connector);
        RenderedQuery Query(bool warmup)
        {
            var q = new SelectQuery<StressUser>().SelectRootColumns().Select(x => x.Groups)
                .Select(x => x.Groups.First().Permissions).Select(x => x.Groups.First().Permissions.First().Grants)
                .Where(x => x.Id, 0, warmup ? SqlOp.EQ : SqlOp.GT).OrderByAuto();
            if (fixture.Distinct) q.Distinct();
            if (fixture.NestedDistinct)
            {
                q.Select(x => x.DistinctGroups);
                var selects = (SelectCollection)SelectsField.GetValue(q)!;
                var projection = selects.GetAll<SelectRelationship>().Single(r => r.Alias == nameof(StressUser.DistinctGroups));
                var child = projection.SubQuery;
                var columns = (SelectCollection)SelectsField.GetValue(child)!;
                columns.Clear(); columns.AddStatement(new SelectColumn(projection.Relationship.ChildTable.Columns.TryGet(nameof(StressGroup.Id)), nameof(StressGroup.Id)));
                DistinctField.SetValue(child, true);
            }
            return q.Render(DbFlavor.SqlServer);
        }
        var query = Query(false); var warm = Query(true);
        if (mode == "plan")
        {
            using var command = connection.CreateCommand(); command.CommandTimeout = 180;
            command.CommandText = "SET STATISTICS XML ON;" + query.Text + ";SET STATISTICS XML OFF;";
            foreach (var parameter in query.Parameters.GetAll())
                command.Parameters.AddWithValue(parameter.Placehodler, parameter.Value ?? DBNull.Value);
            using var reader = await command.ExecuteReaderAsync(); System.Xml.Linq.XDocument? plans = null;
            do
            {
                while (await reader.ReadAsync())
                    if (reader.FieldCount == 1 && reader.GetName(0).Contains("Showplan"))
                    {
                        var plan = System.Xml.Linq.XDocument.Parse(reader.GetString(0));
                        if (plans == null) plans = plan;
                        else
                        {
                            var ns = plans.Root!.Name.Namespace;
                            plans.Root.Element(ns + "BatchSequence")!.Add(plan.Root!.Element(ns + "BatchSequence")!.Elements());
                        }
                    }
            } while (await reader.NextResultAsync());
            if (plans == null) throw new Exception("Missing actual plan");
            File.WriteAllText(args[2], plans.ToString()); Console.WriteLine("Actual plans captured"); return;
        }
        using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        for (int i = 0; i < 3; i++)
        {
            await foreach (var row in stream(warm, tx, default, TimeSpan.FromMinutes(3))) Validate(row, fixture, true);
            var result = await buffer(warm, tx, default, TimeSpan.FromMinutes(3));
            if (result.Error != null) throw result.Error;
            foreach (var row in result.Result) Validate(row, fixture, true);
        }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long liveBefore = GC.GetTotalMemory(true);
        Console.WriteLine(JsonSerializer.Serialize(new { ready = true, pid = Environment.ProcessId, fixture, mode }));
        Console.Out.Flush();
        if (Console.ReadLine() != "go") throw new Exception("Missing measurement gate");
        connection.ResetStatistics();
        int[] beforeCollections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        long allocated = GC.GetTotalAllocatedBytes(true), start = Stopwatch.GetTimestamp();
        double firstMs = 0; int roots = 0; long checksum = 0, objects = 0;
        List<StressUser>? retained = null;
        if (mode == "buffered")
        {
            var result = await buffer(query, tx, default, TimeSpan.FromMinutes(3));
            if (result.Error != null) throw result.Error;
            retained = result.Result;
            foreach (var row in retained) { objects += Validate(row, fixture, false); checksum += row.Id; roots++; }
        }
        else
        {
            await foreach (var row in stream(query, tx, default, TimeSpan.FromMinutes(3)))
            {
                if (roots == 0) firstMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                objects += Validate(row, fixture, false); checksum += row.Id; roots++;
                if (mode == "first") break;
            }
        }
        double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        allocated = GC.GetTotalAllocatedBytes(true) - allocated;
        long liveBeforeGC = GC.GetTotalMemory(false);
        var stats = connection.RetrieveStatistics();
        int expectedRoots = mode == "first" ? 1 : fixture.Users;
        long expectedChecksum = mode == "first" ? 1 : (long)fixture.Users * (fixture.Users + 1) / 2;
        long perRoot = 1L + fixture.Groups * (1L + fixture.Permissions * (1L + fixture.Grants)) + (fixture.NestedDistinct ? fixture.Groups : 0);
        if (roots != expectedRoots || checksum != expectedChecksum || objects != expectedRoots * perRoot)
            throw new Exception($"Invalid totals: {roots}, {checksum}, {objects}");
        long retainedAfterGC = GC.GetTotalMemory(true);
        GC.KeepAlive(retained);
        int[] collections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - beforeCollections[i]).ToArray();
        var sample = new
        {
            fixture,
            mode,
            transport = streamMethod.Name.Contains("Json") ? "released-library-json" : "production-native",
            roots,
            objects,
            checksum,
            firstMs = mode == "buffered" ? (double?)null : firstMs,
            elapsedMs,
            allocatedBytes = allocated,
            providerBytesReceived = Convert.ToInt64(stats["BytesReceived"]),
            providerBytesSent = Convert.ToInt64(stats["BytesSent"]),
            managedLiveBeforeBytes = liveBefore,
            managedLiveBeforeFinalGCBytes = liveBeforeGC,
            managedRetainedAfterGCBytes = retainedAfterGC,
            gcCollections = collections,
            sqlCharacters = query.Text.Length,
            runtime = Environment.Version.ToString(),
            serverVersion = connection.ServerVersion
        };
        Console.WriteLine(JsonSerializer.Serialize(sample));
        await tx.RollbackAsync();
    }

    private static long Validate(StressUser user, Fixture f, bool warm)
    {
        int groups = warm ? 1 : f.Groups, permissions = warm ? 1 : f.Permissions, grants = warm ? (f.Grants > 0 ? 1 : 0) : f.Grants;
        if (warm ? user.Id != 0 : user.Id < 1 || user.Id > f.Users) throw new Exception("Invalid user");
        Name(user.Name, "user 雪 ", user.Id, f.Width);
        if (user.Groups.Count != groups) throw new Exception("Incomplete groups");
        long count = 1;
        if (f.NestedDistinct)
        {
            if (user.DistinctGroups == null || user.DistinctGroups.Count != groups) throw new Exception("Incomplete distinct sibling");
            for (int i = 0; i < groups; i++)
            {
                var group = user.DistinctGroups[i]; int id = warm ? 0 : (user.Id - 1) * f.Groups + i + 1;
                if (group.Id != id || group.UserId != 0 || group.Name != null || group.Permissions.Count != 0) throw new Exception("Invalid distinct sibling projection");
                count++;
            }
        }
        for (int gi = 0; gi < groups; gi++)
        {
            var group = user.Groups[gi]; int gid = warm ? 0 : (user.Id - 1) * f.Groups + gi + 1;
            if (group.Id != gid || group.UserId != user.Id || group.Permissions.Count != permissions) throw new Exception("Invalid group");
            Name(group.Name, "group 雪 ", gid, f.Width); count++;
            for (int pi = 0; pi < permissions; pi++)
            {
                var permission = group.Permissions[pi]; int pid = warm ? 0 : (gid - 1) * f.Permissions + pi + 1;
                if (permission.Id != pid || permission.GroupId != gid || permission.Grants.Count != grants) throw new Exception("Invalid permission");
                Name(permission.Name, "permission 雪 ", pid, f.Width); count++;
                for (int li = 0; li < grants; li++)
                {
                    var grant = permission.Grants[li]; int id = warm ? 0 : (pid - 1) * f.Grants + li + 1;
                    if (grant.Id != id || grant.PermissionId != pid) throw new Exception("Invalid grant");
                    if (id % 2 == 0) { if (grant.Name != null) throw new Exception("Invalid NULL"); }
                    else Name(grant.Name, "grant 雪 ", id, f.Width);
                    count++;
                }
            }
        }
        return count;
    }
    private static void Name(string? value, string prefix, int id, int width)
    {
        Span<char> digits = stackalloc char[12]; id.TryFormat(digits, out int length);
        if (value == null || value.Length != prefix.Length + length + width || !value.AsSpan().StartsWith(prefix)
            || !value.AsSpan(prefix.Length, length).SequenceEqual(digits[..length])) throw new Exception("Invalid name");
        foreach (char c in value.AsSpan(prefix.Length + length)) if (c != 'x') throw new Exception("Invalid payload");
    }
    private static async Task Setup(SqlConnection connection, Fixture f)
    {
        if (f.Users < 1 || f.Groups < 1 || f.Permissions < 1 || f.Grants < 0 || f.Width < 0 || f.Width > 1800
            || (long)f.Users * f.Groups * f.Permissions * Math.Max(1, f.Grants) > int.MaxValue) throw new Exception("Invalid fixture bounds");
        using var command = connection.CreateCommand(); command.CommandTimeout = 180;
        command.CommandText = """
            DROP TABLE IF EXISTS stress_grants,stress_permissions,stress_groups,stress_users;
            CREATE TABLE stress_users(Id int PRIMARY KEY,Name nvarchar(2000));
            CREATE TABLE stress_groups(Id int PRIMARY KEY,UserId int,Name nvarchar(2000));
            CREATE TABLE stress_permissions(Id int PRIMARY KEY,GroupId int,Name nvarchar(2000));
            CREATE TABLE stress_grants(Id int PRIMARY KEY,PermissionId int,Name nvarchar(2000));
            SELECT TOP (@maximum) CONVERT(int,ROW_NUMBER() OVER (ORDER BY (SELECT NULL))) n INTO #numbers
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            INSERT stress_users SELECT n,N'user 雪 '+CONVERT(nvarchar(12),n)+REPLICATE(N'x',@width) FROM #numbers WHERE n<=@users;
            INSERT stress_groups SELECT (u.Id-1)*@groups+n,u.Id,N'group 雪 '+CONVERT(nvarchar(12),(u.Id-1)*@groups+n)+REPLICATE(N'x',@width)
            FROM stress_users u CROSS JOIN #numbers WHERE n<=@groups;
            INSERT stress_permissions SELECT (g.Id-1)*@permissions+n,g.Id,N'permission 雪 '+CONVERT(nvarchar(12),(g.Id-1)*@permissions+n)+REPLICATE(N'x',@width)
            FROM stress_groups g CROSS JOIN #numbers WHERE n<=@permissions;
            INSERT stress_grants SELECT (p.Id-1)*@grants+n,p.Id,CASE WHEN ((p.Id-1)*@grants+n)%2=0 THEN NULL
            ELSE N'grant 雪 '+CONVERT(nvarchar(12),(p.Id-1)*@grants+n)+REPLICATE(N'x',@width) END
            FROM stress_permissions p CROSS JOIN #numbers WHERE n<=@grants;
            INSERT stress_users VALUES(0,N'user 雪 0'+REPLICATE(N'x',@width));
            INSERT stress_groups VALUES(0,0,N'group 雪 0'+REPLICATE(N'x',@width));
            INSERT stress_permissions VALUES(0,0,N'permission 雪 0'+REPLICATE(N'x',@width));
            IF @grants>0 INSERT stress_grants VALUES(0,0,NULL);
            CREATE INDEX ix_stress_groups ON stress_groups(UserId,Id);
            CREATE INDEX ix_stress_permissions ON stress_permissions(GroupId,Id);
            CREATE INDEX ix_stress_grants ON stress_grants(PermissionId,Id);
            UPDATE STATISTICS stress_users WITH FULLSCAN;
            UPDATE STATISTICS stress_groups WITH FULLSCAN;
            UPDATE STATISTICS stress_permissions WITH FULLSCAN;
            UPDATE STATISTICS stress_grants WITH FULLSCAN;
            """;
        command.Parameters.AddWithValue("maximum", new[] { f.Users, f.Groups, f.Permissions, f.Grants }.Max());
        command.Parameters.AddWithValue("users", f.Users); command.Parameters.AddWithValue("groups", f.Groups);
        command.Parameters.AddWithValue("permissions", f.Permissions); command.Parameters.AddWithValue("grants", f.Grants);
        command.Parameters.AddWithValue("width", f.Width); await command.ExecuteNonQueryAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { setup = true, fixture = f, expectedObjects = (long)f.Users * (1L + f.Groups * (1L + f.Permissions * (1L + f.Grants)) + (f.NestedDistinct ? f.Groups : 0)) }));
    }
}

[SqlTable("stress_users")]
public sealed class StressUser
{
    [SqlPrimaryKey, SqlAutoOrderBy] public int Id { get; set; }
    public string? Name { get; set; }
    [SqlOneToMany(nameof(StressGroup.UserId))] public List<StressGroup> Groups { get; set; } = [];
    [SqlOneToMany(nameof(StressGroup.UserId))] public List<StressGroup>? DistinctGroups { get; set; }
}
[SqlTable("stress_groups")]
public sealed class StressGroup
{
    [SqlPrimaryKey, SqlAutoOrderBy] public int Id { get; set; }
    public int UserId { get; set; }
    public string? Name { get; set; }
    [SqlOneToMany(nameof(StressPermission.GroupId))] public List<StressPermission> Permissions { get; set; } = [];
}
[SqlTable("stress_permissions")]
public sealed class StressPermission
{
    [SqlPrimaryKey, SqlAutoOrderBy] public int Id { get; set; }
    public int GroupId { get; set; }
    public string? Name { get; set; }
    [SqlOneToMany(nameof(StressGrant.PermissionId))] public List<StressGrant> Grants { get; set; } = [];
}
[SqlTable("stress_grants")]
public sealed class StressGrant
{
    [SqlPrimaryKey, SqlAutoOrderBy] public int Id { get; set; }
    public int PermissionId { get; set; }
    public string? Name { get; set; }
}
