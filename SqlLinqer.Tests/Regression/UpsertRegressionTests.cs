using System.Text.RegularExpressions;
using SqlLinqer;
using SqlLinqer.Components.Update;
using SqlLinqer.Queries.Typed;
using Lease = SqlLinqer.Tests.Regression.UpsertProviderChecks.Lease;

namespace SqlLinqer.Tests.Regression;

internal static class UpsertRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        string Sql(InsertQuery<Lease> query, DbFlavor flavor) => Regex.Replace(query.Render(flavor).Text, @"\s+", " ").Trim();
        InsertQuery<Lease> Insert() => new InsertQuery<Lease>().InsertAuto(new Lease { DocumentId = 1, Language = "en", Generation = 1, Token = "a", State = "new" });
        InsertQuery<Lease> Upsert() => Insert()
            .OnConflict(x => x.DocumentId, x => x.Language)
            .OnConflictUpdate(x => x.Generation, new BasicMathOperation(BasicMathOperation.MathOp.Add, 1))
            .OnConflictUpdate(x => x.Token)
            .OnConflictUpdate(x => x.State, "current")
            .Output(x => x.Generation);

        test("upsert: PostgreSQL, SQLite, and DuckDB render ON CONFLICT", () =>
        {
            Check(Sql(Upsert(), DbFlavor.PostgreSql) == "INSERT INTO \"upsert_leases\" ( \"document_id\", \"language\", \"generation\", \"token\", \"state\") VALUES (@P1,@P2,@P3,@P4,@P5) "
                + "ON CONFLICT (\"document_id\",\"language\") DO UPDATE SET \"generation\" = \"upsert_leases\".\"generation\" + @P6,\"token\" = EXCLUDED.\"token\",\"state\" = @P7 RETURNING \"generation\"",
                Sql(Upsert(), DbFlavor.PostgreSql));
            foreach (var flavor in new[] { DbFlavor.Sqlite, DbFlavor.DuckDb })
            {
                string sql = Sql(Upsert(), flavor);
                Check(sql.Contains("ON CONFLICT (") && sql.Contains("DO UPDATE SET") && sql.Contains("EXCLUDED.") && sql.Contains("RETURNING"), sql);
            }
            Check(Sql(Insert().OnConflictDoNothing(), DbFlavor.PostgreSql).EndsWith("ON CONFLICT DO NOTHING"), "Do nothing without a target changed");
            Check(Sql(Insert().OnConflict(x => x.DocumentId).OnConflictDoNothing(), DbFlavor.Sqlite).Contains("ON CONFLICT (\"document_id\") DO NOTHING"), "Do nothing with a target changed");
        });
        test("upsert: MySQL renders ON DUPLICATE KEY UPDATE without a target", () =>
        {
            var query = Insert().OnConflict(x => x.DocumentId, x => x.Language)
                .OnConflictUpdate(x => x.Generation, new BasicMathOperation(BasicMathOperation.MathOp.Add, 1)).OnConflictUpdate(x => x.Token);
            Check(Sql(query, DbFlavor.MySql).EndsWith("VALUES (@P1,@P2,@P3,@P4,@P5) ON DUPLICATE KEY UPDATE `upsert_leases`.`generation` = `upsert_leases`.`generation` + @P6,`token` = VALUES(`token`)"),
                Sql(query, DbFlavor.MySql));
            Check(Sql(Insert().OnConflict(x => x.DocumentId).OnConflictDoNothing(), DbFlavor.MySql).EndsWith("ON DUPLICATE KEY UPDATE `document_id` = `document_id`"), "MySQL do nothing changed");
            Throws<NotSupportedException>(() => Upsert().Render(DbFlavor.MySql));
        });
        foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
            test($"upsert: {flavor} renders MERGE with HOLDLOCK", () =>
            {
                Check(Sql(Upsert(), flavor) == "MERGE INTO [upsert_leases] WITH (HOLDLOCK) AS [target] USING (VALUES (@P1,@P2,@P3,@P4,@P5)) AS [source] ([document_id],[language],[generation],[token],[state]) "
                    + "ON [target].[document_id] = [source].[document_id] AND [target].[language] = [source].[language] "
                    + "WHEN MATCHED THEN UPDATE SET [target].[generation] = [target].[generation] + @P6,[target].[token] = [source].[token],[target].[state] = @P7 "
                    + "WHEN NOT MATCHED THEN INSERT ([document_id],[language],[generation],[token],[state]) VALUES ([source].[document_id],[source].[language],[source].[generation],[source].[token],[source].[state]) "
                    + "OUTPUT [Inserted].[generation];", Sql(Upsert(), flavor));
                string skip = Sql(Insert().OnConflict(x => x.DocumentId).OnConflictDoNothing(), flavor);
                Check(!skip.Contains("WHEN MATCHED") && skip.Contains("WHEN NOT MATCHED THEN INSERT") && skip.EndsWith(";"), skip);
                Throws<InvalidOperationException>(() => Insert().OnConflictDoNothing().Render(flavor));
                Throws<NotSupportedException>(() => Upsert().RenderTVP(new SqlLinqer.Components.Render.ParameterCollection(flavor), flavor, "dbo"));
            });
        test("upsert: automatic updates skip the target and explicit assignments win", () =>
        {
            string sql = Sql(Insert().OnConflictUpdateAuto().OnConflict(x => x.DocumentId, x => x.Language).OnConflictUpdate(x => x.State, "kept"), DbFlavor.PostgreSql);
            Check(sql.EndsWith("DO UPDATE SET \"state\" = @P6,\"generation\" = EXCLUDED.\"generation\",\"token\" = EXCLUDED.\"token\""), sql);
        });
        test("upsert: incomplete or contradictory conflict actions are rejected", () =>
        {
            Throws<InvalidOperationException>(() => Insert().OnConflict(x => x.DocumentId).Render(DbFlavor.PostgreSql));
            Throws<InvalidOperationException>(() => Insert().OnConflictUpdate(x => x.Token).Render(DbFlavor.PostgreSql));
            Throws<InvalidOperationException>(() => Insert().OnConflict(x => x.DocumentId).OnConflictUpdate(x => x.Token).OnConflictDoNothing().Render(DbFlavor.PostgreSql));
            var partial = new InsertQuery<PartialLease>().InsertAuto(new PartialLease { DocumentId = 1 }).OnConflict(x => x.DocumentId);
            Throws<InvalidOperationException>(() => partial.OnConflictUpdate(x => x.Token).Render(DbFlavor.PostgreSql));
        });
        foreach (bool asynchronous in new[] { false, true })
            test($"upsert: bound conflict values count toward the parameter limit, async={asynchronous}", () =>
            {
                // Five columns fill the default 2,100 parameter budget at exactly 420 rows. The conflict values need room too.
                var database = new FakeDatabase();
                var connector = new FakeConnector(database);
                var rows = Enumerable.Range(1, 420).Select(i => new Lease { DocumentId = i, Language = "en", Generation = 1, Token = "t", State = "new" }).ToArray();
                var query = new InsertQuery<Lease>().InsertAuto(rows).OnConflict(x => x.DocumentId, x => x.Language)
                    .OnConflictUpdate(x => x.Generation, new BasicMathOperation(BasicMathOperation.MathOp.Add, 1))
                    .OnConflictUpdate(x => x.State, "current");
                var response = asynchronous ? query.ExecuteAsync(connector).GetAwaiter().GetResult() : query.Execute(connector);
                Check(response.State == ResponseState.Valid, "A full upsert batch was rejected: " + response.Error?.Message);
                Check(database.Executed.Count == 2 && database.Executed.All(c => c.Parameters.Count <= 2100),
                    "Batches: " + string.Join(",", database.Executed.Select(c => c.Parameters.Count)));
                Check(database.Executed.Sum(c => c.Parameters.Count) == 420 * 5 + 2 * 2, "Rows or conflict values were lost across batches");
            });
        test("upsert: a captured query keeps its conflict action", () =>
        {
            var query = Upsert();
            var snapshot = query.Capture();
            query.OnConflictDoNothing();
            Check(Regex.Replace(snapshot.Render(DbFlavor.PostgreSql).Text, @"\s+", " ").Contains("DO UPDATE SET"), "A later change altered the captured upsert");
            Check(Sql(Upsert(), DbFlavor.PostgreSql) == Sql(Upsert(), DbFlavor.PostgreSql), "Rendering changed the table alias for a later render");
            Check(!Sql(Insert(), DbFlavor.PostgreSql).Contains("CONFLICT"), "A plain insert gained a conflict clause");
            // The conflict renderer renames the table alias on its own copy, never on the shared model mapping.
            string Select() => new SelectQuery<Lease>().SelectRootColumns().Where(x => x.DocumentId, 1, SqlOp.EQ).Render(DbFlavor.SqlServer).Text;
            string before = Select();
            Upsert().Render(DbFlavor.SqlServer);
            Upsert().Render(DbFlavor.PostgreSql);
            Check(Select() == before, "Rendering an upsert changed the shared table alias");
        });
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action run) where T : Exception
    {
        try { run(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    [SqlLinqer.Modeling.SqlTable("upsert_leases")]
    private sealed class PartialLease
    {
        [SqlLinqer.Modeling.SqlColumn("document_id")] public int DocumentId { get; set; }
        [SqlLinqer.Modeling.SqlColumn("token"), SqlLinqer.Modeling.SqlAutoInsertExclude] public string Token { get; set; }
    }
}
