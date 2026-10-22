using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Select;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using Root = SqlLinqer.Tests.Regression.ComplexRelationshipChecks.Root;
using Person = SqlLinqer.Tests.Regression.ComplexRelationshipChecks.Person;
using RelationItem = SqlLinqer.Tests.Regression.ComplexRelationshipChecks.Item;

namespace SqlLinqer.Tests.Regression;

internal static class AdvancedProjectionChecks
{
    // Uses the populated complex relationship fixture, without changing its rows.
    internal static void Run(BaseConnector connector, Action<string, Action> test)
    {
        string schema = connector.Options.DefaultSchema;
        test("aggregate grouping: equal computed inputs share one group", () =>
        {
            string table = "complex_roots".DbWrap(connector.DbFlavor);
            if (!string.IsNullOrEmpty(schema)) table = schema.DbWrap(connector.DbFlavor) + "." + table;
            string id = "Id".DbWrap(connector.DbFlavor), name = "Name".DbWrap(connector.DbFlavor);
            void Sql(string sql)
            {
                var response = connector.ExecuteNonQuery(new(sql));
                if (response.State != ResponseState.Valid) throw response.Error;
            }
            Sql($"INSERT INTO {table} ({id},{name}) VALUES (-4,'negative'),(4,'positive')");
            try
            {
                var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new
                {
                    Text = q.Concat(x => q.ABS(y => y.Id), x => ":", x => q.Count())
                }).Where(x => x.Id, new[] { -4, 4 }, SqlArrayOp.IN);
                foreach (string mode in new[] { "sync", "async", "stream" })
                    Check(ComplexRelationshipChecks.Read(query, connector, mode).Single().Text == "4:2",
                        "Computed grouping split equal outputs by their source columns");
            }
            finally { Sql($"DELETE FROM {table} WHERE {id} IN (-4,4)"); }
        });
        foreach (string mode in new[] { "sync", "async", "stream" })
        {
            test($"CASE grouping: returned columns and equivalent branches, {mode}", () =>
            {
                var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new
                {
                    Label = q.Case<string>(c => c.AddCaseColumn(x => x.Name,
                        w => w.Where(x => x.OwnerId, (int?)0, SqlOp.GT)).AddElseValue("other")),
                    Count = q.Count()
                });
                var rows = ComplexRelationshipChecks.Read(query, connector, mode);
                Check(rows.Count == 2 && rows.Single(x => x.Label == "same").Count == 2
                    && rows.Single(x => x.Label == "other").Count == 1, "CASE groups must follow the returned expression");
                var page = query.OrderBy(x => x.Label, SqlDir.ASC).Page(1, 1).ExecuteWithTotalCount(connector);
                Check(page.State == ResponseState.Valid && page.TotalResults == 2 && page.Result.Single().Label == "other",
                    page.Error?.ToString() ?? "CASE grouped paging/count differs");
            });
            test($"CASE grouping: aggregate branches nested inside scalar functions, {mode}", () =>
            {
                var table = Table.GetCached<Root>(schema);
                var count = new SelectFuncStatement(SqlFunc.COUNT, "Count", table.PrimaryKey);
                var query = new SelectQueryInitializer<Root>(table).BuildQuery(q => new
                {
                    Text = q.Concat(x => q.Case<int>(c => c.AddCaseValue(count,
                        w => w.Where(x => x.Name, "same", SqlOp.EQ)).AddElseValue(0)), x => " rows")
                });
                var rows = ComplexRelationshipChecks.Read(query, connector, mode);
                Check(rows.Count == 2 && rows.Any(x => x.Text == "2 rows") && rows.Any(x => x.Text == "0 rows"),
                    "CASE aggregate branch was not detected through its scalar wrapper");
            });
            test($"relationship grouping: parent predicates and composite correlations, {mode}", () =>
            {
                var query = new SelectQueryInitializer<CorrelatedRoot>(schema).BuildQuery(q => new
                {
                    Owner = q.Select(x => x.Owner),
                    Items = q.Select(x => x.Items),
                    Members = q.Select(x => x.Members),
                    Count = q.Count()
                }).OrderBy(x => x.Owner.Name, SqlDir.ASC);
                var rows = ComplexRelationshipChecks.Read(query, connector, mode);
                Check(rows.Count == 3 && rows.All(x => x.Count == 1), "Correlated relationship groups changed cardinality");
                var alice = rows.Single(x => x.Owner?.Name == "Alice");
                Check(alice.Items.Single().Name == "first" && alice.Members.Count == 2,
                    "Composite collection correlation lost its second key");
                Check(rows.Single(x => x.Owner?.Name == "Bob").Items.Count == 0
                    && rows.Single(x => x.Owner == null).Members.Count == 0, "Null/empty correlated projection differs");
                var page = query.Page(1, 2).ExecuteWithTotalCount(connector);
                Check(page.State == ResponseState.Valid && page.TotalResults == 3 && page.Result.Count == 2,
                    page.Error?.ToString() ?? "Grouped relationship ordering changed counts or paging");
            });
            test($"BuildQuery ordering: nested aliases, repeated paths, paging and counts, {mode}", () =>
            {
                var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new
                {
                    Id = q.Select(x => x.Id),
                    Main = q.Select(x => x.Owner, p => p.Manager.Manager),
                    Other = q.Select(x => x.Backup)
                }).OrderBy(x => x.Main.Manager.Manager.Name, SqlDir.DESC)
                    .ThenBy(x => x.Other.Name, SqlDir.DESC).ThenBy(x => x.Id, SqlDir.ASC).Page(1, 2);
                var rows = ComplexRelationshipChecks.Read(query, connector, mode);
                int[] expected = connector.DbFlavor == DbFlavor.PostgreSql ? new[] { 3, 2, 1 } : new[] { 1, 2, 3 };
                Check(rows.Select(x => x.Id).SequenceEqual(expected.Take(2)), "Nested ordering changed root rows or alias resolution");
                var page = query.ExecuteWithTotalCount(connector);
                Check(page.State == ResponseState.Valid && page.TotalResults == 3 && page.Result.Count == 2,
                    page.Error?.ToString() ?? "Nested ordering changed the total count");
                var second = query.Page(2, 2);
                Check(ComplexRelationshipChecks.Read(second, connector, mode).Single().Id == expected[2],
                    "Missing relationship was lost during nested ordering");
            });
            test($"BuildQuery ordering: collection members preserve root cardinality, {mode}", () =>
            {
                var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new
                {
                    Id = q.Select(x => x.Id),
                    Team = q.Select(x => x.Members)
                }).OrderBy(x => x.Team.First().Name, SqlDir.ASC).ThenBy(x => x.Id, SqlDir.ASC);
                var rows = ComplexRelationshipChecks.Read(query, connector, mode);
                Check(rows.Count == 3 && rows.Where(x => x.Team.Count != 0).Select(x => x.Id).SequenceEqual(new[] { 1, 2 })
                    && rows.Single(x => x.Id == 3).Team.Count == 0, "Collection ordering multiplied or removed root rows");
            });
            test($"BuildQuery paging: distinct scalar values and deterministic ties, {mode}", () =>
            {
                var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new { Label = q.Select(x => x.Name) })
                    .Distinct().OrderBy(x => x.Label, SqlDir.ASC).Page(2, 1);
                Check(ComplexRelationshipChecks.Read(query, connector, mode).Single().Label == "same", "DISTINCT paging differs");
                var page = query.ExecuteWithTotalCount(connector);
                Check(page.State == ResponseState.Valid && page.TotalResults == 2, page.Error?.ToString() ?? "DISTINCT count differs");
                var tied = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new { Id = q.Select(x => x.Id), Label = q.Select(x => x.Name) })
                    .OrderBy(x => x.Label, SqlDir.DESC).ThenBy(x => x.Id, SqlDir.DESC).Page(2, 1);
                Check(ComplexRelationshipChecks.Read(tied, connector, mode).Single().Id == 1, "Tie-breaking paging differs");
            });
            test($"CASE grouping: related ELSE columns and aggregate argument, {mode}", () =>
            {
                var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new
                {
                    Label = q.Case<string>(c => c.AddCaseValue("missing", w => w.Where(x => x.OwnerId, SqlNull.NULL, SqlOp.EQ))
                        .AddElseColumn(x => x.Owner.Name)),
                    Count = q.Count()
                });
                var rows = ComplexRelationshipChecks.Read(query, connector, mode);
                Check(rows.Count == 3 && rows.All(x => x.Count == 1) && rows.Any(x => x.Label == "Alice")
                    && rows.Any(x => x.Label == "missing"), "Related CASE return column or join is missing");
                var conditional = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new
                {
                    Count = q.Sum(x => q.Case<int>(c => c.AddCaseValue(1, w => w.Where(y => y.Name, "same", SqlOp.EQ)).AddElseValue(0)))
                });
                Check(ComplexRelationshipChecks.Read(conditional, connector, mode).Single().Count == 2,
                    "CASE inside an aggregate must retain its per-row inputs");
            });
        }
        test("BuildQuery ordering: unselected paths and scalar member suffixes fail explicitly", () =>
        {
            var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new { Main = q.Select(x => x.Owner) })
                .OrderBy(x => x.Main.Name, SqlDir.ASC);
            string before = query.Render(connector).Text;
            ThrowsPath(() => query.OrderBy(x => x.Main.Manager.Name, SqlDir.ASC));
            ThrowsPath(() => query.OrderBy(x => x.Main.Name.Length, SqlDir.ASC));
            Check(query.Render(connector).Text == before, "Invalid ordering changed the query or repeated rendering changed aliases");
        });
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void ThrowsPath(Action action)
    {
        try { action(); }
        catch (SqlLinqer.Exceptions.SqlPathNotFoundException) { return; }
        throw new Exception("Expected an invalid projected ordering path to fail");
    }

    [SqlTable("complex_roots")]
    private sealed class CorrelatedRoot
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
        public int? OwnerId { get; set; }
        [SqlOneToOne(nameof(OwnerId)), SqlConditionParentColumnConstant(nameof(Name), "same", SqlOp.EQ)]
        public Person Owner { get; set; }
        [SqlOneToMany(nameof(RelationItem.RootId)), SqlConditionTwoColumn(nameof(OwnerId), nameof(RelationItem.PersonId), SqlOp.EQ)]
        public List<RelationItem> Items { get; set; }
        [SqlManyToMany("complex_memberships", "RootId", "PersonId"), SqlConditionParentColumnConstant(nameof(Name), "same", SqlOp.EQ)]
        public List<Person> Members { get; set; }
    }
}
