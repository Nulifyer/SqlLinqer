using Microsoft.Data.Sqlite;
using SqlLinqer.Components.Select;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class QueryDistinctChecks
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (bool queryOnly in new[] { false, true })
            test("SQLite query-level DISTINCT supports read-only execution, query_only=" + queryOnly,
                () => Check(queryOnly).GetAwaiter().GetResult());
    }

    private static async Task Check(bool queryOnly)
    {
        string source = "Data Source=distinct_" + Guid.NewGuid().ToString("N") + ";Mode=Memory;Cache=Shared";
        using var connection = new SqliteConnection(source); await connection.OpenAsync();
        using var setup = connection.CreateCommand();
        setup.CommandText = "CREATE TABLE distinct_users(Id INTEGER,Name TEXT COLLATE NOCASE);"
            + "CREATE TABLE distinct_permissions(Id INTEGER,UserId INTEGER,Name TEXT);"
            + "INSERT INTO distinct_users VALUES(1,'Ada'),(1,'Ada'),(2,'Ada'),(3,NULL);"
            + "INSERT INTO distinct_permissions VALUES(10,1,'read'),(10,1,'read'),(20,2,'write');";
        await setup.ExecuteNonQueryAsync();
        if (queryOnly) { setup.CommandText = "PRAGMA query_only=ON"; await setup.ExecuteNonQueryAsync(); }
        using var tx = connection.BeginTransaction(deferred: true);
        var connector = new ConnectionFactoryConnector(() => throw new Exception("Opened another connection"), DbFlavor.Sqlite, new());
        foreach (string mode in new[] { "sync", "async", "stream" })
            foreach (bool childDistinct in new[] { false, true })
            {
                var query = new SelectQuery<User>().Select(x => x.Name).Select(x => x.Permissions).Distinct();
                // Nested DISTINCT is an explicit operation on that query definition.
                query.Selects.GetAll<SelectRelationship>().Single().SubQuery.Distinct = childDistinct;
                List<User> rows;
                if (mode == "stream") { rows = []; await foreach (var row in query.ExecuteAsyncStream(connector, tx)) rows.Add(row); }
                else rows = (mode == "sync" ? query.Execute(connector, tx) : await query.ExecuteAsync(connector, tx)).GetResultOrThrow();
                if (rows.Count != 3 || rows.Count(r => r.Name == "Ada") != 2)
                    throw new Exception("Relationship keys did not preserve separate same-name parents");
                var read = rows.Single(r => r.Permissions.Any(p => p.Name == "read"));
                if (read.Permissions.Count != (childDistinct ? 1 : 2)
                    || rows.Single(r => r.Name == null).Permissions.Count != 0)
                    throw new Exception("Parent DISTINCT changed child multiplicity or empty collections");
                var page = query.Page(1, 1);
                var counted = await page.ExecuteWithTotalCountAsync(connector, tx);
                if (counted.GetResultOrThrow().Count != 1 || counted.TotalResults != 3)
                    throw new Exception("Distinct paging and SQL count disagree");
                await foreach (var row in query.ExecuteAsyncStream(connector, tx)) break;
                if (query.Execute(connector, tx).GetResultOrThrow().Count != 1)
                    throw new Exception("Early stop damaged connection reuse");
            }
        setup.Transaction = tx;
        setup.CommandText = "SELECT COUNT(*) FROM sqlite_temp_master";
        if (Convert.ToInt64(await setup.ExecuteScalarAsync()) != 0) throw new Exception("SELECT created TEMP resources");
        setup.CommandText = "PRAGMA query_only";
        if (Convert.ToInt32(await setup.ExecuteScalarAsync()) != (queryOnly ? 1 : 0)) throw new Exception("Changed caller pragma");
        tx.Rollback();
        var owned = new ConnectionFactoryConnector(() =>
        {
            var c = new SqliteConnection(source); c.Open();
            if (queryOnly) { using var pragma = c.CreateCommand(); pragma.CommandText = "PRAGMA query_only=ON"; pragma.ExecuteNonQuery(); }
            return c;
        }, DbFlavor.Sqlite, new());
        var ownQuery = new SelectQuery<User>().Select(x => x.Name).Select(x => x.Permissions).Distinct();
        if (ownQuery.Execute(owned).GetResultOrThrow().Count != 3
            || (await ownQuery.ExecuteAsync(owned)).GetResultOrThrow().Count != 3)
            throw new Exception("Owned connection DISTINCT changed parent rows");
        using var cancel = new CancellationTokenSource();
        try
        {
            await foreach (var row in ownQuery.ExecuteAsyncStream(owned, cancellationToken: cancel.Token)) cancel.Cancel();
            throw new Exception("Cancellation was ignored");
        }
        catch (OperationCanceledException error) when (error.CancellationToken == cancel.Token) { }
        if (ownQuery.Execute(owned).GetResultOrThrow().Count != 3) throw new Exception("Cancellation damaged subsequent execution");
    }

    [SqlTable("distinct_users")]
    private sealed class User
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
        [SqlOneToMany(nameof(Permission.UserId))] public List<Permission> Permissions { get; set; } = [];
    }
    [SqlTable("distinct_permissions")]
    private sealed class Permission
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; }
    }
}
