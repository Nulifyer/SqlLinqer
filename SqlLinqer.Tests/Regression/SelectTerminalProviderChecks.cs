using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.Regression;

// Runs the filtered exists terminal and row locks on every live provider.
internal static class SelectTerminalProviderChecks
{
    internal static void Run(BaseConnector db, Action<string, Action> test)
    {
        foreach (bool asynchronous in new[] { false, true })
            test($"select: exists follows the filter and the caller transaction, async={asynchronous}", () => Exists(db, asynchronous).GetAwaiter().GetResult());
        test("select: row locks skip, fail fast, and claim queued rows", () => RowLocks(db));
        test("select: LIKE and NOT LIKE ignore case", () => LikeIgnoresCase(db));
        test("select: a filtering subselect may order by a column it does not select", () => SubselectOrdering(db));
        test("select: whole-word matching finds a word at any position", () => WholeWords(db));
    }

    private static string TableName(BaseConnector db)
        => (string.IsNullOrEmpty(db.Options.DefaultSchema) ? "" : db.Options.DefaultSchema.DbWrap(db.DbFlavor) + ".") + "select_jobs".DbWrap(db.DbFlavor);

    private static void Create(BaseConnector db)
    {
        string Q(string value) => value.DbWrap(db.DbFlavor);
        db.BeginSql($"CREATE TABLE {TableName(db)} ({Q("id")} INTEGER PRIMARY KEY, {Q("state")} VARCHAR(16) NOT NULL, {Q("owner")} VARCHAR(16) NULL, {Q("parent_id")} INTEGER NULL, {Q("created")} {(db.DbFlavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? "DATETIME2" : "TIMESTAMP")} NULL)").Execute().EnsureSuccess();
        db.BeginInsert<Job>().InsertAuto(new[]
        {
            new Job { Id = 1, State = "queued" }, new Job { Id = 2, State = "queued", ParentId = 3 }, new Job { Id = 3, State = "done", Owner = "ada" },
        }).Execute().EnsureSuccess();
    }

    private static void Drop(BaseConnector db) => db.BeginSql($"DROP TABLE IF EXISTS {TableName(db)}").Execute();

    private static async Task Exists(BaseConnector db, bool asynchronous)
    {
        try
        {
            Create(db);
            async Task<bool> Any(SelectQuery<Job> query, System.Data.Common.DbTransaction transaction = null)
                => (asynchronous ? await query.ExecuteExistsAsync(transaction) : query.ExecuteExists(transaction)).GetResultOrThrow();
            Check(await Any(db.BeginSelect<Job>().Where(x => x.State, "done", SqlOp.EQ).Where(x => x.Owner, "ada", SqlOp.EQ)), "A matching row was not found");
            Check(!await Any(db.BeginSelect<Job>().Where(x => x.State, "done", SqlOp.EQ).Where(x => x.Owner, "bob", SqlOp.EQ)), "A filter was ignored");
            Check(await Any(db.BeginSelect<Job>()), "An unfiltered check missed existing rows");
            // Selection, ordering, and paging do not change the answer.
            Check(await Any(db.BeginSelect<Job>().SelectRootColumns().Where(x => x.Id, 1, SqlOp.EQ).OrderBy(x => x.Id, SqlDir.DESC).Page(5, 10)), "Paging changed the existence check");

            using var connection = db.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            db.BeginUpdate<Job>().Update(x => x.Owner, "bob").Where(x => x.Id, 3, SqlOp.EQ).Execute(transaction).EnsureSuccess();
            Check(await Any(db.BeginSelect<Job>().Where(x => x.Owner, "bob", SqlOp.EQ), transaction), "The check did not run inside the caller transaction");
            transaction.Rollback();
            Check(!await Any(db.BeginSelect<Job>().Where(x => x.Owner, "bob", SqlOp.EQ)), "The caller transaction was committed");
        }
        finally { Drop(db); }
    }

