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
        foreach (bool asynchronous in new[] { false, true })
            test($"upsert: a condition limits which conflicting rows are updated, async={asynchronous}", () => Conditional(db, asynchronous).GetAwaiter().GetResult());
        test("upsert: a named constraint is a conflict target on PostgreSQL only", () => NamedConstraint(db));
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

    private static async Task Conditional(BaseConnector db, bool asynchronous)
    {
        string Q(string value) => value.DbWrap(db.DbFlavor);
        string table = (string.IsNullOrEmpty(db.Options.DefaultSchema) ? "" : Q(db.Options.DefaultSchema) + ".") + Q("upsert_leases");
        List<Lease> Read() => db.BeginSql($"SELECT * FROM {table} ORDER BY {Q("document_id")}, {Q("language")}").ExecuteList<Lease>().GetResultOrThrow();
        try
        {
            db.BeginSql($"CREATE TABLE {table} ({Q("document_id")} INTEGER NOT NULL, {Q("language")} VARCHAR(8) NOT NULL, {Q("generation")} INTEGER NOT NULL, "
                + $"{Q("token")} VARCHAR(40) NULL, {Q("state")} VARCHAR(16) NULL, PRIMARY KEY ({Q("document_id")}, {Q("language")}))").Execute().EnsureSuccess();
            db.BeginInsert<Lease>().InsertAuto(new[]
            {
                new Lease { DocumentId = 1, Language = "en", Generation = 5, Token = "old", State = "open" },
                new Lease { DocumentId = 2, Language = "en", Generation = 5, Token = "old", State = "open" },
                new Lease { DocumentId = 3, Language = "en", Generation = 5, Token = "old", State = "closed" },
            }).Execute().EnsureSuccess();

            // Only a newer generation replaces a stored row. Document 1 is older, document 2 is newer, document 4 is new.
            var newer = db.BeginInsert<Lease>()
                .InsertAuto(new[]
                {
                    new Lease { DocumentId = 1, Language = "en", Generation = 4, Token = "stale", State = "open" },
                    new Lease { DocumentId = 2, Language = "en", Generation = 6, Token = "fresh", State = "open" },
                    new Lease { DocumentId = 4, Language = "en", Generation = 1, Token = "first", State = "open" },
                })
                .OnConflict(x => x.DocumentId, x => x.Language)
                .OnConflictUpdate(x => x.Generation).OnConflictUpdate(x => x.Token)
                .OnConflictWhere(x => x.Generation, SqlOp.LT);
            int affected = (asynchronous ? await newer.ExecuteAsync() : newer.Execute()).GetResultOrThrow();
            var rows = Read();
            Check(rows[0].Generation == 5 && rows[0].Token == "old", "A conflicting row that failed the condition was updated");
            Check(rows[1].Generation == 6 && rows[1].Token == "fresh", "A conflicting row that met the condition was not updated");
            Check(rows.Count == 4 && rows[3].Token == "first", "A new row was not inserted beside conditional updates");
            if (db.DbFlavor != DbFlavor.MySql) Check(affected == 2, $"The affected-row count included a row the condition left unchanged: {affected}");

            // A filter on the existing row combines with the comparison. Document 3 is newer but closed.
            var open = db.BeginInsert<Lease>()
                .InsertAuto(new[]
                {
                    new Lease { DocumentId = 2, Language = "en", Generation = 7, Token = "next", State = "open" },
                    new Lease { DocumentId = 3, Language = "en", Generation = 7, Token = "next", State = "open" },
                })
                .OnConflict(x => x.DocumentId, x => x.Language)
                .OnConflictUpdate(x => x.Generation).OnConflictUpdate(x => x.Token, "claimed")
                .OnConflictWhere(x => x.Generation, SqlOp.LT)
                .OnConflictWhere(w => w.Where(x => x.State, "open", SqlOp.EQ));
            (asynchronous ? await open.ExecuteAsync() : open.Execute()).EnsureSuccess();
            rows = Read();
            Check(rows[1].Generation == 7 && rows[1].Token == "claimed", "A row that met both conditions was not updated");
            Check(rows[2].Generation == 5 && rows[2].Token == "old", "A row that failed the filter on its existing values was updated");

            if (db.DbFlavor != DbFlavor.MySql)
            {
                // A row the condition leaves unchanged is not returned.
                var returned = db.BeginInsert<Lease>()
                    .InsertAuto(new[]
                    {
                        new Lease { DocumentId = 1, Language = "en", Generation = 1, Token = "stale", State = "open" },
                        new Lease { DocumentId = 4, Language = "en", Generation = 2, Token = "second", State = "open" },
                    })
                    .OnConflict(x => x.DocumentId, x => x.Language).OnConflictUpdateAuto().OnConflictWhere(x => x.Generation, SqlOp.LT)
                    .Output(x => x.DocumentId).ExecuteWithOutput<int>().GetResultOrThrow();
                Check(returned.SequenceEqual(new[] { 4 }), "Returned rows included a row the condition left unchanged: " + string.Join(",", returned));
            }
            else
            {
                // MySQL assigns left to right, so only one column that the condition reads can be assigned.
                var twoRead = db.BeginInsert<Lease>().InsertAuto(new Lease { DocumentId = 1, Language = "en", Generation = 9, Token = "t", State = "s" })
                    .OnConflict(x => x.DocumentId, x => x.Language).OnConflictUpdate(x => x.Generation).OnConflictUpdate(x => x.State)
                    .OnConflictWhere(x => x.Generation, SqlOp.LT).OnConflictWhere(w => w.Where(x => x.State, "open", SqlOp.EQ));
                try { twoRead.Render(db); throw new Exception("MySQL accepted a conditional update that assigns two columns its condition reads"); }
                catch (NotSupportedException) { }
                // An operation is made conditional as well.
                db.BeginInsert<Lease>().InsertAuto(new Lease { DocumentId = 1, Language = "en", Generation = 1, Token = "t", State = "open" })
                    .OnConflict(x => x.DocumentId, x => x.Language)
                    .OnConflictUpdate(x => x.Generation, new BasicMathOperation(BasicMathOperation.MathOp.Add, 10))
                    .OnConflictUpdate(x => x.Token, new SetIfNullOperation("unused"))
                    .OnConflictWhere(w => w.Where(x => x.Token, "missing", SqlOp.EQ)).Execute().EnsureSuccess();
                Check(Read()[0].Generation == 5, "A conditional operation ran although its condition failed");
            }

            var skip = db.BeginInsert<Lease>().InsertAuto(new Lease { DocumentId = 1, Language = "en", Generation = 1 })
                .OnConflict(x => x.DocumentId, x => x.Language).OnConflictDoNothing().OnConflictWhere(x => x.Generation, SqlOp.LT);
            try { skip.Render(db); throw new Exception("A condition on a skipped conflict was accepted"); }
            catch (InvalidOperationException) { }
        }
        finally { db.BeginSql($"DROP TABLE IF EXISTS {table}").Execute(); }
    }

    private static void NamedConstraint(BaseConnector db)
    {
        string Q(string value) => value.DbWrap(db.DbFlavor);
        string table = (string.IsNullOrEmpty(db.Options.DefaultSchema) ? "" : Q(db.Options.DefaultSchema) + ".") + Q("upsert_leases");
        var upsert = db.BeginInsert<Lease>().InsertAuto(new Lease { DocumentId = 1, Language = "en", Generation = 2, Token = "b", State = "new" })
            .OnConflictConstraint("upsert_leases_key").OnConflictUpdate(x => x.Generation).OnConflictUpdate(x => x.Token);
        if (db.DbFlavor is not (DbFlavor.PostgreSql or DbFlavor.MySql))
        {
            try { upsert.Render(db); }
            catch (NotSupportedException) { return; }
            throw new Exception("A named constraint target was accepted on a database that cannot use one");
        }
        try
        {
            db.BeginSql($"CREATE TABLE {table} ({Q("document_id")} INTEGER NOT NULL, {Q("language")} VARCHAR(8) NOT NULL, {Q("generation")} INTEGER NOT NULL, "
                + $"{Q("token")} VARCHAR(40) NULL, {Q("state")} VARCHAR(16) NULL, CONSTRAINT {Q("upsert_leases_key")} PRIMARY KEY ({Q("document_id")}, {Q("language")}))").Execute().EnsureSuccess();
            db.BeginInsert<Lease>().InsertAuto(new Lease { DocumentId = 1, Language = "en", Generation = 1, Token = "a", State = "new" }).Execute().EnsureSuccess();
            upsert.Execute().EnsureSuccess();
            var row = db.BeginSql($"SELECT * FROM {table}").ExecuteList<Lease>().GetResultOrThrow().Single();
            Check(row.Generation == 2 && row.Token == "b" && row.State == "new", "A conflict on a named constraint did not update the row");
            // Key members named later replace the constraint.
            Check(!upsert.OnConflict(x => x.DocumentId, x => x.Language).Render(db).Text.Contains("CONSTRAINT"), "Key members did not replace a named constraint");
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

            // A row type that the output cannot fill is an error, and the write it belonged to is rolled back.
            var unmapped = db.BeginUpdate<Lease>().Update(x => x.Generation, 99).Where(x => x.DocumentId, 1, SqlOp.EQ).Output(x => x.DocumentId);
            var failed = asynchronous ? await unmapped.ExecuteWithOutputAsync<LeaseChange>() : unmapped.ExecuteWithOutput<LeaseChange>();
            Check(failed.State == ResponseState.Error, "An output row missing a mapped column was reported as success");
            Check(db.BeginSql($"SELECT {Q("generation")} FROM {table} WHERE {Q("document_id")} = @id", new { id = 1 }).ExecuteScalar<int>().GetResultOrThrow() == 2,
                "A write whose output could not be mapped was committed");

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
