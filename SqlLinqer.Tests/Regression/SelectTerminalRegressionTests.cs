using System.Text.RegularExpressions;
using SqlLinqer;
using SqlLinqer.Queries.Typed;
using Job = SqlLinqer.Tests.Regression.SelectTerminalProviderChecks.Job;

namespace SqlLinqer.Tests.Regression;

internal static class SelectTerminalRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        string Clean(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();
        SelectQuery<Job> Next(SqlRowLock wait) => new SelectQuery<Job>().Select(x => x.Id)
            .Where(x => x.State, "queued", SqlOp.EQ).OrderBy(x => x.Id, SqlDir.ASC).Top(1).ForUpdate(wait);

        foreach (var flavor in Enum.GetValues<DbFlavor>())
            test($"exists renders one filtered check without selection, order, or paging for {flavor}", () =>
            {
                var query = new SelectQuery<Job>().SelectRootColumns().Where(x => x.State, "done", SqlOp.EQ)
                    .OrderBy(x => x.Id, SqlDir.DESC).Page(2, 10).ForUpdate();
                var rendered = query.Capture().RenderForExists(flavor);
                string sql = Clean(rendered.Text);
                Check(sql.StartsWith("SELECT CASE WHEN EXISTS (SELECT 1 FROM ") && sql.EndsWith(") THEN 1 ELSE 0 END"), sql);
                Check(sql.Contains("WHERE") && !sql.Contains("ORDER BY") && !sql.Contains("LIMIT") && !sql.Contains("OFFSET")
                    && !sql.Contains("FOR UPDATE") && !sql.Contains("UPDLOCK"), sql);
                Check(rendered.Parameters.Count == 1 && rendered.CommandRole == "Exists", "Exists bindings or role changed");
                if (flavor != DbFlavor.Sqlite && flavor != DbFlavor.DuckDb)
                    Check(Clean(query.Render(flavor).Text).Contains(flavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? "UPDLOCK" : "FOR UPDATE"),
                        "Rendering the check changed the original query");
            });
        test("exists runs as a read without an owned transaction", () =>
        {
            foreach (bool found in new[] { true, false })
            {
                var database = new FakeDatabase { ScalarFactory = () => found ? 1 : 0 };
                var response = new SelectQuery<Job>().Where(x => x.Id, 1, SqlOp.EQ).ExecuteExists(new FakeConnector(database));
                Check(response.State == ResponseState.Valid && response.Result == found, "Exists result changed");
                Check(database.Connections.Sum(c => c.Transactions.Count) == 0 && database.Executed.Count == 1, "Exists opened a transaction or ran twice");
            }
        });
        test("row locks render FOR UPDATE on PostgreSQL and MySQL", () =>
        {
            foreach (var flavor in new[] { DbFlavor.PostgreSql, DbFlavor.MySql })
            {
                Check(Clean(Next(SqlRowLock.WAIT).Render(flavor).Text).EndsWith("LIMIT 1 OFFSET 0 FOR UPDATE"), Clean(Next(SqlRowLock.WAIT).Render(flavor).Text));
                Check(Clean(Next(SqlRowLock.SKIPLOCKED).Render(flavor).Text).EndsWith("FOR UPDATE SKIP LOCKED"), "Skip locked changed");
                Check(Clean(Next(SqlRowLock.NOWAIT).Render(flavor).Text).EndsWith("FOR UPDATE NOWAIT"), "No wait changed");
                Check(!Clean(Next(SqlRowLock.WAIT).RenderForCount(new SqlLinqer.Components.Render.ParameterCollection(flavor), flavor)).Contains("FOR UPDATE"),
                    "A count kept the row lock");
            }
        });
        test("row locks name the root table when the query joins another", () =>
        {
            SelectQuery<Job> Child() => new SelectQuery<Job>().Select(x => x.Id).Where(x => x.Parent.State, "done", SqlOp.EQ).ForUpdate(SqlRowLock.SKIPLOCKED);
            Check(Regex.IsMatch(Clean(Child().Render(DbFlavor.PostgreSql).Text), "LEFT JOIN .* FOR UPDATE OF \"[^\"]+\" SKIP LOCKED$"), Clean(Child().Render(DbFlavor.PostgreSql).Text));
            Check(Regex.IsMatch(Clean(Child().Render(DbFlavor.MySql).Text), "LEFT JOIN .* FOR UPDATE OF `[^`]+` SKIP LOCKED$"), Clean(Child().Render(DbFlavor.MySql).Text));
            Check(Regex.Matches(Clean(Child().Render(DbFlavor.SqlServer).Text), "UPDLOCK").Count == 1, "A joined table received lock hints");
        });
        test("row locks render table hints on SQL Server and Azure SQL", () =>
        {
            foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
            {
                Check(Clean(Next(SqlRowLock.WAIT).Render(flavor).Text).Contains("] WITH (UPDLOCK, ROWLOCK) WHERE"), Clean(Next(SqlRowLock.WAIT).Render(flavor).Text));
                Check(Clean(Next(SqlRowLock.SKIPLOCKED).Render(flavor).Text).Contains("WITH (UPDLOCK, ROWLOCK, READPAST)"), "Skip locked changed");
                Check(Clean(Next(SqlRowLock.NOWAIT).Render(flavor).Text).Contains("WITH (UPDLOCK, ROWLOCK, NOWAIT)"), "No wait changed");
                Check(!Clean(Next(SqlRowLock.WAIT).Render(flavor).Text).Contains("FOR UPDATE"), "SQL Server rendered FOR UPDATE");
            }
        });
        foreach (var flavor in Enum.GetValues<DbFlavor>())
            test($"LIKE ignores case for {flavor}", () =>
            {
                bool ilike = flavor is DbFlavor.PostgreSql or DbFlavor.DuckDb;
                string like = Clean(new SelectQuery<Job>().Select(x => x.Id).Where(x => x.Owner, "a%", SqlOp.LIKE).Render(flavor).Text);
                string notLike = Clean(new SelectQuery<Job>().Select(x => x.Id).Where(x => x.Owner, "a%", SqlOp.NOTLIKE).Render(flavor).Text);
                string columns = Clean(new SelectQuery<Job>().Select(x => x.Id).Where(x => x.Owner, x => x.State, SqlOp.LIKE).Render(flavor).Text);
                string equal = Clean(new SelectQuery<Job>().Select(x => x.Id).Where(x => x.Owner, "a", SqlOp.EQ).Render(flavor).Text);
                Check(Regex.IsMatch(like, ilike ? " ILIKE " : " LIKE ") && like.Contains(" ILIKE ") == ilike, like);
                Check(Regex.IsMatch(notLike, ilike ? " NOT ILIKE " : " NOT LIKE ") && notLike.Contains(" ILIKE ") == ilike, notLike);
                Check(columns.Contains(" ILIKE ") == ilike && columns.Contains("LIKE"), columns);
                Check(!equal.Contains("LIKE"), "Another operator changed");
            });
        foreach (var flavor in Enum.GetValues<DbFlavor>())
            test($"a filtering subselect omits an order-only column for {flavor}", () =>
            {
                SelectQuery<Job> Ordered() => new SelectQuery<Job>().Select(x => x.Id).Where(x => x.State, "queued", SqlOp.EQ)
                    .OrderBy(x => x.Created, SqlDir.ASC).ThenBy(x => x.Id, SqlDir.ASC).Top(1);
                string sql = Clean(new SelectQuery<Job>().Select(x => x.Id).Where(x => x.Id, Ordered(), SqlArrayOp.IN).Render(flavor).Text);
                string inner = sql.Substring(sql.IndexOf("WHERE", StringComparison.Ordinal));
                var list = Regex.Match(inner, @"SELECT (?:TOP 1 )?(?<list>.*?) FROM ");
                Check(list.Success && !list.Groups["list"].Value.Contains(','), "The subselect returns more than one column: " + sql);
                Check(Regex.IsMatch(inner, @"ORDER BY [^)]*created[^)]* ASC,"), "The subselect lost its ordering column: " + sql);
                // A select executed on its own keeps its hidden ordering column.
                string alone = Clean(Ordered().Render(flavor).Text);
                Check(Regex.Match(alone, @"SELECT (?:TOP 1 )?(?<list>.*?) FROM ").Groups["list"].Value.Contains(','), "A standalone select changed: " + alone);
            });
        test("row locks are rejected where they cannot apply", () =>
        {
            Throws<NotSupportedException>(() => Next(SqlRowLock.WAIT).Render(DbFlavor.Sqlite));
            Throws<NotSupportedException>(() => Next(SqlRowLock.WAIT).Render(DbFlavor.DuckDb));
            Throws<NotSupportedException>(() => Next(SqlRowLock.WAIT).Distinct().Render(DbFlavor.PostgreSql));
        });
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action run) where T : Exception
    {
        try { run(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
