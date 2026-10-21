using System.Data;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SqlLinqer;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Ordering;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Components.Update;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.Sqlite;

internal static class SqliteSuite
{
    internal static int Run()
    {
        int passed = 0, failed = 0;
        void Test(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine($"PASS {name}"); }
            catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}\n{e}"); }
        }
        void Expect(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
        T Result<T>(SqlResponse<T> response)
        {
            if (response.State != ResponseState.Valid) throw response.Error;
            return response.Result;
        }
        void Reject(Action action)
        {
            try { action(); } catch (NotSupportedException) { return; }
            throw new Exception("Expected NotSupportedException");
        }
        var file = Path.Combine(Path.GetTempPath(), $"sqllinqer-{Guid.NewGuid():N}.db");
        try
        {
            foreach (bool memory in new[] { false, true })
            {
                string label = memory ? "shared memory" : "file";
                string connectionString = memory
                    ? $"Data Source=sqllinqer-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False;Default Timeout=4"
                    : new SqliteConnectionStringBuilder { DataSource = file, Pooling = false, DefaultTimeout = 4 }.ToString();
                using var keeper = new SqliteConnection(connectionString);
                keeper.Open();
                using (var version = keeper.CreateCommand())
                {
                    version.CommandText = "SELECT sqlite_version()";
                    string runtime = (string)version.ExecuteScalar();
                    Console.WriteLine($"{label}: Microsoft.Data.Sqlite {typeof(SqliteConnection).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}; SQLite {runtime}; .NET {Environment.Version}");
                    Expect(Version.Parse(runtime) >= new Version(3, 38));
                    string expectedRuntime = Environment.GetEnvironmentVariable("SQLLINQER_EXPECT_SQLITE_VERSION");
                    if (expectedRuntime != null)
                        Expect(runtime == expectedRuntime, $"Expected SQLite {expectedRuntime}, loaded {runtime}");
                }
                var connector = new ReplicatorConnector(keeper, DbFlavor.Sqlite,
                    new ConnectorOptions { CommandTimeout = TimeSpan.FromSeconds(2), ConnectionTimeout = TimeSpan.FromSeconds(9) });
                void Run(string sql) => Result(connector.ExecuteNonQuery(new RenderedQuery(sql)));
                long Scalar(string sql) => Result(connector.ExecuteScalar<long>(new RenderedQuery(sql)));
                Run("""
                    CREATE TABLE categories (Id INTEGER PRIMARY KEY, Name TEXT);
                    CREATE TABLE items (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT, OtherName TEXT, Enabled INTEGER,
                      Status INTEGER, CategoryId INTEGER, Token TEXT, Created TEXT, OffsetDate TEXT, Amount TEXT, Bytes BLOB);
                    CREATE TABLE children (Id INTEGER PRIMARY KEY, ItemId INTEGER, Name TEXT, CategoryId INTEGER);
                    INSERT INTO categories VALUES (1, 'group');
                    INSERT INTO children VALUES (1, 1, 'z', 1), (2, 1, 'a', NULL), (3, 1, 'm', 1), (4, 2, 'other', 1);
                    """);
                var token = Guid.NewGuid();
                var date = new DateTime(2026, 10, 20, 12, 34, 56).AddTicks(1234567);
                var offset = new DateTimeOffset(date, TimeSpan.FromHours(-4));
                var item = new Item { Name = "a_%\\'\"", OtherName = "old", Enabled = true, Status = State.Active,
                    CategoryId = 1, Token = token, Created = date, OffsetDate = offset, Amount = 1234567890.123456789m,
                    Bytes = [0, 1, 127, 255] };
                Test($"{label}: connector preserves provider timeout and connection string", () =>
                {
                    using var copy = (SqliteConnection)connector.CreateConnection();
                    Expect(copy.DefaultTimeout == 4);
                    Expect(!copy.ConnectionString.Contains("Connect Timeout", StringComparison.OrdinalIgnoreCase));
                });
                Test($"{label}: insert RETURNING converts Int64 primary keys to Int32", () =>
                {
                    var response = new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(item).Execute(connector);
                    Expect(Result(response) == 1);
                    Expect(response.TouchedPrimaryKeys.SequenceEqual([1]));
                });
                Test($"{label}: multirow insert and null/default values", () =>
                {
                    var response = new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(new[] {
                        new Item { Name = "second", CategoryId = 1, Bytes = [] },
                        new Item { Name = "third", CategoryId = null, Bytes = null }
                    }).Execute(connector);
                    Expect(Result(response) == 2);
                    Expect(response.TouchedPrimaryKeys.Order().SequenceEqual([2, 3]));
                });
                Test($"{label}: typed root JSON roundtrips provider values", () =>
                {
                    var rows = Result(new SelectQuery<Item>().SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC).Execute(connector));
                    Expect(rows.Count == 3);
                    var row = rows[0];
                    Expect(row.Name == item.Name && row.Enabled && row.Status == State.Active);
                    Expect(row.Token == token && row.Created == date && row.OffsetDate == offset);
                    Expect(row.Amount == item.Amount && row.Bytes.SequenceEqual(item.Bytes));
                    Expect(rows[1].Bytes.Length == 0 && rows[2].Bytes == null && rows[2].CategoryId == null);
                });
                Test($"{label}: direct render and connector render share parameter syntax", () =>
                {
                    var query = new SelectQuery<Item>().Select(x => x.Name).Where(x => x.Name, item.Name, SqlOp.EQ);
                    var direct = query.Render(DbFlavor.Sqlite);
                    Expect(direct.Parameters.GetAll().Single().Placehodler == "@P1");
                    Expect(Result(connector.ExecuteReaderJson<Item>(direct)).Single().Name == item.Name);
                    Expect(Result(query.Execute(connector)).Single().Name == item.Name);
                });
                Test($"{label}: DateTime clock roundtrips but Kind is not stored", () =>
                {
                    using var transaction = keeper.BeginTransaction();
                    var utc = DateTime.SpecifyKind(date, DateTimeKind.Utc);
                    var inserted = new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(new Item { Created = utc }).Execute(connector, transaction);
                    Result(inserted);
                    var row = Result(new SelectQuery<Item>().Select(x => x.Created)
                        .Where(x => x.Id, inserted.TouchedPrimaryKeys.Single(), SqlOp.EQ).Execute(connector, transaction)).Single();
                    Expect(row.Created.Value.Ticks == utc.Ticks && row.Created.Value.Kind == DateTimeKind.Unspecified);
                    transaction.Rollback();
                });
                Test($"{label}: root Top Page Limit and total count", () =>
                {
                    Expect(Result(new SelectQuery<Item>().SelectRootColumns().Top(2).Execute(connector)).Count == 2);
                    var query = new SelectQuery<Item>().SelectRootColumns().Page(2, 1);
                    Expect(Result(query.Execute(connector)).Single().Id == 2);
                    var counted = query.ExecuteWithTotalCount(connector);
                    Expect(Result(counted).Single().Id == 2 && counted.TotalResults == 3);
                    Expect(Result(new SelectQuery<Item>().SelectRootColumns().Limit(1, 2).Execute(connector)).Single().Id == 3);
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
                    var rows = Result(connector.ExecuteReaderJson<Item>(query.Render(DbFlavor.Sqlite)));
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
                    var values = Result(new SelectQueryInitializer<Item>().BuildQuery(q => new {
                        Id = q.Select(x => x.Id), Year = q.YEAR(x => x.Created), Month = q.MONTH(x => x.Created), Day = q.DAY(x => x.Created),
                        Upper = q.UPPER(x => x.OtherName), Right = q.RIGHT(x => x.OtherName, 2), Sub = q.SUBSTRING(x => x.OtherName, 2, 2),
                        Left = q.LEFT(x => x.OtherName, 2),
                        Concat = q.Concat(x => x.OtherName, x => x.Name), Joined = q.ConcatWS("|", x => x.OtherName, x => x.Name)
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
                    var query = new SelectQueryInitializer<Item>().BuildQuery(q => new {
                        Value = q.Case<string>(c => c.AddCaseValue("yes", w => w.Where(x => x.Enabled, true, SqlOp.EQ)).AddElseValue("no")),
                        Upper = q.Concat(x => q.UPPER(x => x.OtherName), x => x.Name),
                        Empty = q.RIGHT(x => x.Name, 0)
                    }).Where(x => x.Id, 1, SqlOp.EQ);
                    var row = Result(query.Execute(connector)).Single();
                    Expect(row.Value == "yes" && row.Upper == "OLD" + item.Name && row.Empty == "");
                });
                Test($"{label}: typed nested CASE and functions handle null and column branches", () =>
                {
                    var rows = Result(new SelectQueryInitializer<Item>().BuildQuery(q => new {
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
                    var query = new SelectQueryInitializer<Item>().BuildQuery(q => new {
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
                    var query = new SelectQueryInitializer<Item>().BuildQuery(q => new {
                        Category = q.Select(x => x.CategoryId), Count = q.Count()
                    }).OrderBy(x => x.Count, SqlDir.DESC).Page(1, 1);
                    var response = query.ExecuteWithTotalCount(connector);
                    var row = Result(response).Single();
                    Expect(row.Category == 1 && row.Count == 2 && response.TotalResults == 2);
                });
                Test($"{label}: DISTINCT aggregate counts ignore nulls and preserve empty results", () =>
                {
                    var query = new SelectQueryInitializer<Item>().BuildQuery(q => new {
                        Count = q.Count(x => x.CategoryId, true), Big = q.CountBig(x => x.CategoryId, true),
                        Sum = (long?)q.Sum(x => x.Id), Average = (decimal?)q.Avg(x => x.Id)
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
                Test($"{label}: identifiers literals and JSON keys escape quotes", () =>
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
                    using var json = JsonDocument.Parse((string)raw.Rows[0][0]);
                    Expect(json.RootElement.GetProperty(key).GetString() == "quote'\"");
                    Expect("'".DbWrapString(DbFlavor.Sqlite) == "''''");
                });
                Test($"{label}: GUID primary keys and default-only inserts", () =>
                {
                    Run("CREATE TABLE guid_rows (Id TEXT PRIMARY KEY, Name TEXT); CREATE TABLE defaults (Id INTEGER PRIMARY KEY)");
                    Guid id = Guid.NewGuid();
                    var inserted = new InsertWithPrimaryKeyQuery<GuidRow, Guid>().InsertAuto(new GuidRow { Id = id, Name = "guid" }).Execute(connector);
                    Expect(Result(inserted) == 1 && inserted.TouchedPrimaryKeys.Single() == id);
                    var updated = new UpdateWithPrimaryKeyQuery<GuidRow, Guid>().Update(x => x.Name, "updated").Where(x => x.Id, id, SqlOp.EQ).Execute(connector);
                    Expect(Result(updated) == 1 && updated.TouchedPrimaryKeys.Single() == id);
                    var deleted = new DeleteWithPrimaryKeyQuery<GuidRow, Guid>().Where(x => x.Id, id, SqlOp.EQ).Execute(connector);
                    Expect(Result(deleted) == 1 && deleted.TouchedPrimaryKeys.Single() == id);
                    var defaults = new InsertWithPrimaryKeyQuery<DefaultRow, int>().InsertAuto(new DefaultRow()).Execute(connector);
                    Expect(Result(defaults) == 1 && defaults.TouchedPrimaryKeys.Single() == 1);
                    Reject(() => new InsertQuery<DefaultRow>().InsertAuto(new[] { new DefaultRow(), new DefaultRow() }).Render(DbFlavor.Sqlite));
                });
                Test($"{label}: default expressions RETURNING and failed bulk writes are atomic", () =>
                {
                    Run("CREATE TABLE generated_rows (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL DEFAULT 'generated', Number INTEGER NOT NULL DEFAULT (6*7))");
                    var output = Result(new InsertQuery<GeneratedRow>().InsertAuto(new GeneratedRow())
                        .Output(x => x.Id).Output(x => x.Name).Output(x => x.Number).ExecuteWithOutput(connector));
                    Expect(output.Rows.Count == 1 && (long)output.Rows[0][0] == 1 && (string)output.Rows[0][1] == "generated" && (long)output.Rows[0][2] == 42);
                    var key = new InsertWithPrimaryKeyQuery<GeneratedRow, long>().InsertAuto(new GeneratedRow()).Execute(connector);
                    Expect(Result(key) == 1 && key.TouchedPrimaryKeys.Single() == 2L);
                    Guid id = Guid.NewGuid();
                    var failedBulk = new InsertWithPrimaryKeyQuery<GuidRow, Guid>().InsertAuto(new[] {
                        new GuidRow { Id = id, Name = "first" }, new GuidRow { Id = id, Name = "duplicate" }
                    }).Execute(connector);
                    Expect(failedBulk.State == ResponseState.Error);
                    Expect(Result(new SelectQuery<GuidRow>().SelectRootColumns().Where(x => x.Id, id, SqlOp.EQ).Execute(connector)).Count == 0);
                    var retry = new InsertWithPrimaryKeyQuery<GuidRow, Guid>().InsertAuto(new GuidRow { Id = id, Name = "retry" }).Execute(connector);
                    Expect(Result(retry) == 1 && retry.TouchedPrimaryKeys.Single() == id);
                });
                Test($"{label}: joined writes without a primary key are rejected", () =>
                {
                    Reject(() => new UpdateQuery<NoKeyRow>().Update(x => x.Name, "bad").Where(x => x.Category.Name, "group", SqlOp.EQ).Render(DbFlavor.Sqlite));
                    Reject(() => new DeleteQuery<NoKeyRow>().Where(x => x.Category.Name, "group", SqlOp.EQ).Render(DbFlavor.Sqlite));
                });
                Test($"{label}: plain updates use unqualified targets and commit owned transactions", () =>
                {
                    Expect(Result(new UpdateQuery<Item>().Update(x => x.OtherName, x => x.Name).Where(x => x.Id, 2, SqlOp.EQ).Execute(connector)) == 1);
                    Expect(Result(new SelectQuery<Item>().Select(x => x.OtherName).Where(x => x.Id, 2, SqlOp.EQ).Execute(connector)).Single().OtherName == "second");
                    Expect(Result(new SelectPrimaryKeyExistsQuery<Item, int>(1).Execute(connector)));
                    Expect(!Result(new SelectPrimaryKeyExistsQuery<Item, int>(-1).Execute(connector)));
                });
                Test($"{label}: update RETURNING with primary keys and arithmetic", () =>
                {
                    var response = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.OtherName, "changed").Where(x => x.Id, 1, SqlOp.EQ).Execute(connector);
                    Expect(Result(response) == 1 && response.TouchedPrimaryKeys.Single() == 1);
                    var output = Result(new UpdateQuery<Item>().Update(x => x.Id, new BasicMathOperation(BasicMathOperation.MathOp.Add, 0))
                        .Output(x => x.Name).Where(x => x.Id, 1, SqlOp.EQ).ExecuteWithOutput(connector));
                    Expect((string)output.Rows[0][0] == item.Name);
                });
                Test($"{label}: joined update uses safe target selection", () =>
                {
                    var response = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.OtherName, "joined")
                        .Where(x => x.Category.Name, "group", SqlOp.EQ).Execute(connector);
                    Expect(Result(response) == 2 && response.TouchedPrimaryKeys.Order().SequenceEqual([1, 2]));
                    var cte = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.OtherName, "child")
                        .Where(x => x.Children.First().Name, "a", SqlOp.EQ).Execute(connector);
                    Expect(Result(cte) == 1 && cte.TouchedPrimaryKeys.Single() == 1);
                    var unmatched = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.OtherName, "unmatched")
                        .Where(x => x.Category.Name, (string)null, SqlOp.EQ).Execute(connector);
                    Expect(Result(unmatched) == 1 && unmatched.TouchedPrimaryKeys.Single() == 3);
                });
                Test($"{label}: joined source assignments preserve LEFT nulls and simultaneous target references", () =>
                {
                    using var transaction = keeper.BeginTransaction();
                    var query = new UpdateWithPrimaryKeyQuery<Item, int>()
                        .Update(x => x.OtherName, x => x.Name).Update(x => x.Name, x => x.Category.Name);
                    string sql = query.Render(DbFlavor.Sqlite).Text;
                    Expect(sql.Contains("LEFT JOIN") && !sql.Contains("LIMIT"));
                    var updated = query.Execute(connector, transaction);
                    Expect(Result(updated) == 3 && updated.TouchedPrimaryKeys.Order().SequenceEqual([1, 2, 3]));
                    var rows = Result(new SelectQuery<Item>().Select(x => x.Id).Select(x => x.Name).Select(x => x.OtherName)
                        .OrderBy(x => x.Id, SqlDir.ASC).Execute(connector, transaction));
                    Expect(rows[0].Name == "group" && rows[0].OtherName == item.Name);
                    Expect(rows[1].Name == "group" && rows[1].OtherName == "second");
                    Expect(rows[2].Name == null && rows[2].OtherName == "third");
                    transaction.Rollback();
                    Expect(Result(new SelectQuery<Item>().Select(x => x.Name).Where(x => x.Id, 1, SqlOp.EQ).Execute(connector)).Single().Name == item.Name);
                });
                Test($"{label}: joined source assignments combine CTE filters RETURNING and no matches", () =>
                {
                    using var transaction = keeper.BeginTransaction();
                    var query = new UpdateQuery<Item>().Update(x => x.OtherName, x => x.Category.Name)
                        .Where(x => x.Children.First().Name, "a", SqlOp.EQ).Output(x => x.Id).Output(x => x.OtherName);
                    Expect(query.Render(DbFlavor.Sqlite).Text.Contains("WITH"));
                    var output = Result(query.ExecuteWithOutput(connector, transaction));
                    Expect(output.Rows.Count == 1 && (long)output.Rows[0][0] == 1 && (string)output.Rows[0][1] == "group");
                    var none = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.OtherName, x => x.Category.Name)
                        .Where(x => x.Category.Name, "absent", SqlOp.EQ).Execute(connector, transaction);
                    Expect(Result(none) == 0 && none.TouchedPrimaryKeys.Count == 0);
                    var nulls = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.OtherName, x => x.Category.Name)
                        .Where(x => x.Category.Name, (string)null, SqlOp.EQ).Execute(connector, transaction);
                    Expect(Result(nulls) == 1 && nulls.TouchedPrimaryKeys.Single() == 3);
                    transaction.Rollback();
                });
                Test($"{label}: joined duplicate sources update each target once using a native matching value", () =>
                {
                    using var transaction = keeper.BeginTransaction();
                    var query = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.OtherName, x => x.Children.First().Name)
                        .Update(x => x.Id, new BasicMathOperation(BasicMathOperation.MathOp.Add, 10))
                        .Where(x => x.Id, 1, SqlOp.EQ);
                    Expect(!query.Render(DbFlavor.Sqlite).Text.Contains("LIMIT"));
                    var updated = query.Execute(connector, transaction);
                    Expect(Result(updated) == 1 && updated.TouchedPrimaryKeys.Single() == 11);
                    var row = Result(new SelectQuery<Item>().Select(x => x.OtherName).Where(x => x.Id, 11, SqlOp.EQ).Execute(connector, transaction)).Single();
                    Expect(new[] { "z", "a", "m" }.Contains(row.OtherName));
                    transaction.Rollback();
                    Expect(Scalar("SELECT count(*) FROM items WHERE Id = 1") == 1);
                });
                Test($"{label}: rejects unsupported requests before execution", () =>
                {
                    try { new UpdateQuery<Item>().Update(x => x.Category.Name, "bad"); throw new Exception("Expected root-column validation"); }
                    catch (FormatException) { }
                    Reject(() => new TruncateQuery<Item>().Render(DbFlavor.Sqlite));
                    Reject(() => new SqlLinqer.Queries.Core.StoredProcedureQuery("proc").Render(DbFlavor.Sqlite));
                    Reject(() => new InsertQuery<Item>().InsertAuto(item).RenderTVP(new ParameterCollection(), DbFlavor.Sqlite, null));
                    Reject(() => new SelectQueryInitializer<Item>().BuildQuery(q => new { Value = q.REVERSE(x => x.Name) }).Render(DbFlavor.Sqlite));
                });
                Test($"{label}: rollback and failed writes keep caller ownership", () =>
                {
                    using var connection = (SqliteConnection)connector.CreateConnection(); connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        Result(new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(new Item { Name = "rolled back" }).Execute(connector, transaction));
                        Expect(connector.ExecuteNonQuery(new RenderedQuery("INSERT INTO no_such_table VALUES (1)"), transaction).State == ResponseState.Error);
                        Expect(transaction.Connection == connection && connection.State == ConnectionState.Open);
                        transaction.Rollback();
                    }
                    Expect(Scalar("SELECT count(*) FROM items") == 3);
                    using (var transaction = connection.BeginTransaction())
                    {
                        Result(new InsertQuery<Item>().InsertAuto(new Item { Name = "committed" }).Execute(connector, transaction));
                        transaction.Commit();
                    }
                    Expect(Scalar("SELECT count(*) FROM items") == 4);
                });
                Test($"{label}: delete RETURNING through joined filters", () =>
                {
                    using (var transaction = keeper.BeginTransaction())
                    {
                        var cteDelete = new DeleteWithPrimaryKeyQuery<Item, int>().Where(x => x.Children.First().Name, "a", SqlOp.EQ).Execute(connector, transaction);
                        Expect(Result(cteDelete) == 1 && cteDelete.TouchedPrimaryKeys.Single() == 1);
                        transaction.Rollback();
                    }
                    var response = new DeleteWithPrimaryKeyQuery<Item, int>().Where(x => x.Category.Name, "group", SqlOp.EQ).Execute(connector);
                    Expect(Result(response) == 2 && response.TouchedPrimaryKeys.Order().SequenceEqual([1, 2]));
                    Expect(Scalar("SELECT count(*) FROM items") == 2);
                    var output = Result(new DeleteQuery<Item>().Output(x => x.Name).Where(x => x.Id, 3, SqlOp.EQ).ExecuteWithOutput(connector));
                    Expect((string)output.Rows[0][0] == "third");
                });
                SqlLinqer.Tests.Regression.CompatibleDatabaseTests.Run(connector, (name, action) => Test($"{label}: {name}", action));
                Test($"{label}: attached schema remains qualified and missing schemas fail", () =>
                {
                    using var transaction = keeper.BeginTransaction();
                    Result(connector.ExecuteNonQuery(new RenderedQuery("ATTACH DATABASE ':memory:' AS attached; CREATE TABLE attached.categories (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO attached.categories VALUES (9,'attached');"), transaction));
                    var query = new SelectQuery<Category>("attached").SelectRootColumns();
                    Expect(query.Render(DbFlavor.Sqlite).Text.Contains("\"attached\".\"categories\""));
                    Expect(Result(query.Execute(connector, transaction)).Single().Name == "attached");
                    Expect(new SelectQuery<Category>("missing").SelectRootColumns().Execute(connector, transaction).State == ResponseState.Error);
                    var updated = new UpdateWithPrimaryKeyQuery<Category, int>("attached").Update(x => x.Name, "changed").Where(x => x.Id, 9, SqlOp.EQ).Execute(connector, transaction);
                    Expect(Result(updated) == 1 && updated.TouchedPrimaryKeys.Single() == 9);
                    var deleted = new DeleteWithPrimaryKeyQuery<Category, int>("attached").Where(x => x.Id, 9, SqlOp.EQ).Execute(connector, transaction);
                    Expect(Result(deleted) == 1 && deleted.TouchedPrimaryKeys.Single() == 9);
                    Expect(Result(new SelectQuery<Category>("main").SelectRootColumns().Execute(connector, transaction)).Single().Name == "group");
                    transaction.Rollback();
                });
            }
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(file); }
        SqlLinqer.Tests.Regression.AsyncProviderChecks.Run(DbFlavor.Sqlite, Test);
        Console.WriteLine($"{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;

    }
}

[SqlTable("items"), SqlAutoInsert, SqlAutoSelect]
public class Item
{
    [SqlPrimaryKey, SqlAutoInsertExclude] public int Id { get; set; }
    public string Name { get; set; }
    public string OtherName { get; set; }
    public bool Enabled { get; set; }
    public State Status { get; set; }
    public int? CategoryId { get; set; }
    public Guid Token { get; set; }
    public DateTime? Created { get; set; }
    public DateTimeOffset? OffsetDate { get; set; }
    public decimal Amount { get; set; }
    public byte[] Bytes { get; set; }
    [SqlOneToOne(nameof(CategoryId)), SqlAutoSelect] public Category Category { get; set; }
    [SqlOneToMany(nameof(Child.ItemId)), SqlAutoSelect] public List<Child> Children { get; set; }
}
public enum State { None, Active }
[SqlTable("categories"), SqlAutoSelect]
public class Category
{
    [SqlPrimaryKey] public int Id { get; set; }
    public string Name { get; set; }
}
[SqlTable("children"), SqlAutoSelect]
public class Child
{
    [SqlPrimaryKey] public int Id { get; set; }
    public int ItemId { get; set; }
    public string Name { get; set; }
    public int? CategoryId { get; set; }
    [SqlOneToOne(nameof(CategoryId)), SqlAutoSelect] public Category Category { get; set; }
}

[SqlTable("odd\"table")]
public class QuotedRow
{
    [SqlPrimaryKey] public int Id { get; set; }
    [SqlColumn("va\"lue")] public string Value { get; set; }
}
[SqlTable("guid_rows")]
public class GuidRow
{
    [SqlPrimaryKey(false)] public Guid Id { get; set; }
    public string Name { get; set; }
}
[SqlTable("defaults")]
public class DefaultRow
{
    [SqlPrimaryKey] public int Id { get; set; }
}
[SqlTable("items")]
public class NoKeyRow
{
    public string Name { get; set; }
    public int? CategoryId { get; set; }
    [SqlOneToOne(nameof(CategoryId))] public Category Category { get; set; }
}
public sealed class NestedPageQuery : SelectQuery<Item>
{
    public NestedPageQuery()
    {
        Select(x => x.Id); Select(x => x.Children);
        var relationship = Selects.GetAll<SelectRelationship>().Single();
        var table = relationship.Relationship.ChildTable;
        // Public components expose nested paging to derived query builders.
        var queryType = typeof(SqlLinqer.Queries.Core.SelectQuery);
        var paging = (PagingControls)queryType.GetField("PagingControls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(relationship.SubQuery);
        paging.SetLimit(1, 1);
        var ordering = (OrderByCollection)queryType.GetField("OrderBys", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(relationship.SubQuery);
        ordering.AddStatement(new ColumnOrderBy(table.Columns.TryGet(nameof(Child.Name)), SqlDir.ASC));
    }
}

[SqlTable("words")]
public class WordRow
{
    [SqlPrimaryKey(false)] public int Id { get; set; }
    public string Name { get; set; }
}

[SqlTable("generated_rows")]
public class GeneratedRow
{
    [SqlPrimaryKey] public int Id { get; set; }
    [SqlAutoInsertExclude] public string Name { get; set; }
    [SqlAutoInsertExclude] public int Number { get; set; }
}
