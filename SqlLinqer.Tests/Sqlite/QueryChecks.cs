using System.Data;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Typed;
using static SqlLinqer.Tests.Sqlite.CheckAssertions;

namespace SqlLinqer.Tests.Sqlite;

internal static class QueryChecks
{
    internal static void Run(BaseConnector connector, SqliteConnection keeper, Item item, string label, Action<string, Action> Test)
    {
        void Run(string sql) => Result(connector.ExecuteNonQuery(new RenderedQuery(sql)));
        Test($"{label}: root Top Page Limit and total count", () =>
        {
            Expect(Result(new SelectQuery<Item>().SelectRootColumns().Top(2).Execute(connector)).Count == 2);
            var query = new SelectQuery<Item>().SelectRootColumns().Page(2, 1);
            Expect(Result(query.Execute(connector)).Single().Id == 2);
            var counted = query.ExecuteWithTotalCount(connector);
            Expect(Result(counted).Single().Id == 2 && counted.TotalResults == 3);
            Expect(Result(new SelectQuery<Item>().SelectRootColumns().Limit(1, 2).Execute(connector)).Single().Id == 3);
        });
        Test($"{label}: generated SELECT scalars and page counts coexist with an active reader", () =>
        {
            using var command = keeper.CreateCommand();
            command.CommandText = "SELECT Id FROM items ORDER BY Id";
            using var reader = command.ExecuteReader();
            Expect(reader.Read()); // Retain a real read lock while other connections count.
            var query = new SelectQuery<Item>().Select(x => x.Id).Page(1, 1);
            var count = query.Capture().RenderForCount(DbFlavor.Sqlite);
            Expect(Result(connector.ExecuteScalar<long>(count)) == 3);
            Expect(Result(connector.ExecuteScalarAsync<long>(count).GetAwaiter().GetResult()) == 3);
            Expect(Result(connector.ExecuteScalar<int>(query.Render(DbFlavor.Sqlite))) == 1);
            Expect(query.ExecuteWithTotalCount(connector).EnsureSuccess().TotalResults == 3);
            Expect(query.ExecuteWithTotalCountAsync(connector).GetAwaiter().GetResult().EnsureSuccess().TotalResults == 3);
        });
        Test($"{label}: raw scalar write batches still roll back on failure", () =>
        {
            foreach (bool asynchronous in new[] { false, true })
            {
                var raw = new RenderedQuery("INSERT INTO categories VALUES (77,'scalar batch'); SELECT missing_column FROM categories");
                var result = asynchronous ? connector.ExecuteScalarAsync<int>(raw).GetAwaiter().GetResult() : connector.ExecuteScalar<int>(raw);
                Expect(result.State == ResponseState.Error);
                Expect(Result(connector.ExecuteScalar<int>(new RenderedQuery("SELECT COUNT(*) FROM categories WHERE Id=77"))) == 0);
            }
        });
        Test($"{label}: nested objects arrays and missing relationships", () =>
        {
            var rows = Result(new SelectQuery<Item>().SelectAuto().OrderBy(x => x.Id, SqlDir.ASC).Execute(connector));
            Expect(rows[0].Category.Name == "group" && rows[0].Children.Count == 3);
            Expect(rows[0].Children.Single(x => x.Name == "z").Category.Name == "group");
            Expect(rows[0].Children.Single(x => x.Name == "a").Category == null);
            Expect(rows[2].Category == null && rows[2].Children.Count == 0);
        });
        Test($"{label}: nested pagination orders and limits before JSON aggregation", () =>
        {
            var query = new NestedPageQuery();
            var rows = Result(connector.ExecuteReaderModel<Item>(query.Render(DbFlavor.Sqlite)));
            Expect(rows.Single(x => x.Id == 1).Children.Select(x => x.Name).SequenceEqual(["m"]));
            Expect(rows.Single(x => x.Id == 3).Children.Count == 0);
        });
        Test($"{label}: empty root and null predicates", () =>
        {
            Expect(Result(new SelectQuery<Item>().SelectAuto().Where(x => x.Id, -1, SqlOp.EQ).Execute(connector)).Count == 0);
            Expect(Result(new SelectQuery<Item>().SelectRootColumns().Where(x => x.CategoryId, (int?)null, SqlOp.EQ).Execute(connector)).Single().Id == 3);
        });
        Test($"{label}: LIKE uses explicit backslash escaping", () =>
        {
            string pattern = item.Name.EscapeForSqlLike(DbFlavor.Sqlite);
            Expect(Result(new SelectQuery<Item>().Select(x => x.Id).Where(x => x.Name, pattern, SqlOp.LIKE).Execute(connector)).Single().Id == 1);
            Expect(Result(new SelectQuery<Item>().Select(x => x.Id).Where(x => x.Name, pattern, SqlOp.NOTLIKE).Execute(connector)).Count == 2);
        });
        Test($"{label}: word LIKE uses native ASCII boundaries escaping wildcard patterns and nulls", () =>
        {
            using var transaction = keeper.BeginTransaction();
            Result(connector.ExecuteNonQuery(new RenderedQuery("CREATE TABLE words (Id INTEGER PRIMARY KEY, Name TEXT)"), transaction));
            string[] words = ["word", "WORD", "a word!", "sword", "word2", "_word_", "[word]", "éwordé", "word-word", "", null,
                "w%rd", "w_rd", "w*rd", "w?rd", "w[rd", "w\\rd", "x word y", "word\nend"];
            Result(new InsertQuery<WordRow>().InsertAuto(words.Select((word, index) => new WordRow { Id = index + 1, Name = word }).ToArray()).Execute(connector, transaction));
            int[] Match(string pattern, SqlOp op = SqlOp.LIKEWORD) => Result(new SelectQuery<WordRow>().Select(x => x.Id)
                .Where(x => x.Name, pattern, op).Execute(connector, transaction)).Select(x => x.Id).Order().ToArray();
            Expect(Match("word").SequenceEqual([1, 2, 3, 8, 9, 18, 19]));
            Expect(Match("word", SqlOp.NOTLIKEWORD).SequenceEqual([4, 5, 6, 7, 10, 12, 13, 14, 15, 16, 17]));
            foreach (int id in new[] { 12, 13, 14, 15, 16, 17 })
                Expect(Match(words[id - 1].EscapeForSqlLike(DbFlavor.Sqlite)).SequenceEqual([id]));
            Expect(Match("wo%d").SequenceEqual([1, 2, 3, 8, 9, 18, 19]));
            Expect(Match("word\\").Length == 0);
            Expect(Match("word\\", SqlOp.NOTLIKEWORD).Length == 18);
            Expect(Match(null).Length == 0 && Match(null, SqlOp.NOTLIKEWORD).Length == 0);
            Result(connector.ExecuteNonQuery(new RenderedQuery("PRAGMA case_sensitive_like=1"), transaction));
            try { Expect(Match("word").SequenceEqual([1, 3, 8, 9, 18, 19])); }
            finally { Result(connector.ExecuteNonQuery(new RenderedQuery("PRAGMA case_sensitive_like=0"), transaction)); }
            transaction.Rollback();
        });
        Test($"{label}: binary equality uses one BLOB parameter", () =>
        {
            Expect(Result(new SelectQuery<Item>().Select(x => x.Id).Where(x => x.Bytes, item.Bytes, SqlOp.EQ).Execute(connector)).Single().Id == 1);
        });
        Test($"{label}: byte-valued lists remain numeric predicates for nonbinary columns", () =>
        {
            var table = Table.GetCached<Item>();
            var query = new SelectQuery<Item>().Select(x => x.Id)
                .Where(new SqlLinqer.Components.Where.WhereColumnValue(table.Columns.TryGet("Id"),
                    new byte[] { 1, 3 }, SqlOp.EQ, SqlSubQueryOp.ANY));
            Expect(Result(query.Execute(connector)).Select(x => x.Id).Order().SequenceEqual(new[] { 1, 3 }));
            foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.MySql, DbFlavor.PostgreSql })
            {
                var parameters = query.Render(flavor).Parameters.GetAll().ToArray();
                Expect(parameters.Length == 2 && parameters.All(x => x.Value is byte));
            }
        });
        Test($"{label}: aggregate JSON date string and null functions", () =>
        {
            var totals = Result(new SelectQueryInitializer<Item>().BuildQuery(q => new { Count = q.Count(), Big = q.CountBig(), Sum = q.Sum(x => x.Id), Average = (double)q.Avg(x => x.Id) }).Execute(connector)).Single();
            Expect(totals.Count == 3 && totals.Big == 3 && totals.Sum == 6 && totals.Average == 2);
            var values = Result(new SelectQueryInitializer<Item>().BuildQuery(q => new
            {
                Id = q.Select(x => x.Id),
                Year = q.YEAR(x => x.Created),
                Month = q.MONTH(x => x.Created),
                Day = q.DAY(x => x.Created),
                Upper = q.UPPER(x => x.OtherName),
                Right = q.RIGHT(x => x.OtherName, 2),
                Sub = q.SUBSTRING(x => x.OtherName, 2, 2),
                Left = q.LEFT(x => x.OtherName, 2),
                Concat = q.Concat(x => x.OtherName, x => x.Name),
                Joined = q.ConcatWS("|", x => x.OtherName, x => x.Name)
            }).Where(x => x.Id, 1, SqlOp.EQ).Execute(connector)).Single();
            Expect(values.Year == 2026 && values.Month == 10 && values.Day == 20);
            Expect(values.Upper == "OLD" && values.Right == "ld" && values.Sub == "ld");
            Expect(values.Left == "ol");
            Expect(values.Concat == "old" + item.Name && values.Joined == "old|" + item.Name);
        });
        Test($"{label}: grouped aggregate JSON and empty aggregate", () =>
        {
            var rows = Result(new SelectQueryInitializer<Item>().BuildQuery(q => new { Category = q.Select(x => x.CategoryId), Count = q.Count() }).Execute(connector));
            Expect(rows.Count == 2 && rows.Single(x => x.Category == 1).Count == 2 && rows.Single(x => x.Category == null).Count == 1);
            var empty = Result(new SelectQueryInitializer<Item>().BuildQuery(q => new { Count = q.Count(), Max = q.Max(x => x.CategoryId), Sum = (int?)q.Sum(x => x.Id), Average = (double?)q.Avg(x => x.Id) })
                .Where(x => x.Id, -1, SqlOp.EQ).Execute(connector)).Single();
            Expect(empty.Count == 0 && empty.Max == null && empty.Sum == null && empty.Average == null);
        });
        Test($"{label}: grouped DISTINCT and nested counts ignore paging", () =>
        {
            var grouped = new SelectQueryInitializer<Item>().BuildQuery(q => new { Category = q.Select(x => x.CategoryId), Count = q.Count() }).Page(1, 1).ExecuteWithTotalCount(connector);
            Expect(Result(grouped).Count == 1 && grouped.TotalResults == 2);
            var distinct = new SelectQuery<Item>().Select(x => x.CategoryId).Distinct().Page(1, 1).ExecuteWithTotalCount(connector);
            Expect(Result(distinct).Count == 1 && distinct.TotalResults == 2);
            var nested = new SelectQuery<Item>().SelectAuto().Page(1, 1).ExecuteWithTotalCount(connector);
            Expect(Result(nested).Single().Children.Count == 3 && nested.TotalResults == 3);
        });
        Test($"{label}: CASE and nested functions keep scalar SQL expressions", () =>
        {
            var query = new SelectQueryInitializer<Item>().BuildQuery(q => new
            {
                Value = q.Case<string>(c => c.AddCaseValue("yes", w => w.Where(x => x.Enabled, true, SqlOp.EQ)).AddElseValue("no")),
                Upper = q.Concat(x => q.UPPER(x => x.OtherName), x => x.Name),
                Empty = q.RIGHT(x => x.Name, 0)
            }).Where(x => x.Id, 1, SqlOp.EQ);
            var row = Result(query.Execute(connector)).Single();
            Expect(row.Value == "yes" && row.Upper == "OLD" + item.Name && row.Empty == "");
        });
        Test($"{label}: typed nested CASE and functions handle null and column branches", () =>
        {
            var rows = Result(new SelectQueryInitializer<Item>().BuildQuery(q => new
            {
                Id = q.Select(x => x.Id),
                Value = q.UPPER(x => q.Case<string>(c => c
                    .AddCaseColumn(x => x.Category.Name, w => w.Where(x => x.CategoryId, 1, SqlOp.EQ))
                    .AddElseColumn(x => x.Name))),
                Fallback = q.Coalesce(x => x.Category.Name, x => q.LOWER(x => x.Name)),
                Checked = checked((long)q.ABS(x => x.Id))
            }).OrderBy(x => x.Id, SqlDir.ASC).Execute(connector));
            Expect(rows.Count == 3 && rows[0].Value == "GROUP" && rows[1].Value == "GROUP" && rows[2].Value == "THIRD");
            Expect(rows[2].Fallback == "third" && rows[2].Checked == 3);
        });
        Test($"{label}: CASE aggregates group by the resulting value and count result groups", () =>
        {
            var query = new SelectQueryInitializer<Item>().BuildQuery(q => new
            {
                Bucket = q.Case<string>(c => c.AddCaseValue("all", w => w.Where(x => x.Id, 0, SqlOp.GT)).AddElseValue("none")),
                Count = q.Count()
            }).Page(1, 1);
            var response = query.ExecuteWithTotalCount(connector);
            var rows = Result(response);
            Expect(rows.Count == 1 && rows[0].Bucket == "all" && rows[0].Count == 3 && response.TotalResults == 1);
            var repeat = query.ExecuteWithTotalCount(connector);
            Expect(Result(repeat).Single().Count == 3 && repeat.TotalResults == 1);
        });
        Test($"{label}: anonymous grouped ordering uses aggregate expressions before pagination", () =>
        {
            var query = new SelectQueryInitializer<Item>().BuildQuery(q => new
            {
                Category = q.Select(x => x.CategoryId),
                Count = q.Count()
            }).OrderBy(x => x.Count, SqlDir.DESC).Page(1, 1);
            var response = query.ExecuteWithTotalCount(connector);
            var row = Result(response).Single();
            Expect(row.Category == 1 && row.Count == 2 && response.TotalResults == 2);
        });
        Test($"{label}: DISTINCT aggregate counts ignore nulls and preserve empty results", () =>
        {
            var query = new SelectQueryInitializer<Item>().BuildQuery(q => new
            {
                Count = q.Count(x => x.CategoryId, true),
                Big = q.CountBig(x => x.CategoryId, true),
                Sum = (long?)q.Sum(x => x.Id),
                Average = (decimal?)q.Avg(x => x.Id)
            });
            var row = Result(query.Execute(connector)).Single();
            Expect(row.Count == 1 && row.Big == 1 && row.Sum == 6 && row.Average == 2);
            var empty = query.Where(x => x.Id, -1, SqlOp.EQ).ExecuteWithTotalCount(connector);
            var emptyRow = Result(empty).Single();
            Expect(empty.TotalResults == 1 && emptyRow.Count == 0 && emptyRow.Big == 0 && emptyRow.Sum == null && emptyRow.Average == null);
        });
        Test($"{label}: one-to-many CTE filtering preserves one root per match", () =>
        {
            var query = new SelectQuery<Item>().SelectRootColumns().Where(x => x.Children.First().Name, "a", SqlOp.EQ);
            Expect(query.Render(DbFlavor.Sqlite).Text.Contains("WITH"));
            Expect(Result(query.Execute(connector)).Single().Id == 1);
        });
        Test($"{label}: IN NOT IN and empty array filters", () =>
        {
            var subquery = new SelectQuery<Item>().Select(x => x.Id).Where(x => x.Id, 2, SqlOp.LTE);
            Expect(Result(new SelectQuery<Item>().SelectRootColumns().Where(x => x.Id, subquery, SqlArrayOp.IN).Execute(connector)).Count == 2);
            Expect(Result(new SelectQuery<Item>().SelectRootColumns().Where(x => x.Id, subquery, SqlArrayOp.NOTIN).Execute(connector)).Single().Id == 3);
            Expect(Result(new SelectQuery<Item>().SelectRootColumns().Where(x => x.Id, Array.Empty<int>(), SqlArrayOp.IN).Execute(connector)).Count == 0);
            Expect(Result(new SelectQuery<Item>().SelectRootColumns().Where(x => x.Id, Array.Empty<int>(), SqlArrayOp.NOTIN).Execute(connector)).Count == 3);
        });
        Test($"{label}: identifiers literals and selected aliases escape quotes", () =>
        {
            Run("CREATE TABLE \"odd\"\"table\" (Id INTEGER PRIMARY KEY, \"va\"\"lue\" TEXT)");
            Result(new InsertQuery<QuotedRow>().InsertAuto(new QuotedRow { Value = "quote'\"" }).Execute(connector));
            Expect(Result(new SelectQuery<QuotedRow>().SelectRootColumns().Execute(connector)).Single().Value == "quote'\"");
            string key = "'key\"";
            var table = Table.GetCached<QuotedRow>();
            var query = new SqlLinqer.Queries.Core.SelectQuery(table);
            var selects = (SelectCollection)typeof(SqlLinqer.Queries.Core.SelectQuery).GetField("Selects", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(query);
            selects.AddStatement(new SelectColumn(table.Columns.TryGet(nameof(QuotedRow.Value)), key));
            var raw = Result(connector.ExecuteReaderDataTable(query.Render(DbFlavor.Sqlite)));
            Expect(raw.Columns[0].ColumnName == key && (string)raw.Rows[0][0] == "quote'\"");
            Expect("'".DbWrapString(DbFlavor.Sqlite) == "''''");
        });
    }
}