    private static void SubselectOrdering(BaseConnector db)
    {
        try
        {
            Create(db);
            // Ordering by State must not add State to the subselect, which may return only one column.
            var newestQueued = db.BeginSelect<Job>().Select(x => x.Id).Where(x => x.State, "queued", SqlOp.EQ)
                .OrderBy(x => x.State, SqlDir.ASC).ThenBy(x => x.Id, SqlDir.DESC);
            // MySQL does not accept LIMIT in a quantified subselect, so its subselect keeps both queued rows.
            if (db.DbFlavor != DbFlavor.MySql) newestQueued.Top(1);
            var ids = db.BeginSelect<Job>().Select(x => x.Id).Where(x => x.Id, newestQueued, SqlArrayOp.IN).OrderBy(x => x.Id, SqlDir.ASC)
                .Execute().GetResultOrThrow().Select(x => x.Id).ToList();
            Check(ids.SequenceEqual(db.DbFlavor == DbFlavor.MySql ? new[] { 1, 2 } : new[] { 2 }), "The subselect did not filter by its ordered, limited keys: " + string.Join(",", ids));
        }
        finally { Drop(db); }
    }

    private static void LikeIgnoresCase(BaseConnector db)
    {
        try
        {
            Create(db);
            List<int> Ids(SelectQuery<Job> query) => query.Select(x => x.Id).OrderBy(x => x.Id, SqlDir.ASC).Execute().GetResultOrThrow().Select(x => x.Id).ToList();
            Check(Ids(db.BeginSelect<Job>().Where(x => x.State, "QUEUED", SqlOp.LIKE)).SequenceEqual(new[] { 1, 2 }), "LIKE did not ignore case for an exact pattern");
            Check(Ids(db.BeginSelect<Job>().Where(x => x.State, "%UeU%", SqlOp.LIKE)).SequenceEqual(new[] { 1, 2 }), "LIKE did not ignore case with wildcards");
            Check(Ids(db.BeginSelect<Job>().Where(x => x.State, "Q%", SqlOp.NOTLIKE)).SequenceEqual(new[] { 3 }), "NOT LIKE did not ignore case");
            Check(Ids(db.BeginSelect<Job>().Where(x => x.State, "DONE", SqlOp.LIKEWORD)).SequenceEqual(new[] { 3 }), "Whole-word LIKE did not ignore case");
            Check(Ids(db.BeginSelect<Job>().Where(x => x.State, "QUEUED", SqlOp.EQ)).Count is 0 or 2, "Equality failed");
        }
        finally { Drop(db); }
    }

    private static void WholeWords(BaseConnector db)
    {
        string Q(string value) => value.DbWrap(db.DbFlavor);
        try
        {
            db.BeginSql($"CREATE TABLE {TableName(db)} ({Q("id")} INTEGER PRIMARY KEY, {Q("state")} VARCHAR(16) NOT NULL, {Q("owner")} VARCHAR(40) NULL, {Q("parent_id")} INTEGER NULL, {Q("created")} {(db.DbFlavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? "DATETIME2" : "TIMESTAMP")} NULL)").Execute().EnsureSuccess();
            string[] owners = { "red", "a red apple", "Red.apple", "apple,RED", "redapple", "tired", "re d", "r3d apple", null, "100% red", "(red)" };
            db.BeginInsert<Job>().InsertAuto(owners.Select((owner, index) => new Job { Id = index + 1, State = "x", Owner = owner })).Execute().EnsureSuccess();
            List<int> Ids(string pattern, SqlOp op) => db.BeginSelect<Job>().Select(x => x.Id).Where(x => x.Owner, pattern, op)
                .OrderBy(x => x.Id, SqlDir.ASC).Execute().GetResultOrThrow().Select(x => x.Id).ToList();
            void Same(List<int> actual, int[] expected, string message)
                => Check(actual.SequenceEqual(expected), $"{message}: {string.Join(",", actual)}");
            // The word may stand alone, lead, trail, or sit between other characters that are not letters or digits.
            Same(Ids("red", SqlOp.LIKEWORD), new[] { 1, 2, 3, 4, 10, 11 }, "A whole word was not found at every position");
            Same(Ids("red", SqlOp.NOTLIKEWORD), new[] { 5, 6, 7, 8 }, "NOT whole-word did not return the other non-null rows");
            Same(Ids("red", SqlOp.NOTLIKEWORDORNULL), new[] { 5, 6, 7, 8, 9 }, "NOT whole-word OR NULL did not add the null row");
            // LIKE wildcards keep their meaning inside the word.
            Same(Ids("r_d", SqlOp.LIKEWORD), new[] { 1, 2, 3, 4, 8, 10, 11 }, "A single-character wildcard changed inside a whole word");
            // Characters that are special in a regular expression match literally.
            Same(Ids("(red)", SqlOp.LIKEWORD), new[] { 11 }, "Punctuation in a whole word did not match literally");
            Same(Ids("red.apple", SqlOp.LIKEWORD), new[] { 3 }, "A period in a whole word matched as a wildcard");
            Check(db.BeginUpdate<Job>().Update(x => x.State, "y").Where(x => x.Owner, "red", SqlOp.LIKEWORD).Execute().GetResultOrThrow() == 6,
                "An update filtered by a whole word changed a different number of rows");
        }
        finally { Drop(db); }
    }

