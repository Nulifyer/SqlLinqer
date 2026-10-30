using System.Data;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Components.Where;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class DatabaseEqualityChecks
{
    internal static void Run(BaseConnector connector, Action<string, Action> test)
    {
        string[] collations = connector.DbFlavor switch
        {
            DbFlavor.SqlServer or DbFlavor.AzureSql => ["Latin1_General_100_CI_AS", "Latin1_General_100_CS_AS"],
            DbFlavor.Sqlite => ["NOCASE", "RTRIM", "BINARY"],
            DbFlavor.DuckDb => ["", "NOCASE"],
            DbFlavor.MySql => ["utf8mb4_0900_ai_ci", "utf8mb4_0900_as_cs"],
            _ => ["C"],
        };
        foreach (string collation in collations)
            test("native database equality: root/nested DISTINCT, paging and totals, " + collation,
                () => Check(connector, collation).GetAwaiter().GetResult());
    }

    private static async Task Check(BaseConnector connector, string collation)
    {
        var flavor = connector.DbFlavor; string schema = connector.Options.DefaultSchema;
        string Q(string s) => s.DbWrap(flavor);
        string T(string s) => string.IsNullOrEmpty(schema) ? Q(s) : Q(schema) + "." + Q(s);
        using var connection = connector.CreateConnection(); await connection.OpenAsync();
        async Task Sql(string text)
        { using var c = connection.CreateCommand(); c.CommandText = text; await c.ExecuteNonQueryAsync(); }
        string type = flavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? "nvarchar(50)" : "varchar(50)";
        if (flavor == DbFlavor.MySql) type += " CHARACTER SET utf8mb4";
        if (collation.Length > 0) type += " COLLATE " + (flavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? collation : Q(collation));
        var created = new List<string>();
        bool viewCreated = false;
        try
        {
            await Sql($"CREATE TABLE {T("equality_roots")} ({Q("Id")} int,{Q("Name")} {type},{Q("OwnerId")} int)"); created.Add("equality_roots");
            await Sql($"CREATE TABLE {T("equality_children")} ({Q("Id")} int,{Q("ParentId")} int,{Q("Name")} {type})"); created.Add("equality_children");
            await Sql($"CREATE TABLE {T("equality_leaves")} ({Q("Id")} int,{Q("ParentId")} int,{Q("Name")} {type})"); created.Add("equality_leaves");
            await Sql($"INSERT INTO {T("equality_roots")} ({Q("Id")},{Q("Name")}) VALUES(1,'a'),(2,'A'),(3,'a '),(4,'b'),(5,'á'),(6,NULL),(7,NULL),(8,'1'),(9,'-1')");
            await Sql($"CREATE TABLE {T("equality_owners")} ({Q("Id")} int,{Q("Name")} {type})"); created.Add("equality_owners");
            string random = flavor switch
            {
                DbFlavor.SqlServer or DbFlavor.AzureSql => "CONVERT(nvarchar(50),NEWID())",
                DbFlavor.MySql => "CAST(RAND() AS CHAR(50))",
                _ => "CAST(random() AS varchar(50))"
            };
            await Sql($"CREATE VIEW {T("equality_volatile")} AS SELECT {Q("Id")},{random} AS {Q("Code")} FROM {T("equality_roots")} WHERE {Q("Id")}<=4");
            viewCreated = true;
            using var tx = connection.BeginTransaction();
            int expected = new SelectQuery<EqualityRoot>(schema).Select(x => x.Name).Distinct().Execute(connector, tx).GetResultOrThrow().Count;
            SelectQuery<EqualityRoot> Query(bool nested)
            {
                var q = new SelectQuery<EqualityRoot>(schema).Select(x => x.Name).Select(x => x.Children).Distinct().OrderBy(x => x.Id, SqlDir.ASC);
                var child = q.Selects.GetAll<SelectRelationship>().Single().SubQuery;
                if (nested) q.Select(x => x.Children.First().Leaves);
                KeepName(child);
                if (nested) KeepName(child.Selects.GetAll<SelectRelationship>().Single().SubQuery);
                return q;
            }
            async Task Verify(int count, bool nested)
            {
                foreach (string mode in new[] { "sync", "async", "stream" })
                {
                    var q = Query(nested); List<EqualityRoot> rows;
                    if (mode == "stream") { rows = []; await foreach (var row in q.ExecuteAsyncStream(connector, tx)) rows.Add(row); }
                    else rows = (mode == "sync" ? q.Execute(connector, tx) : await q.ExecuteAsync(connector, tx)).GetResultOrThrow();
                    Expect(rows.Count == count, "Graph DISTINCT differs from database scalar equality, " + mode);
                    Expect(rows.Any(r => r.Name == "1") && rows.Any(r => r.Name == "-1"), "Hash collision merged different scalar values");
                    q.Page(1, 1);
                    var page = mode == "sync" ? q.ExecuteWithTotalCount(connector, tx) : await q.ExecuteWithTotalCountAsync(connector, tx);
                    Expect(page.GetResultOrThrow().Count == 1 && page.TotalResults == count, "DISTINCT page/count differs, " + mode);
                }
            }
            await Verify(9, false);
            // Child values do not change the distinct parent row projection.
            using (var c = connection.CreateCommand())
            {
                c.Transaction = tx; c.CommandText = $"INSERT INTO {T("equality_children")} SELECT {Q("Id")},{Q("Id")},{Q("Name")} FROM {T("equality_roots")};"
                    + $"INSERT INTO {T("equality_leaves")} SELECT {Q("Id")},{Q("Id")},{Q("Name")} FROM {T("equality_roots")};";
                await c.ExecuteNonQueryAsync();
            }
            await Verify(9, true);
            var childrenOnly = new SelectQuery<EqualityRoot>(schema).Select(x => x.Children).Distinct().OrderBy(x => x.Id, SqlDir.ASC);
            KeepName(childrenOnly.Selects.GetAll<SelectRelationship>().Single().SubQuery);
            Expect(childrenOnly.Execute(connector, tx).GetResultOrThrow().Count == 9, "Child-only graph lost parent relationship keys");
            using (var c = connection.CreateCommand())
            {
                c.Transaction = tx; c.CommandText = $"INSERT INTO {T("equality_children")} SELECT {Q("Id")}+10,{Q("Id")},UPPER({Q("Name")}) FROM {T("equality_roots")}";
                await c.ExecuteNonQueryAsync();
            }
            var expectedChildren = new Dictionary<int, int>();
            for (int id = 1; id <= 9; id++)
                expectedChildren[id] = new SelectQuery<EqualityChild>(schema).Select(x => x.Name).Where(x => x.ParentId, id, SqlOp.EQ)
                    .Distinct().Execute(connector, tx).GetResultOrThrow().Count;
            foreach (string mode in new[] { "sync", "async", "stream" })
            {
                var q = new SelectQuery<EqualityRoot>(schema).Select(x => x.Id).Select(x => x.Children).OrderBy(x => x.Id, SqlDir.ASC);
                var child = q.Selects.GetAll<SelectRelationship>().Single().SubQuery;
                KeepName(child); child.Distinct = true;
                var rows = new List<EqualityRoot>();
                if (mode == "stream") { await foreach (var row in q.ExecuteAsyncStream(connector, tx)) rows.Add(row); }
                else rows = (mode == "sync" ? q.Execute(connector, tx) : await q.ExecuteAsync(connector, tx)).GetResultOrThrow();
                Expect(rows.Count == 9 && rows.All(row => row.Children.Count == expectedChildren[row.Id]), "Nested DISTINCT differs from database equality, " + mode);
            }
            using (var c = connection.CreateCommand())
            {
                c.Transaction = tx;
                c.CommandText = $"INSERT INTO {T("equality_roots")} ({Q("Id")},{Q("Name")}) VALUES(10,'a'),(10,'a ');"
                    + $"INSERT INTO {T("equality_children")} VALUES(100,10,'x')";
                await c.ExecuteNonQueryAsync();
            }
            foreach (string mode in new[] { "sync", "async", "stream" })
            {
                SelectQuery<EqualityRoot> AncestorQuery(bool missingOwner)
                {
                    var q = new SelectQuery<EqualityRoot>(schema).Select(x => x.Children).Where(x => x.Id, 10, SqlOp.EQ).Distinct();
                    if (missingOwner) q.OrderBy(x => x.Owner.Name, SqlDir.ASC);
                    else q.OrderBy(x => x.Name, SqlDir.ASC);
                    var child = q.Selects.GetAll<SelectRelationship>().Single().SubQuery;
                    child.Selects.Clear(); child.Distinct = true;
                    var input = missingOwner ? q.Table.FindColumnFromPath(new[] { nameof(EqualityRoot.Owner), nameof(EqualityOwner.Name) })
                        : q.Table.Columns.TryGet(nameof(EqualityRoot.Name));
                    child.Selects.AddStatement(new SelectFuncStatement(missingOwner ? SqlFunc.COALESCE : SqlFunc.CONCAT,
                        nameof(EqualityChild.Name), input, missingOwner ? "unknown" : "x"));
                    child.RootWhere.AddComponent(new WhereColumnValue(child.Table.Columns.TryGet(nameof(EqualityChild.Name)), "x", SqlOp.EQ, SqlSubQueryOp.ANY));
                    return q;
                }
                async Task<List<EqualityRoot>> Read(SelectQuery<EqualityRoot> q)
                {
                    if (mode != "stream") return (mode == "sync" ? q.Execute(connector, tx) : await q.ExecuteAsync(connector, tx)).GetResultOrThrow();
                    var rows = new List<EqualityRoot>(); await foreach (var row in q.ExecuteAsyncStream(connector, tx)) rows.Add(row); return rows;
                }
                var spelled = await Read(AncestorQuery(false));
                int spellings = new SelectQuery<EqualityRoot>(schema).Select(x => x.Name).Where(x => x.Id, 10, SqlOp.EQ).Distinct().Execute(connector, tx).GetResultOrThrow().Count;
                Expect(spelled.Count == spellings && spelled.All(r => r.Children.Count == 1),
                    "Ancestor inputs collapsed before CONCAT or child constants were bound incorrectly, " + mode);
                var missing = await Read(AncestorQuery(true));
                Expect(missing.Count == 1 && missing[0].Children.Single().Name == "unknown", "Null-extended ancestor input lost its equality class, " + mode);
                var volatileQuery = new SelectQuery<VolatileRoot>(schema).Select(x => x.Code).Select(x => x.Children).Distinct();
                List<VolatileRoot> values;
                if (mode == "stream") { values = []; await foreach (var row in volatileQuery.ExecuteAsyncStream(connector, tx)) values.Add(row); }
                else values = (mode == "sync" ? volatileQuery.Execute(connector, tx) : await volatileQuery.ExecuteAsync(connector, tx)).GetResultOrThrow();
                Expect(values.Count == 4 && values.All(r => !string.IsNullOrEmpty(r.Code)), "Volatile view values were reevaluated for equality, " + mode);
                await foreach (var row in AncestorQuery(false).ExecuteAsyncStream(connector, tx)) { Expect(row.Children.Count == 1, "Incomplete distinct root at early stop"); break; }
                Expect((await Read(AncestorQuery(false))).Count == spellings && tx.Connection == connection, "Early stop left query resources or changed caller ownership");
            }
            var cteQuery = new SelectQuery<EqualityRoot>(schema).Select(x => x.Id).Select(x => x.Children)
                .Where(x => x.Children.First().Id, Enumerable.Range(1, 900).ToArray(), SqlArrayOp.IN).Distinct();
            var ctePage = await cteQuery.Page(1, 1).ExecuteWithTotalCountAsync(connector, tx);
            Expect(ctePage.GetResultOrThrow().Count == 1 && ctePage.TotalResults == 10, "Shared CTE bindings broke DISTINCT graph selection/count");
            foreach (string mode in new[] { "sync", "async", "stream" })
            {
                var mixed = new SelectQuery<EqualityRoot>(schema).Select(x => x.Id).Select(x => x.Children).Select(x => x.Children.First().Leaves).Select(x => x.Owner)
                    .Where(x => x.Id, 10, SqlOp.EQ);
                var child = mixed.Selects.GetAll<SelectRelationship>().Single(r => r.Alias == nameof(EqualityRoot.Children)).SubQuery;
                KeepName(child); child.Distinct = true;
                List<EqualityRoot> rows;
                if (mode == "stream") { rows = []; await foreach (var row in mixed.ExecuteAsyncStream(connector, tx)) rows.Add(row); }
                else rows = (mode == "sync" ? mixed.Execute(connector, tx) : await mixed.ExecuteAsync(connector, tx)).GetResultOrThrow();
                Expect(rows.Count == 2 && rows.All(r => r.Owner == null && r.Children.Single().Name == "x"), "Cached and root-driven sibling branches differ");
            }
            foreach (bool distinct in new[] { false, true })
                foreach (string mode in new[] { "sync", "async", "stream" })
                {
                    var aggregate = new SelectQueryInitializer<EqualityRoot>(schema).BuildQuery(q => new
                    {
                        Owner = q.Select(x => x.Owner),
                        Count = q.Count(x => x.Id),
                        Total = q.Sum(x => x.Id)
                    });
                    if (distinct) aggregate.Distinct();
                    if (mode == "stream")
                    {
                        int groups = 0;
                        await foreach (var row in aggregate.ExecuteAsyncStream(connector, tx))
                        { Expect(row.Owner == null && row.Count == 11 && row.Total == 65, "Hidden inputs split an aggregate group"); groups++; }
                        Expect(groups == 1, "Wrong aggregate group count");
                    }
                    else
                    {
                        var row = (mode == "sync" ? aggregate.Execute(connector, tx) : await aggregate.ExecuteAsync(connector, tx)).GetResultOrThrow().Single();
                        Expect(row.Owner == null && row.Count == 11 && row.Total == 65, "Hidden aggregate arguments became dimensions");
                    }
                }
            tx.Rollback();
        }
        finally
        {
            if (viewCreated) await Sql($"DROP VIEW {T("equality_volatile")}");
            foreach (string table in created.AsEnumerable().Reverse()) await Sql($"DROP TABLE {T(table)}");
        }
    }
    private static void KeepName(SqlLinqer.Queries.Core.SelectQuery query)
    {
        var relationships = query.Selects.GetAll<SelectRelationship>().ToArray();
        var name = query.Table.Columns.GetAll().Single(c => c.ColumnName == "Name");
        query.Selects.Clear(); query.Selects.AddStatement(new SelectColumn(name, "Name"));
        foreach (var relation in relationships) query.Selects.AddStatement(relation);
    }
    private static void Expect(bool ok, string message) { if (!ok) throw new Exception(message); }
    [SqlTable("equality_roots")]
    private sealed class EqualityRoot
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
        public int? OwnerId { get; set; }
        [SqlOneToOne(nameof(OwnerId))] public EqualityOwner Owner { get; set; }
        [SqlOneToMany(nameof(EqualityChild.ParentId))] public List<EqualityChild> Children { get; set; } = [];
    }
    [SqlTable("equality_owners")]
    private sealed class EqualityOwner
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
    }
    [SqlTable("equality_volatile")]
    private sealed class VolatileRoot
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Code { get; set; }
        [SqlOneToMany(nameof(EqualityChild.ParentId))] public List<EqualityChild> Children { get; set; } = [];
    }
    [SqlTable("equality_children")]
    private sealed class EqualityChild
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public int ParentId { get; set; }
        public string Name { get; set; }
        [SqlOneToMany(nameof(EqualityLeaf.ParentId))] public List<EqualityLeaf> Leaves { get; set; } = [];
    }
    [SqlTable("equality_leaves")]
    private sealed class EqualityLeaf
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public int ParentId { get; set; }
        public string Name { get; set; }
    }
}
