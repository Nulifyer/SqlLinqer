using SqlLinqer.Components.Render;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Select;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Interfaces;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class ComplexRelationshipChecks
{
    internal static void RunRendering(Action<string, Action> test)
    {
        foreach (var flavor in Enum.GetValues<DbFlavor>())
        {
            test($"CASE rendering retains member aliases inside relationship subqueries, {flavor}", () =>
            {
                var query = new SelectQuery<Root>();
                var relationship = new SelectRelationship(query, Table.GetCached<Root>().Relationships.TryGet(nameof(Root.Owner)));
                var conditional = new CaseStatement<Person>(relationship.Relationship.ChildTable, "Label")
                    .AddCaseColumn(x => x.Name, w => w.Where(x => x.Id, 1, SqlOp.EQ)).AddElseValue("other");
                var parameters = new ParameterCollection(flavor);
                string json = conditional.Render(relationship.SubQuery, parameters, flavor, true);
                Check(json.Contains(" AS " + "Label".DbWrap(flavor)), "Nested CASE lost its native output alias");
            });
            test($"aggregate concat rendering retains scalar arguments and grouping bindings, {flavor}", () =>
            {
                var table = Table.GetCached<Root>();
                var query = new SelectQuery<Root>();
                var upper = new SelectFuncStatement(SqlFunc.UPPER, "Label", table.Columns.TryGet(nameof(Root.Name)));
                var concat = new SelectFuncStatement(SqlFunc.CONCAT, "Label", upper, "!");
                var parameters = new ParameterCollection(flavor);
                string scalar = concat.Render(query, parameters, flavor, false);
                string json = concat.Render(query, parameters, flavor, true);
                string grouping = concat.RenderForGroupBy(query, parameters, flavor).Single();
                Check(json.Contains(scalar), "JSON wrapper changed the nested scalar function");
                Check(grouping == scalar && parameters.Count == 1, "Repeated scalar/group expression changed parameter identity");
                var aggregate = new SelectQueryInitializer<Root>().BuildQuery(q => new
                {
                    Label = q.Concat(x => q.UPPER(y => y.Name), x => "!"),
                    Count = q.Count()
                });
                Check(aggregate.Render(flavor).Parameters.Count == 2, "Aggregate grouping added duplicate bindings");
                var mixed = new SelectQueryInitializer<Root>().BuildQuery(q => new
                {
                    Text = q.Concat(x => q.UPPER(y => y.Name), x => ":", x => q.Count())
                }).Render(flavor);
                Check(mixed.Text.Contains(flavor == DbFlavor.MySql ? "CROSS JOIN LATERAL (SELECT UPPER(" : "GROUP BY UPPER(")
                    && !mixed.Text.Split("GROUP BY").Last().Contains("COUNT("),
                    "Mixed aggregate expression failed to group only its scalar inputs");
            });
        }
    }

    internal static void Run(BaseConnector connector, Action<string, Action> test)
    {
        string Q(string name) => name.DbWrap(connector.DbFlavor);
        string T(string name) => string.IsNullOrEmpty(connector.Options.DefaultSchema) ? Q(name)
            : Q(connector.Options.DefaultSchema) + "." + Q(name);
        string schema = connector.Options.DefaultSchema;
        void Sql(string sql) => Result(connector.ExecuteNonQuery(new(sql)));
        Sql($"CREATE TABLE {T("complex_roots")} ({Q("Id")} INTEGER PRIMARY KEY, {Q("Name")} VARCHAR(40), {Q("OwnerId")} INTEGER, {Q("BackupId")} INTEGER);" +
            $"CREATE TABLE {T("complex_people")} ({Q("Id")} INTEGER PRIMARY KEY, {Q("Name")} VARCHAR(40), {Q("ManagerId")} INTEGER, {Q("RegionId")} INTEGER);" +
            $"CREATE TABLE {T("complex_regions")} ({Q("Id")} INTEGER PRIMARY KEY, {Q("Name")} VARCHAR(40));" +
            $"CREATE TABLE {T("complex_items")} ({Q("Id")} INTEGER PRIMARY KEY, {Q("RootId")} INTEGER, {Q("PersonId")} INTEGER, {Q("Name")} VARCHAR(40));" +
            $"CREATE TABLE {T("complex_memberships")} ({Q("RootId")} INTEGER, {Q("PersonId")} INTEGER);" +
            $"INSERT INTO {T("complex_roots")} VALUES (1,'same',1,2),(2,'same',2,1),(3,'other',NULL,NULL);" +
            $"INSERT INTO {T("complex_people")} VALUES (1,'Alice',2,1),(2,'Bob',3,2),(3,'Carl',NULL,NULL),(4,'Dave',1,1);" +
            $"INSERT INTO {T("complex_regions")} VALUES (1,'North'),(2,'South');" +
            $"INSERT INTO {T("complex_items")} VALUES (1,1,1,'first'),(2,1,2,'second'),(3,2,1,'third');" +
            $"INSERT INTO {T("complex_memberships")} VALUES (1,1),(1,2),(2,2);");
        try
        {
            AdvancedProjectionChecks.Run(connector, test);
            foreach (string mode in new[] { "sync", "async", "stream" })
            {
                test($"BuildQuery relationships: scalar child selections retain object shape, {mode}", () =>
                {
                    var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new
                    {
                        Id = q.Select(x => x.Id),
                        Main = q.Select(x => x.Owner, person => person.Name, person => person.Region.Id)
                    }).OrderBy(x => x.Id, SqlDir.ASC);
                    var rows = Read(query, connector, mode);
                    Check(rows[0].Main.Name == "Alice" && rows[0].Main.Region.Id == "1" && rows[2].Main == null,
                        "Child scalar selection replaced the projected relationship object");
                });
                test($"BuildQuery relationships: repeated path names and aliases stay independent, {mode}", () =>
                {
                    var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new
                    {
                        Id = q.Select(x => x.Id),
                        First = q.Select(x => x.Owner, person => person.Manager.Manager),
                        Second = q.Select(x => x.Owner, person => person.Region),
                        Other = q.Select(x => x.Backup, person => person.Manager.Region)
                    }).OrderBy(x => x.Id, SqlDir.ASC);
                    var rows = Read(query, connector, mode);
                    Check(rows[0].First.Manager.Manager.Name == "Carl" && rows[0].Second.Region.Name == "North"
                        && rows[0].Other.Name == "Bob" && rows[0].Other.Manager.Region == null,
                        "Repeated relationship members or projection aliases collapsed");
                    Check(rows[2].First == null && rows[2].Second == null && rows[2].Other == null, "Null projected relationships differ");
                });
                test($"BuildQuery relationships: direct deep objects and collections use selected shape, {mode}", () =>
                {
                    var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new
                    {
                        Id = q.Select(x => x.Id),
                        Boss = q.Select(x => x.Owner.Manager),
                        Team = q.Select(x => x.Owner.Reports),
                        Members = q.Select(x => x.Members, people => people.First().Reports.First().Region)
                    }).OrderBy(x => x.Id, SqlDir.ASC);
                    var rows = Read(query, connector, mode);
                    Check(rows.Count == 3 && rows[0].Boss.Name == "Bob" && rows[0].Team.Single().Name == "Dave"
                        && rows[0].Members.Single(x => x.Name == "Alice").Reports.Single().Region.Name == "North",
                        "Deep projected relationship returned intermediate ancestors");
                    Check(rows[2].Boss == null && rows[2].Team.Count == 0 && rows[2].Members.Count == 0, "Deep projected null/empty shape differs");
                });
                test($"complex relationships: repeated model types across four levels, {mode}", () =>
                {
                    var query = new SelectQuery<Root>(schema).SelectRootColumns()
                        .Select(x => x.Owner.Manager.Manager.Name).Select(x => x.Backup.Manager.Name)
                        .Select(x => x.Items).Select(x => x.Items.First().Person.Region)
                        .Select(x => x.Owner.Reports).Select(x => x.Owner.Reports.First().Region.Name)
                        .OrderBy(x => x.Id, SqlDir.ASC);
                    var rows = Read(query, connector, mode);
                    Check(rows.Count == 3 && rows[0].Owner.Manager.Manager.Name == "Carl" && rows[0].Backup.Manager.Name == "Carl", "Deep single relationships lost their occurrence");
                    Check(rows[0].Items.Count == 2 && rows[0].Items.Single(x => x.Id == 1).Person.Region.Name == "North"
                        && rows[0].Items.Single(x => x.Id == 2).Person.Region.Name == "South"
                        && rows[0].Items.Single(x => x.Id == 2).Person.Region.Id == "2", "Nested collection/single relationships or numeric string keys differ");
                    Check(rows[0].Owner.Reports.Single().Name == "Dave" && rows[0].Owner.Reports.Single().Region.Name == "North", "Self-referencing child collection differs");
                    Check(rows[2].Owner == null && rows[2].Backup == null && rows[2].Items.Count == 0, "Deep null/empty relationships differ");
                });
                test($"complex relationships: nested collection filters and total count, {mode}", () =>
                {
                    var query = new SelectQuery<Root>(schema).SelectRootColumns()
                        .Where(x => x.Items.First().Person.Reports.First().Region.Name, "North", SqlOp.EQ)
                        .Where(x => x.Items.First().Name, "first", SqlOp.EQ).OrderBy(x => x.Id, SqlDir.ASC).Page(1, 1);
                    Check(Read(query, connector, mode).Select(x => x.Id).SequenceEqual(new[] { 1 }), "Nested many-to-many traversal filter differs");
                    var response = query.ExecuteWithTotalCount(connector);
                    Check(Result(response).Single().Id == 1 && response.TotalResults == 1, "Nested relationship total-count result differs");
                });
                test($"complex relationships: many-to-many followed by collection and single relation, {mode}", () =>
                {
                    var query = new SelectQuery<Root>(schema).SelectRootColumns().Select(x => x.Members)
                        .Select(x => x.Members.First().Reports).Select(x => x.Members.First().Reports.First().Region.Name)
                        .OrderBy(x => x.Id, SqlDir.ASC);
                    var rows = Read(query, connector, mode);
                    Check(rows[0].Members.Count == 2 && rows[2].Members.Count == 0, "Many-to-many collection cardinality differs");
                    Check(rows[0].Members.Single(x => x.Name == "Alice").Reports.Single().Region.Name == "North", "Nested many-to-many values differ");
                    var filtered = new SelectQuery<Root>(schema).SelectRootColumns()
                        .Where(x => x.Members.First().Reports.First().Region.Name, "North", SqlOp.EQ)
                        .Where(x => x.Members.First().Name, "Alice", SqlOp.EQ).Page(1, 1);
                    Check(Read(filtered, connector, mode).Single().Id == 1, "Nested many-to-many filter differs");
                    var response = filtered.ExecuteWithTotalCount(connector);
                    Check(Result(response).Single().Id == 1 && response.TotalResults == 1, "Nested many-to-many count differs");
                });
                test($"BuildQuery relationships: deep object combined with grouped concat, {mode}", () =>
                {
                    var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new
                    {
                        Boss = q.Select(x => x.Owner.Manager, manager => manager.Region),
                        Label = q.Concat(x => q.UPPER(y => y.Name), x => "!"),
                        Count = q.Count()
                    });
                    var rows = Read(query, connector, mode);
                    Check(rows.Count == 3 && rows.All(x => x.Count == 1)
                        && rows.Single(x => x.Boss?.Name == "Bob").Boss.Region.Name == "South"
                        && rows.Single(x => x.Boss?.Name == "Carl").Label == "SAME!"
                        && rows.Single(x => x.Boss == null).Label == "OTHER!", "Relationship projection and aggregate grouping disagree");
                    var response = query.OrderBy(x => x.Label, SqlDir.ASC).Page(1, 1).ExecuteWithTotalCount(connector);
                    Check(Result(response).Count == 1 && response.TotalResults == 3, "Grouped relationship projection count differs");
                });
                test($"aggregate concat: parameters, nested functions, paging and count, {mode}", () =>
                {
                    var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new
                    {
                        Label = q.Concat(x => q.UPPER(y => y.Name), x => "!"),
                        Count = q.Count()
                    });
                    var rows = Read(query, connector, mode);
                    Check(rows.Count == 2 && rows.Single(x => x.Label == "SAME!").Count == 2 && rows.Single(x => x.Label == "OTHER!").Count == 1, "Concat grouping changed aggregate values");
                    var page = query.OrderBy(x => x.Label, SqlDir.ASC).Page(1, 1).ExecuteWithTotalCount(connector);
                    Check(Result(page).Single().Label == "OTHER!" && page.TotalResults == 2, "Concat grouped paging/count differs");
                });
                test($"aggregate concat: aggregate nested inside scalar function, {mode}", () =>
                {
                    var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new
                    {
                        Label = q.Select(x => x.Name),
                        Text = q.Concat(x => q.Count(), x => " rows")
                    });
                    var rows = Read(query, connector, mode);
                    Check(rows.Count == 2 && rows.Single(x => x.Label == "same").Text == "2 rows", "Scalar aggregate wrapper changed grouping");
                });
                test($"aggregate concat: mixed scalar and aggregate arguments infer grouping, {mode}", () =>
                {
                    var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new
                    {
                        Text = q.Concat(x => q.UPPER(y => y.Name), x => ":", x => q.Count())
                    });
                    var rows = Read(query, connector, mode);
                    Check(rows.Count == 2 && rows.Any(x => x.Text == "SAME:2") && rows.Any(x => x.Text == "OTHER:1"),
                        "Mixed scalar/aggregate expression did not group its scalar inputs");
                    var page = query.OrderBy(x => x.Text, SqlDir.ASC).Page(1, 1).ExecuteWithTotalCount(connector);
                    Check(Result(page).Single().Text == "OTHER:1" && page.TotalResults == 2, "Mixed scalar/aggregate total count differs");
                });
                test($"aggregate concat: group across deep sibling relationships, {mode}", () =>
                {
                    var query = new SelectQueryInitializer<Root>(schema).BuildQuery(q => new
                    {
                        Label = q.ConcatWS("|", x => x.Owner.Manager.Region.Name, x => x.Backup.Name),
                        Count = q.Count()
                    });
                    var rows = Read(query, connector, mode);
                    Check(rows.Count == 3 && rows.All(x => x.Count == 1) && rows.Any(x => x.Label == "South|Bob")
                        && rows.Any(x => x.Label == "Alice") && rows.Any(x => x.Label == ""), "Grouped deep relationship values differ");
                });
                test($"SQL JSON: numeric database column loads into string model, {mode}", () =>
                {
                    var query = new SelectQuery<StringRoot>(schema).SelectRootColumns().OrderBy(x => x.Id, SqlDir.ASC);
                    Check(Read(query, connector, mode).Select(x => x.Id).SequenceEqual(new[] { "1", "2", "3" }), "Numeric database values no longer load as text");
                });
            }
        }
        finally { Sql($"DROP TABLE {T("complex_memberships")}; DROP TABLE {T("complex_items")}; DROP TABLE {T("complex_roots")}; DROP TABLE {T("complex_people")}; DROP TABLE {T("complex_regions")};"); }
    }

    internal static List<T> Read<T>(IQueryExecutable<List<T>> query, BaseConnector connector, string mode)
    {
        if (mode == "sync") return Result(query.Execute(connector));
        if (mode == "async") return Result(((IAsyncQueryExecutable<List<T>>)query).ExecuteAsync(connector).GetAwaiter().GetResult());
        async Task<List<T>> Collect()
        {
            var rows = new List<T>();
            await foreach (var row in ((IAsyncQueryExecutableStream<T>)query).ExecuteAsyncStream(connector)) rows.Add(row);
            return rows;
        }
        return Collect().GetAwaiter().GetResult();
    }
    private static T Result<T>(SqlResponse<T> response) => response.State == ResponseState.Valid ? response.Result : throw response.Error;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    [SqlTable("complex_roots")]
    internal sealed class Root
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
        public int? OwnerId { get; set; }
        public int? BackupId { get; set; }
        [SqlOneToOne(nameof(OwnerId))] public Person Owner { get; set; }
        [SqlOneToOne(nameof(BackupId))] public Person Backup { get; set; }
        [SqlOneToMany(nameof(Item.RootId))] public List<Item> Items { get; set; }
        [SqlManyToMany("complex_memberships", "RootId", "PersonId")] public List<Person> Members { get; set; }
    }
    [SqlTable("complex_people")]
    internal sealed class Person
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public string Name { get; set; }
        public int? ManagerId { get; set; }
        public int? RegionId { get; set; }
        [SqlOneToOne(nameof(ManagerId))] public Person Manager { get; set; }
        [SqlOneToOne(nameof(RegionId))] public Region Region { get; set; }
        [SqlOneToMany(nameof(ManagerId))] public List<Person> Reports { get; set; }
    }
    [SqlTable("complex_regions")]
    internal sealed class Region { [SqlPrimaryKey] public string Id { get; set; } public string Name { get; set; } }
    [SqlTable("complex_items")]
    internal sealed class Item
    {
        [SqlPrimaryKey] public int Id { get; set; }
        public int RootId { get; set; }
        public int PersonId { get; set; }
        public string Name { get; set; }
        [SqlOneToOne(nameof(PersonId))] public Person Person { get; set; }
    }
    [SqlTable("complex_roots")]
    private sealed class StringRoot { [SqlPrimaryKey] public string Id { get; set; } }
}
