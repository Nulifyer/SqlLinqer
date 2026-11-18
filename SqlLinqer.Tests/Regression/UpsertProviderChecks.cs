using SqlLinqer.Components.Update;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.Regression;

// Runs insert conflict actions on every live provider.
internal static class UpsertProviderChecks
{
    internal static void Run(BaseConnector db, Action<string, Action> test)
    {
        foreach (bool asynchronous in new[] { false, true })
            test($"upsert: conflict updates, skipped rows, and returned values, async={asynchronous}", () => CheckProvider(db, asynchronous).GetAwaiter().GetResult());
        if (db.DbFlavor != DbFlavor.MySql)
            foreach (bool asynchronous in new[] { false, true })
                test($"writes: update and delete return typed output rows, async={asynchronous}", () => TypedOutput(db, asynchronous).GetAwaiter().GetResult());
    }

    private static async Task CheckProvider(BaseConnector db, bool asynchronous)
    {
        string Q(string value) => value.DbWrap(db.DbFlavor);
        string table = (string.IsNullOrEmpty(db.Options.DefaultSchema) ? "" : Q(db.Options.DefaultSchema) + ".") + Q("upsert_leases");
        bool returnsRows = db.DbFlavor != DbFlavor.MySql;
        List<Lease> Read() => db.BeginSql($"SELECT * FROM {table} ORDER BY {Q("document_id")}, {Q("language")}").ExecuteList<Lease>().GetResultOrThrow();
        try
        {
            db.BeginSql($"CREATE TABLE {table} ({Q("document_id")} INTEGER NOT NULL, {Q("language")} VARCHAR(8) NOT NULL, {Q("generation")} INTEGER NOT NULL, "
                + $"{Q("token")} VARCHAR(40) NULL, {Q("state")} VARCHAR(16) NULL, PRIMARY KEY ({Q("document_id")}, {Q("language")}))").Execute().EnsureSuccess();

            async Task<int?> Grant(string token)
            {
                var query = db.BeginInsert<Lease>()
                    .InsertAuto(new Lease { DocumentId = 1, Language = "en", Generation = 1, Token = token, State = "new" })
                    .OnConflict(x => x.DocumentId, x => x.Language)
                    .OnConflictUpdate(x => x.Generation, new BasicMathOperation(BasicMathOperation.MathOp.Add, 1))
                    .OnConflictUpdate(x => x.Token)
                    .OnConflictUpdate(x => x.State, "current");
                if (!returnsRows)
                {
                    (asynchronous ? await query.ExecuteAsync() : query.Execute()).EnsureSuccess();
                    return null;
                }
                query.Output(x => x.Generation);
                var returned = (asynchronous ? await query.ExecuteWithOutputAsync<int>() : query.ExecuteWithOutput<int>()).GetResultOrThrow();
                Check(returned.Count == 1, "An upsert did not return exactly one row");
                return returned[0];
            }

            int? first = await Grant("a");
            var row = Read().Single();
            Check(row.Generation == 1 && row.Token == "a" && row.State == "new", "The first upsert did not insert the row as supplied");
            int? second = await Grant("b");
            row = Read().Single();
            Check(row.Generation == 2 && row.Token == "b" && row.State == "current", "A conflict did not apply the operation, inserted value, and constant");
            if (returnsRows) Check(first == 1 && second == 2, "Returned values did not follow the inserted and updated row");

            var batch = db.BeginInsert<Lease>()
                .InsertAuto(new[]
                {
                    new Lease { DocumentId = 1, Language = "en", Generation = 9, Token = "c", State = null },
                    new Lease { DocumentId = 2, Language = "fr", Generation = 1, Token = "d", State = "new" },
                })
                .OnConflict(x => x.DocumentId, x => x.Language)
                .OnConflictUpdateAuto();
            (asynchronous ? await batch.ExecuteAsync() : batch.Execute()).EnsureSuccess();
            var rows = Read();
            Check(rows.Count == 2 && rows[0].Generation == 9 && rows[0].Token == "c" && rows[0].State == null,
                "Automatic conflict updates did not copy every inserted value");
            Check(rows[1].DocumentId == 2 && rows[1].Language == "fr" && rows[1].Token == "d", "A new row in a mixed batch was not inserted");

            var skip = db.BeginInsert<Lease>()
                .InsertAuto(new[]
                {
                    new Lease { DocumentId = 2, Language = "fr", Generation = 5, Token = "ignored", State = "ignored" },
                    new Lease { DocumentId = 3, Language = "de", Generation = 1, Token = "e", State = "new" },
                })
                .OnConflict(x => x.DocumentId, x => x.Language)
                .OnConflictDoNothing();
            int skipped = (asynchronous ? await skip.ExecuteAsync() : skip.Execute()).GetResultOrThrow();
            rows = Read();
            Check(rows.Count == 3 && rows[1].Generation == 1 && rows[1].Token == "d" && rows[2].Token == "e", "Do nothing changed an existing row or dropped a new one");
            if (returnsRows) Check(skipped == 1, "Do nothing counted a skipped row");

            // 420 rows of five columns fill the default parameter budget exactly, before the conflict values are bound.
            var many = Enumerable.Range(100, 420).Select(i => new Lease { DocumentId = i, Language = "en", Generation = 1, Token = "t", State = "new" }).ToArray();
            for (int pass = 1; pass <= 2; pass++)
            {
                var bulk = db.BeginInsert<Lease>().InsertAuto(many).OnConflict(x => x.DocumentId, x => x.Language)
                    .OnConflictUpdate(x => x.Generation, new BasicMathOperation(BasicMathOperation.MathOp.Add, 1)).OnConflictUpdate(x => x.State, "current");
                (asynchronous ? await bulk.ExecuteAsync() : bulk.Execute()).EnsureSuccess();
                var bulkRows = Read().Where(x => x.DocumentId >= 100).ToList();
                Check(bulkRows.Count == 420 && bulkRows.All(x => x.Generation == pass && x.State == (pass == 1 ? "new" : "current")),
                    $"A full upsert batch did not insert, then update, every row on pass {pass}");
            }
        }
        finally { db.BeginSql($"DROP TABLE IF EXISTS {table}").Execute(); }
    }