    private static void RowLocks(BaseConnector db)
    {
        SelectQuery<Job> Next(SqlRowLock wait) => db.BeginSelect<Job>().SelectRootColumns()
            .Where(x => x.State, "queued", SqlOp.EQ).OrderBy(x => x.Id, SqlDir.ASC).Top(1).ForUpdate(wait);
        if (db.DbFlavor is DbFlavor.Sqlite or DbFlavor.DuckDb)
        {
            try { Next(SqlRowLock.WAIT).Execute(); }
            catch (NotSupportedException) { return; }
            throw new Exception("An unsupported row lock was not rejected");
        }
        try
        {
            Create(db);
            if (db.DbFlavor == DbFlavor.PostgreSql) db.BeginSql($"ANALYZE {TableName(db)}").WithoutTransaction().Execute().EnsureSuccess();
            using var firstConnection = db.CreateConnection();
            using var secondConnection = db.CreateConnection();
            firstConnection.Open();
            secondConnection.Open();
            using (var first = firstConnection.BeginTransaction())
            using (var second = secondConnection.BeginTransaction())
            {
                Check(Next(SqlRowLock.WAIT).Execute(first).GetResultOrThrow().Single().Id == 1, "The first transaction did not lock the first queued row");
                Check(Next(SqlRowLock.SKIPLOCKED).Execute(second).GetResultOrThrow().Single().Id == 2, "A locked row was not skipped");
                var blocked = db.BeginSelect<Job>().SelectRootColumns().Where(x => x.Id, 1, SqlOp.EQ).ForUpdate(SqlRowLock.NOWAIT).Execute(second);
                Check(blocked.State == ResponseState.Error, "NOWAIT did not fail on a locked row");
                second.Rollback();
                first.Rollback();
            }

            // A filter through a relationship joins another table. Only the root table's rows are locked.
            using (var first = firstConnection.BeginTransaction())
            using (var second = secondConnection.BeginTransaction())
            {
                SelectQuery<Job> Child(SqlRowLock wait) => db.BeginSelect<Job>().SelectRootColumns()
                    .Where(x => x.Parent.State, "done", SqlOp.EQ).OrderBy(x => x.Id, SqlDir.ASC).ForUpdate(wait);
                Check(Child(SqlRowLock.WAIT).Execute(first).GetResultOrThrow().Single().Id == 2, "A joined locking select did not return the child row");
                Check(Child(SqlRowLock.SKIPLOCKED).Execute(second).GetResultOrThrow().Count == 0, "A joined locking select did not lock the root row");
                if (db.DbFlavor == DbFlavor.PostgreSql)
                    Check(db.BeginSelect<Job>().SelectRootColumns().Where(x => x.Id, 3, SqlOp.EQ).ForUpdate(SqlRowLock.NOWAIT)
                        .Execute(second).GetResultOrThrow().Count == 1, "A joined locking select locked the joined table's row");
                second.Rollback();
                first.Rollback();
            }

            // MySQL cannot update a table it also selects from in a subquery, so it claims in two statements.
            using var claimConnection = db.CreateConnection();
            claimConnection.Open();
            using var claim = claimConnection.BeginTransaction();
            if (db.DbFlavor == DbFlavor.MySql)
            {
                int id = Next(SqlRowLock.SKIPLOCKED).Execute(claim).GetResultOrThrow().Single().Id;
                Check(db.BeginUpdate<Job>().Update(x => x.State, "running").Update(x => x.Owner, "worker")
                    .Where(x => x.Id, id, SqlOp.EQ).Execute(claim).GetResultOrThrow() == 1, "The locked row was not updated");
            }
            else
            {
                if (db.DbFlavor == DbFlavor.PostgreSql)
                {
                    // Force the plan that re-runs the subselect for every outer row. Each re-run skips rows this
                    // statement already updated, so a subselect that is not evaluated once claims more than its limit.
                    foreach (string setting in new[] { "hashjoin", "mergejoin", "indexscan", "bitmapscan", "material", "hashagg", "sort", "memoize" })
                        db.BeginSql($"SET LOCAL enable_{setting}=off").Execute(claim).EnsureSuccess();
                    string plan = string.Join(" ", db.BeginSql($"EXPLAIN (COSTS OFF) UPDATE {TableName(db)} SET state = 'x' WHERE id IN "
                        + $"(SELECT id FROM {TableName(db)} WHERE state = 'queued' ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED)").ExecuteList<string>(claim).GetResultOrThrow());
                    Check(plan.Contains("Nested Loop Semi Join"), "The planner settings no longer force the plan this check depends on: " + plan);
                }
                var update = db.BeginUpdate<Job, int>().Update(x => x.State, "running").Update(x => x.Owner, "worker");
                // The subselect orders by a column it does not select, as a queue ordered by creation time does.
                // Built from the connector, as the guide shows, so it resolves the same schema as the update.
                var next = db.BeginSelect<Job>().Select(x => x.Id).Where(x => x.State, "queued", SqlOp.EQ)
                    .OrderBy(x => x.State, SqlDir.ASC).ThenBy(x => x.Id, SqlDir.ASC).Top(1).ForUpdate(SqlRowLock.SKIPLOCKED);
                var claimed = update.Where(x => x.Id, next, SqlArrayOp.IN).Execute(claim).EnsureSuccess();
                Check(claimed.Result == 1 && claimed.TouchedPrimaryKeys.Single() == 1,
                    $"The claim update did not claim exactly the first queued row: {claimed.Result} rows, keys {string.Join(",", claimed.TouchedPrimaryKeys)}");
            }
            claim.Commit();
            var rows = db.BeginSelect<Job>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC).Execute().GetResultOrThrow();
            Check(rows[0].State == "running" && rows[0].Owner == "worker" && rows[1].State == "queued", "More or fewer rows were claimed than the first queued row");
        }
        finally { Drop(db); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    [SqlTable("select_jobs")]
    public sealed class Job
    {
        [SqlPrimaryKey(false), SqlColumn("id")] public int Id { get; set; }
        [SqlColumn("state")] public string State { get; set; }
        [SqlColumn("owner")] public string Owner { get; set; }
        [SqlColumn("parent_id")] public int? ParentId { get; set; }
        [SqlColumn("created"), SqlAutoInsertExclude, SqlAutoSelectExclude] public DateTime? Created { get; set; }
        [SqlOneToOne(nameof(ParentId)), SqlAutoSelectExclude] public Job Parent { get; set; }
    }
}
