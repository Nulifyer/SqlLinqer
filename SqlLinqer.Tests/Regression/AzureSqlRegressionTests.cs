using System.Data;
using System.Text.RegularExpressions;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Ordering;
using SqlLinqer.Components.Outputs;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Components.Withs;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Core.Abstract;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.Regression;

internal static class AzureSqlRegressionTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static string Stable(string text) => Regex.Replace(text ?? "", "[a-f0-9]{32}", "<id>");
    private static void Same(string sqlServer, string azure)
        => Check(Stable(sqlServer) == Stable(azure), $"SQL Server: {sqlServer}\nAzure: {azure}");
    private static void SameParameters(ParameterCollection sqlServer, ParameterCollection azure)
    {
        var left = sqlServer.GetAll().ToArray();
        var right = azure.GetAll().ToArray();
        Check(left.Length == right.Length, "Parameter count differs");
        for (int i = 0; i < left.Length; i++)
            Check(left[i].Placehodler == right[i].Placehodler && Equals(left[i].Value, right[i].Value)
                && left[i].DbType == right[i].DbType, "Parameter value, type or placeholder differs");
    }
    private static void Equivalent(BaseQuery query)
    {
        var expected = query.Render(DbFlavor.SqlServer);
        var actual = query.Render(DbFlavor.AzureSql);
        Same(expected.Text, actual.Text);
        SameParameters(expected.Parameters, actual.Parameters);
        var direct = new ParameterCollection();
        Same(expected.Text, query.Render(direct, DbFlavor.AzureSql));
        SameParameters(expected.Parameters, direct);
        var connector = new FakeConnector(new FakeDatabase(), DbFlavor.AzureSql);
        Same(expected.Text, query.Render(connector).Text);
        Check(connector.DbFlavor == DbFlavor.AzureSql, "Connector flavor was normalized");
    }

    public static void Run(Action<string, Action> test)
    {
        test("Azure enum is distinct and preserves existing values", () =>
        {
            Check((int)DbFlavor.SqlServer == 0 && (int)DbFlavor.MySql == 1 && (int)DbFlavor.PostgreSql == 2,
                "Existing enum value changed");
            Check((int)DbFlavor.AzureSql == 5 && DbFlavor.AzureSql != DbFlavor.SqlServer, "Azure enum is aliased");
        });

        var queries = new Dictionary<string, Func<BaseQuery>>
        {
            ["root select"] = () => new SelectQuery<Item>().SelectRootColumns().Where(x => x.Enabled, true, SqlOp.EQ),
            ["nested one to one JSON"] = () => new SelectQuery<Item>().SelectRootColumns().Select(x => x.Category),
            ["nested one to many JSON"] = () => new SelectQuery<AzureParent>().SelectRootColumns().Select(x => x.Children),
            ["Top"] = () => new SelectQuery<Item>().SelectRootColumns().Top(3),
            ["distinct paging and descending order"] = () => new SelectQuery<Item>().SelectRootColumns().Distinct()
                .OrderBy(x => x.Name, SqlDir.DESC).ThenBy(x => x.Id, SqlDir.ASC).Page(2, 3),
            ["LIKE"] = () => new SelectQuery<Item>().SelectRootColumns().Where(x => x.Name, "a[%_]", SqlOp.LIKE),
            ["WHERE subquery"] = () => new SelectQuery<Item>().SelectRootColumns()
                .Where(x => x.CategoryId, new SelectQuery<Category>().Select(x => x.Id).Top(2), SqlArrayOp.IN),
            ["aggregate and group by"] = () => Item.BeginSelectBuilder().BuildQuery(x => new
            { Name = x.Select(y => y.Name), Count = x.Count(), Max = x.Max(y => y.Id), Upper = x.UPPER(y => y.Name) }),
            ["CASE"] = () => Item.BeginSelectBuilder().BuildQuery(x => new
            { Flag = x.Case<int>(c => c.AddCaseValue(1, w => w.Where(y => y.Enabled, true, SqlOp.EQ)).AddElseValue(0)) }),
            ["CTE"] = () => new CteSelect(),
            ["insert output"] = () => new InsertQuery<Item>().InsertAuto(new Item { Name = "a", Enabled = true }).Output(x => x.Id),
            ["insert returned key"] = () => new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(new Item { Name = "a" }),
            ["update output"] = () => new UpdateQuery<Item>().Update(x => x.Name, "new").Where(x => x.Id, 7, SqlOp.EQ).Output(x => x.Name),
            ["update returned key with join"] = () => new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.Name, "new")
                .Where(x => x.Category.Name, "category", SqlOp.EQ),
            ["delete output"] = () => new DeleteQuery<Item>().Where(x => x.Id, 7, SqlOp.EQ).Output(x => x.Name),
            ["delete returned key with join"] = () => new DeleteWithPrimaryKeyQuery<Item, int>().Where(x => x.Category.Name, "category", SqlOp.EQ),
            ["truncate"] = () => new TruncateQuery<Item>(),
        };
        foreach (var query in queries)
        {
            test($"Azure SQL Server equivalence: {query.Key}", () => Equivalent(query.Value()));
            if (query.Value() is SqlLinqer.Queries.Core.SelectQuery select)
            {
                test($"Azure count equivalence: {query.Key}", () =>
                {
                    var left = new ParameterCollection(); var right = new ParameterCollection();
                    Same(select.RenderForCount(left, DbFlavor.SqlServer), select.RenderForCount(right, DbFlavor.AzureSql));
                    SameParameters(left, right);
                });
                if (!select.Selects.GetAll().Any(s => s is SelectRelationship))
                    test($"Azure direct relational select equivalence: {query.Key}", () =>
                    {
                        var left = new ParameterCollection(); var right = new ParameterCollection();
                        Same(select.RenderRows(left, DbFlavor.SqlServer), select.RenderRows(right, DbFlavor.AzureSql));
                        SameParameters(left, right);
                    });
            }
        }

        foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
        {
            test($"{flavor} DISTINCT count retains projection and restores paging", () =>
            {
                var query = new SelectQuery<Item>().Select(x => x.Name).Distinct().OrderBy(x => x.Name, SqlDir.ASC).Page(2, 1);
                var before = query.Render(flavor).Text;
                var sql = query.RenderForCount(new ParameterCollection(), flavor);
                Check(sql.Contains("SELECT COUNT(*) FROM (") && sql.Contains("SELECT DISTINCT")
                    && !sql.Contains("ORDER BY") && !sql.Contains("FETCH NEXT") && !sql.Contains("FOR JSON"), "Incorrect DISTINCT count");
                Check(query.Render(flavor).Text == before, "Count mutated query");
            });
            test($"{flavor} grouped and ungrouped aggregate totals count result rows", () =>
            {
                var grouped = Item.BeginSelectBuilder().BuildQuery(x => new { Enabled = x.Select(y => y.Enabled), Count = x.Count() });
                var sql = grouped.RenderForCount(new ParameterCollection(), flavor);
                Check(sql.Contains("SELECT COUNT(*) FROM (") && sql.Contains("GROUP BY"), "Group count dropped groups");
                var count = Item.BeginSelectBuilder().BuildQuery(x => new { Count = x.Count() });
                Check(count.Render(flavor).Text.Contains("FROM ["), "COUNT(1) lost root table");
                Check(count.RenderForCount(new ParameterCollection(), flavor).Contains("SELECT COUNT(*) FROM ("), "Aggregate total counts input rows");
            });
            test($"{flavor} source assignment discovers join and OUTPUT order", () =>
            {
                var sql = new UpdateQuery<Item>().Update(x => x.Name, x => x.Category.Name).Output(x => x.Id).Render(flavor).Text;
                Check(sql.Contains("JOIN") && sql.IndexOf(" SET ") < sql.IndexOf("OUTPUT")
                    && sql.IndexOf("OUTPUT") < sql.IndexOf("FROM"), "Joined assignment or output misplaced");
            });
            test($"{flavor} DISTINCT count discovers relationship CTEs before emitting WITH", () =>
            {
                var query = new SelectQuery<AzureParent>().Select(x => x.Id).Distinct()
                    .Where(x => x.Children.First().Name, "child", SqlOp.EQ).Page(2, 1);
                var parameters = new ParameterCollection();
                string count = query.RenderForCount(parameters, flavor);
                Check(count.TrimStart().StartsWith("WITH ") && count.Contains("SELECT COUNT(*) FROM (")
                    && !count.Contains("FROM (WITH"), "Generated relationship CTE missing from the outer count");
                Check(parameters.GetAll().Single().Value.Equals("child"), "Relationship CTE lost its bound filter");
                string repeated = query.RenderForCount(new ParameterCollection(), flavor);
                Same(count, repeated);
                Check(query.Render(flavor).Text.Contains("OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY"), "Count changed paging");
            });
            foreach (bool fail in new[] { false, true })
                test($"{flavor} per-execution TVP output uses one write transaction and autocommit type DDL, fail={fail}", () =>
                {
                    var db = new FakeDatabase { FailSql = fail ? "INSERT INTO" : null };
                    var result = new InsertQuery<Item>().InsertAuto(new Item()).Output(x => x.Id)
                        .ExecuteTvpWithOutput(new FakeConnector(db, flavor) { Options = { ReuseTvpTypes = false } });
                    var connection = db.Connections.First(); var tx = connection.Transactions.Single();
                    Check(result.State == (fail ? ResponseState.Error : ResponseState.Valid), "Wrong TVP output state");
                    Check(db.Executed.Where(x => x.CommandText.StartsWith("INSERT INTO")).All(x => x.UsedTransaction == tx), "TVP output crosses transactions");
                    TvpRegressionTests.CheckTypeDdl(db);
                    Check(tx.Commits == (fail ? 0 : 1) && tx.Rollbacks == (fail ? 1 : 0)
                        && tx.IsDisposed && connection.IsDisposed, "TVP output leaked resources");
                    if (!fail) Check(db.Executed.Count == 3 && result.Result.Rows.Count == 1, "Missing create/insert/drop or output");
                });
        }
        foreach (string stage in new[] { "CREATE TYPE", "INSERT INTO", "DROP TYPE" })
            foreach (bool caller in new[] { false, true })
                foreach (bool output in new[] { false, true })
                    test($"Azure per-execution TVP stage failure {stage}, caller={caller}, output={output}", () =>
                    {
                        var db = new FakeDatabase { FailSql = stage };
                        var connector = new FakeConnector(db, DbFlavor.AzureSql) { Options = { ReuseTvpTypes = false } };
                        using var conn = caller ? new FakeConnection(db) : null; conn?.Open();
                        using var tx = caller ? (FakeTransaction)conn.BeginTransaction() : null;
                        var insert = new InsertQuery<Item>().InsertAuto(new Item());
                        ISqlResponse response = output ? insert.Output(x => x.Id).ExecuteTvpWithOutput(connector, tx) : insert.ExecuteTvp(connector, tx);
                        Check(response.State == ResponseState.Error && response.Error.ToString().Contains("SQL stage failure: " + stage), "Failure lost original stage");
                        if (caller)
                        {
                            Check(tx.Commits == 0 && tx.Rollbacks == 0 && !tx.IsDisposed && !conn.IsDisposed, "Caller TVP completed caller resources");
                            Check(db.Executed.Where(x => x.CommandText.StartsWith("INSERT INTO")).All(x => x.UsedTransaction == tx), "Caller TVP changed transaction");
                            if (stage == "INSERT INTO") Check(db.Executed.Any(x => x.CommandText.StartsWith("DROP TYPE")), "Caller failure never attempted type cleanup");
                        }
                        else
                        {
                            var owned = db.Connections.First();
                            if (stage == "CREATE TYPE") Check(owned.Transactions.Count == 0, "Write transaction began before type creation succeeded");
                            else
                            {
                                var ownedTx = owned.Transactions.Single();
                                Check(ownedTx.Commits == 0 && ownedTx.Rollbacks == 1 && ownedTx.IsDisposed, "Failed owned TVP did not roll back/dispose");
                            }
                            Check(db.Connections.All(x => x.IsDisposed), "Failed TVP leaked an owned connection");
                        }
                        TvpRegressionTests.CheckTypeDdl(db);
                        if (stage == "DROP TYPE") Check(response.Error.Data["TvpTypeCleanupError"] is Exception, "Cleanup failure was lost");
                    });

        test("CTE output columns retain projection aliases and typed lookup", () =>
        {
            var cte = new With(new SelectQuery<Item>().SelectRootColumns(), "items");
            var id = cte.OutputTable.FindColumnFromPath(new[] { nameof(Item.Id) });
            Check(id != null && id.Alias == nameof(Item.Id) && id.ColumnName == nameof(Item.Id), "CTE lost its column alias");
            var query = new CteOutputSelect(cte).Distinct().OrderBy(x => x.Id, SqlDir.ASC).Page(2, 1);
            foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
            {
                var sql = query.RenderForCount(new ParameterCollection(), flavor);
                Check(sql.TrimStart().StartsWith("WITH [items] AS (") && sql.Contains("SELECT COUNT(*) FROM (")
                    && !sql.Contains("FROM (WITH"), "CTE count has invalid WITH placement");
                Check(query.Render(flavor).Text.Contains(" AS [Id]"), "CTE JSON alias differs from model");
            }
        });

        test("factory authentication uses new owned connections and bypasses factory for caller transactions", () =>
        {
            var db = new FakeDatabase(); int calls = 0;
            var connector = new ConnectionFactoryConnector(() => { calls++; return new FakeConnection(db); }, DbFlavor.AzureSql);
            Check(connector.ExecuteNonQuery(new RenderedQuery("test")).State == ResponseState.Valid, "Factory write failed");
            Check(connector.ExecuteNonQuery(new RenderedQuery("test")).State == ResponseState.Valid, "Second factory write failed");
            Check(calls == 2 && db.Connections.Count == 2 && db.Connections.All(x => x.IsDisposed), "Factory connection ownership wrong");
            using var caller = new FakeConnection(db); caller.Open(); using var tx = (FakeTransaction)caller.BeginTransaction();
            connector.ExecuteNonQuery(new RenderedQuery("test"), tx);
            Check(calls == 2 && !caller.IsDisposed && tx.Commits == 0 && tx.Rollbacks == 0, "Factory consumed caller resources");
            var nullFactory = new ConnectionFactoryConnector(() => null, DbFlavor.AzureSql);
            Check(nullFactory.ExecuteNonQuery(new RenderedQuery("test")).State == ResponseState.Error, "Null factory result accepted");
        });

        foreach (bool top in new[] { false, true })
            test($"Azure count rendering and state, Top={top}", () =>
            {
                var query = new SelectQuery<Item>().SelectRootColumns().Select(x => x.Category);
                if (top) query.Top(2); else query.Page(2, 3);
                string before = query.Render(DbFlavor.AzureSql).Text;
                var left = new ParameterCollection(); var right = new ParameterCollection();
                Same(query.RenderForCount(left, DbFlavor.SqlServer), query.RenderForCount(right, DbFlavor.AzureSql));
                SameParameters(left, right);
                Check(before == query.Render(DbFlavor.AzureSql).Text, "Count mutated select state");
            });

        test("Azure public component rendering bypasses query renderer", () =>
        {
            var table = Table.GetCached<Item>();
            var query = new SelectQuery<Item>().SelectRootColumns();
            var column = table.Columns.GetAll().First();
            var statements = new SelectStatement[]
            {
                new SelectColumn(column), new SelectFuncStatement(SqlFunc.COUNT, "count", 1),
                new CaseStatement<Item>(table, "flag").AddCaseValue(1, w => w.Where(x => x.Enabled, true, SqlOp.EQ)).AddElseValue(0),
            };
            foreach (var statement in statements)
                foreach (bool alias in new[] { false, true })
                    Same(statement.Render(query, new ParameterCollection(), DbFlavor.SqlServer, alias),
                        statement.Render(query, new ParameterCollection(), DbFlavor.AzureSql, alias));
            var selects = new SelectCollection(); selects.AddStatement(statements[0]);
            Same(selects.Render(query, new ParameterCollection(), DbFlavor.SqlServer),
                selects.Render(query, new ParameterCollection(), DbFlavor.AzureSql));
            var order = new ColumnOrderBy(column, SqlDir.DESC);
            Same(order.Render(query, new ParameterCollection(), DbFlavor.SqlServer),
                order.Render(query, new ParameterCollection(), DbFlavor.AzureSql));
            var output = new ColumnOutput(column, "Inserted");
            Same(output.Render(DbFlavor.SqlServer), output.Render(DbFlavor.AzureSql));
            var outputs = new OutputsCollection(); outputs.AddOutput(output);
            Same(outputs.Render(DbFlavor.SqlServer), outputs.Render(DbFlavor.AzureSql));
        });
        test("Azure quoting LIKE and public paging controls", () =>
        {
            Same("[column]", "column".DbWrap(DbFlavor.AzureSql));
            Same("'value'", "value".DbWrapString(DbFlavor.AzureSql));
            Same("[[][%][_]]", "[%_]".EscapeForSqlLike(DbFlavor.AzureSql));
            var paging = new PagingControls(); paging.SetPage(2, 3);
            Same(paging.Render(DbFlavor.SqlServer), paging.Render(DbFlavor.AzureSql));
            paging.SetTop(2); Same(paging.Render(DbFlavor.SqlServer), paging.Render(DbFlavor.AzureSql));
        });
        test("Azure TVP SQL type schema data and output", () =>
        {
            var query = new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(new Item { Name = "sample", Enabled = true });
            var left = new ParameterCollection(); var right = new ParameterCollection();
            var server = query.RenderTVP(left, DbFlavor.SqlServer, "dbo");
            var azure = query.RenderTVP(right, DbFlavor.AzureSql, "dbo");
            Same(server.Query, azure.Query); Same(server.TempTypeId, azure.TempTypeId);
            var serverType = query.RenderTempType(DbFlavor.SqlServer, "[dbo].[AzureProbe]");
            var azureType = query.RenderTempType(DbFlavor.AzureSql, "[dbo].[AzureProbe]");
            Same(serverType.Create, azureType.Create); Same(serverType.Drop, azureType.Drop);
            var lp = (TvpParameter)left.GetAll().Single(); var rp = (TvpParameter)right.GetAll().Single();
            Same(lp.TypeName, rp.TypeName);
            var ld = (DataTable)lp.Value; var rd = (DataTable)rp.Value;
            Check(ld.Columns.Count == rd.Columns.Count && ld.Rows.Count == rd.Rows.Count, "TVP shape differs");
            Check(ld.Rows[0].ItemArray.SequenceEqual(rd.Rows[0].ItemArray), "TVP values differ");
            foreach (var unsupported in new[] { DbFlavor.MySql, DbFlavor.PostgreSql })
            {
                bool rejected = false;
                try { query.RenderTVP(new ParameterCollection(), unsupported, null); }
                catch (NotSupportedException) { rejected = true; }
                Check(rejected, "TVP guard was weakened for " + unsupported);
            }
        });

        foreach (bool fail in new[] { false, true })
            foreach (bool caller in new[] { false, true })
                foreach (bool tvp in new[] { false, true })
                    test($"Azure write ownership, fail={fail}, caller={caller}, TVP={tvp}", () =>
                    {
                        var db = new FakeDatabase { FailSql = fail ? "INSERT INTO" : null };
                        var connector = new FakeConnector(db, DbFlavor.AzureSql) { Options = { ReuseTvpTypes = false } };
                        using var connection = caller ? new FakeConnection(db) : null;
                        connection?.Open();
                        using var transaction = caller ? (FakeTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted) : null;
                        var result = tvp
                            ? new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(new Item()).ExecuteTvp(connector, transaction)
                            : new InsertQuery<Item>().InsertAuto(new Item()).Execute(connector, transaction);
                        Check(result.State == (fail ? ResponseState.Error : ResponseState.Valid), "Wrong write result");
                        if (caller)
                        {
                            Check(connector.Created == (tvp ? 2 : 0) && db.Executed.Where(x => !tvp || x.CommandText.StartsWith("INSERT INTO"))
                                .All(x => x.UsedTransaction == transaction), "Caller transaction lost");
                            Check(!transaction.IsDisposed && transaction.Commits == 0 && transaction.Rollbacks == 0
                                && connection.State == ConnectionState.Open, "Caller ownership changed");
                        }
                        else
                        {
                            var owned = db.Connections.First(); var tx = owned.Transactions.Single();
                            Check(tx.IsolationLevel == IsolationLevel.ReadCommitted, "SQL Server isolation changed");
                            Check(tx.Commits == (fail ? 0 : 1) && tx.Rollbacks == (fail ? 1 : 0)
                                && tx.IsDisposed && owned.IsDisposed, "Owned resources not completed");
                        }
                        if (tvp) TvpRegressionTests.CheckTypeDdl(db);
                    });

        foreach (string operation in new[] { "insert", "update", "delete", "output", "select", "count", "exists" })
            test($"Azure execution: {operation}", () =>
            {
                var db = new FakeDatabase { JsonRows = new[] { "[{\"Id\":7,", "\"Name\":\"sample\"}]" } };
                var connector = new FakeConnector(db, DbFlavor.AzureSql) { Options = { ReuseTvpTypes = false } };
                ISqlResponse result;
                switch (operation)
                {
                    case "insert":
                        var insert = new InsertWithPrimaryKeyQuery<Item, int>().InsertAuto(new Item()).Execute(connector);
                        Check(insert.TouchedPrimaryKeys.SequenceEqual(new[] { 7 }), "Insert key lost"); result = insert; break;
                    case "update":
                        var update = new UpdateWithPrimaryKeyQuery<Item, int>().Update(x => x.Name, "new").Execute(connector);
                        Check(update.TouchedPrimaryKeys.SequenceEqual(new[] { 7 }), "Update key lost"); result = update; break;
                    case "delete":
                        var delete = new DeleteWithPrimaryKeyQuery<Item, int>().Execute(connector);
                        Check(delete.TouchedPrimaryKeys.SequenceEqual(new[] { 7 }), "Delete key lost"); result = delete; break;
                    case "output": result = new UpdateQuery<Item>().Update(x => x.Name, "new").Output(x => x.Id).ExecuteWithOutput(connector); break;
                    case "select":
                        var select = new SelectQuery<Item>().SelectRootColumns().Execute(connector);
                        Check(select.Result.Single().Name == "sample", "Chunked JSON was not deserialized"); result = select; break;
                    case "count":
                        var count = new SelectQuery<Item>().SelectRootColumns().Page(2, 1).ExecuteWithTotalCount(connector);
                        Check(count.TotalResults == 22 && db.Executed.Count == 2, "Total count missing"); result = count; break;
                    case "exists": result = new SelectPrimaryKeyExistsQuery<Item, int>(7).Execute(connector); break;
                    default: throw new InvalidOperationException(operation);
                }
                Check(result.State == ResponseState.Valid, result.Error?.ToString());
                Check(connector.DbFlavor == DbFlavor.AzureSql, "Execution changed flavor");
                Check(db.Executed.All(x => x.CommandTimeout == 15), "Command timeout not applied");
            });
    }

    private sealed class CteOutputSelect : SelectQuery<Item>
    {
        public CteOutputSelect(With cte) : base(cte.OutputTable)
        {
            SelectRootColumns();
            Withs.AddWith(cte);
        }
    }

    private sealed class CteSelect : SelectQuery<Item>
    {
        public CteSelect()
        {
            SelectRootColumns();
            Withs.AddWith(new With(new SelectQuery<Category>().SelectRootColumns().Top(2), "category_cte"));
        }
    }
}

[SqlTable("azure_parents")]
public class AzureParent
{
    [SqlPrimaryKey] public int Id { get; set; }
    [SqlOneToMany(nameof(AzureChild.ParentId))] public List<AzureChild> Children { get; set; }
}
[SqlTable("azure_children")]
public class AzureChild
{
    [SqlPrimaryKey] public int Id { get; set; }
    public int ParentId { get; set; }
    public string Name { get; set; }
}