    private static async Task TypedOutput(BaseConnector db, bool asynchronous)
    {
        string Q(string value) => value.DbWrap(db.DbFlavor);
        string table = (string.IsNullOrEmpty(db.Options.DefaultSchema) ? "" : Q(db.Options.DefaultSchema) + ".") + Q("upsert_leases");
        try
        {
            db.BeginSql($"CREATE TABLE {table} ({Q("document_id")} INTEGER NOT NULL, {Q("language")} VARCHAR(8) NOT NULL, {Q("generation")} INTEGER NOT NULL, "
                + $"{Q("token")} VARCHAR(40) NULL, {Q("state")} VARCHAR(16) NULL, PRIMARY KEY ({Q("document_id")}, {Q("language")}))").Execute().EnsureSuccess();
            db.BeginInsert<Lease>().InsertAuto(new[]
            {
                new Lease { DocumentId = 1, Language = "en", Generation = 1, Token = "a" },
                new Lease { DocumentId = 2, Language = "en", Generation = 5, Token = "b" },
            }).Execute().EnsureSuccess();

            var update = db.BeginUpdate<Lease>().Update(x => x.Generation, new BasicMathOperation(BasicMathOperation.MathOp.Add, 1))
                .Where(x => x.Language, "en", SqlOp.EQ).Output(x => x.DocumentId).Output(x => x.Generation);
            var updated = (asynchronous ? await update.ExecuteWithOutputAsync<LeaseChange>() : update.ExecuteWithOutput<LeaseChange>()).GetResultOrThrow();
            Check(updated.OrderBy(x => x.DocumentId).Select(x => x.Generation).SequenceEqual(new[] { 2, 6 }), "An update did not return its changed rows as models");

            var delete = db.BeginDelete<Lease>().Where(x => x.DocumentId, 2, SqlOp.EQ).Output(x => x.Token);
            var deleted = (asynchronous ? await delete.ExecuteWithOutputAsync<string>() : delete.ExecuteWithOutput<string>()).GetResultOrThrow();
            Check(deleted.SequenceEqual(new[] { "b" }), "A delete did not return its removed value as a scalar list");
        }
        finally { db.BeginSql($"DROP TABLE IF EXISTS {table}").Execute(); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    // A typed output row declares only the columns that Output selects.
    private sealed class LeaseChange
    {
        [SqlColumn("document_id")] public int DocumentId { get; set; }
        [SqlColumn("generation")] public int Generation { get; set; }
    }

    [SqlTable("upsert_leases")]
    public sealed class Lease
    {
        [SqlColumn("document_id")] public int DocumentId { get; set; }
        [SqlColumn("language")] public string Language { get; set; }
        [SqlColumn("generation")] public int Generation { get; set; }
        [SqlColumn("token")] public string Token { get; set; }
        [SqlColumn("state")] public string State { get; set; }
    }
}
