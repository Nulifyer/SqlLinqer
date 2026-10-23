using Microsoft.Data.Sqlite;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer.Tests.Regression;

internal static class RelatedAggregateRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("related aggregates: independent counts, sums, mapping conditions, snapshots and paging", Aggregates);
    }

    private static void Aggregates()
    {
        string file = Path.Combine(Path.GetTempPath(), "sqllinqer-related-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var db = new ConnectionFactoryConnector(() => new SqliteConnection($"Data Source={file};Pooling=False"), DbFlavor.Sqlite);
            db.ExecuteNonQuery(new("CREATE TABLE parents(Id INTEGER PRIMARY KEY); CREATE TABLE children(Id INTEGER PRIMARY KEY,ParentId INTEGER,Value INTEGER,Active INTEGER); CREATE TABLE notes(Id INTEGER PRIMARY KEY,ParentId INTEGER); INSERT INTO parents VALUES(1),(2); INSERT INTO children VALUES(1,1,10,1),(2,1,20,1),(3,1,999,0); INSERT INTO notes VALUES(1,1),(2,1),(3,1);")).EnsureSuccess();
            var query = new SelectQueryInitializer<Parent>().BuildQuery(q => new
            {
                Id = q.Select(x => x.Id),
                Tasks = q.CountRelated(x => x.Children),
                Notes = q.CountRelated(x => x.Notes),
                Amount = q.SumRelated(x => x.Children, x => x.Value)
            }).OrderBy(x => x.Id, SqlDir.ASC);
            var before = query.Capture();
            var rows = query.Execute(db).GetResultOrThrow();
            Check(rows.Count == 2 && rows[0].Tasks == 2 && rows[0].Notes == 3 && rows[0].Amount == 30, "Independent related aggregates multiplied or lost mapping conditions");
            Check(rows[1].Tasks == 0 && rows[1].Notes == 0 && rows[1].Amount == 0, "Empty related aggregates did not return zero");
            foreach (DbFlavor flavor in Enum.GetValues<DbFlavor>())
            {
                var first = before.Render(flavor);
                Check(first.Text.Contains("COALESCE((") && !first.Text.Contains("LEFT JOIN"), "Related aggregates joined collections in " + flavor);
                Check(first.Text == before.Render(flavor).Text, "Rendering mutated the snapshot for " + flavor);
                Check(first.Parameters.GetAll().Any(p => Equals(p.Value, 1)), "Mapped constraint binding lost for " + flavor);
            }
            query.Page(2, 1);
            var page = query.ExecuteWithTotalCount(db);
            Check(page.TotalResults == 2 && page.GetResultOrThrow().Single().Id == 2, "Related aggregate page/count changed row meaning");
        }
        finally { File.Delete(file); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    [SqlTable("parents")]
    public sealed class Parent
    {
        [SqlPrimaryKey] public int Id { get; set; }
        [SqlOneToMany(nameof(Child.ParentId)), SqlConditionChildColumnConstant(nameof(Child.Active), 1, SqlOp.EQ)] public List<Child> Children { get; set; } = [];
        [SqlOneToMany(nameof(Note.ParentId))] public List<Note> Notes { get; set; } = [];
    }
    [SqlTable("children")]
    public sealed class Child { [SqlPrimaryKey] public int Id { get; set; } public int ParentId { get; set; } public int Value { get; set; } public int Active { get; set; } }
    [SqlTable("notes")]
    public sealed class Note { [SqlPrimaryKey] public int Id { get; set; } public int ParentId { get; set; } }
}
