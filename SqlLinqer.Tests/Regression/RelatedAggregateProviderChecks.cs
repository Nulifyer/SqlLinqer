using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.Regression;

internal static class RelatedAggregateProviderChecks
{
    internal static void Run(BaseConnector db, Action<string, Action> test)
    {
        foreach (string mode in new[] { "sync", "async", "stream" })
            test("related aggregates: independent counts/sum, constraints, zero and page totals, " + mode, () => CheckProvider(db, mode).GetAwaiter().GetResult());
    }
    private static async Task CheckProvider(BaseConnector db, string mode)
    {
        string Q(string value) => value.DbWrap(db.DbFlavor);
        string T(string value) => string.IsNullOrEmpty(db.Options.DefaultSchema) ? Q(value) : Q(db.Options.DefaultSchema) + "." + Q(value);
        string root = T("review_related_roots"), child = T("review_related_children"), note = T("review_related_notes");
        void Sql(string text) => db.ExecuteNonQuery(new(text)).EnsureSuccess();
        try
        {
            Sql($"CREATE TABLE {root} ({Q("Id")} INTEGER PRIMARY KEY); CREATE TABLE {child} ({Q("Id")} INTEGER PRIMARY KEY,{Q("ParentId")} INTEGER,{Q("Value")} INTEGER,{Q("Active")} INTEGER); CREATE TABLE {note} ({Q("Id")} INTEGER PRIMARY KEY,{Q("ParentId")} INTEGER); INSERT INTO {root} VALUES(1),(2); INSERT INTO {child} VALUES(1,1,10,1),(2,1,20,1),(3,1,999,0); INSERT INTO {note} VALUES(1,1),(2,1),(3,1);");
            var query = new SelectQueryInitializer<Root>(db.Options.DefaultSchema).BuildQuery(q => new
            {
                Id = q.Select(x => x.Id),
                Children = q.CountRelated(x => x.Children),
                Notes = q.CountRelated(x => x.Notes),
                Amount = q.SumRelated(x => x.Children, x => x.Value)
            }).OrderBy(x => x.Id, SqlDir.ASC);
            var rows = mode == "async" ? (await query.ExecuteAsync(db)).GetResultOrThrow() : query.Execute(db).GetResultOrThrow();
            if (mode == "stream")
            {
                rows.Clear();
                await foreach (var row in query.ExecuteAsyncStream(db)) rows.Add(row);
            }
            Check(rows.Count == 2 && rows[0].Children == 2 && rows[0].Notes == 3 && rows[0].Amount == 30, "Joined fan-out or mapped constraint changed aggregate values");
            Check(rows[1].Children == 0 && rows[1].Notes == 0 && rows[1].Amount == 0, "Empty collection did not produce zero");
            var page = await query.Page(2, 1).ExecuteWithTotalCountAsync(db);
            Check(page.TotalResults == 2 && page.GetResultOrThrow().Single().Id == 2, "Page/count changed parent row meaning");
        }
        finally { Sql($"DROP TABLE IF EXISTS {note}; DROP TABLE IF EXISTS {child}; DROP TABLE IF EXISTS {root};"); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    [SqlTable("review_related_roots")]
    public sealed class Root
    {
        [SqlPrimaryKey] public int Id { get; set; }
        [SqlOneToMany(nameof(Child.ParentId)), SqlConditionChildColumnConstant(nameof(Child.Active), 1, SqlOp.EQ)] public List<Child> Children { get; set; } = [];
        [SqlOneToMany(nameof(Note.ParentId))] public List<Note> Notes { get; set; } = [];
    }
    [SqlTable("review_related_children")]
    public sealed class Child { [SqlPrimaryKey] public int Id { get; set; } public int ParentId { get; set; } public int Value { get; set; } public int Active { get; set; } }
    [SqlTable("review_related_notes")]
    public sealed class Note { [SqlPrimaryKey] public int Id { get; set; } public int ParentId { get; set; } }
}
