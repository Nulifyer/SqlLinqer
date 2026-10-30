using System.Collections.Concurrent;
using SqlLinqer.Compilation;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class ModelIdentityRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("model identities stay compact and unique across concurrent roots and columns", () =>
        {
            var ids = new ConcurrentBag<string>();
            Parallel.For(0, 4000, i =>
            {
                var table = new Table("schema", "same_name");
                ids.Add(table.UUID); ids.Add(new Column(table, "same_column").UUID);
            });
            Check(ids.Count == ids.Distinct(StringComparer.Ordinal).Count(), "Independent identities collided");
            Check(ids.All(s => s.Length <= 18 && s[0] == '$'), "Generated identities grew beyond their compact namespace");
        });
        test("column identity follows table and name changes and returns to its canonical identity", () =>
        {
            var first = new Table("schema", "rows"); var second = new Table("schema", "rows");
            var column = new Column(first, "Id"); string original = column.UUID;
            Check(new Column(first, "Id").UUID == original && column.WithAliasAsUUID().UUID == original, "Equivalent columns differ");
            column.ColumnName = "id"; Check(column.UUID != original, "Case-sensitive column identities collapsed");
            column.ColumnName = "Id"; Check(column.UUID == original, "Restoring a name changed its canonical identity");
            column.Table = second; Check(column.UUID != original, "Independent tables shared a column identity");
            column.Table = first; Check(column.UUID == original, "Restoring a table changed its identity");
        });
        test("snapshots preserve identities first discovered after capture", () =>
        {
            var root = new Table(typeof(StressRoot), "schema");
            var copy = SnapshotGraph.Capture(root);
            var copyColumn = new Column(copy, "late_column");
            Check(copyColumn.UUID == new Column(root, "late_column").UUID, "Late column discovery diverged across a snapshot");
            var originalOwner = root.Relationships.TryGet(nameof(StressRoot.Owner)).ChildTable;
            var copiedOwner = copy.Relationships.TryGet(nameof(StressRoot.Owner)).ChildTable;
            Check(originalOwner.UUID == copiedOwner.UUID, "Late relationship discovery diverged across a snapshot");
            Check(originalOwner.Columns.GetAll().Select(c => c.UUID).SequenceEqual(copiedOwner.Columns.GetAll().Select(c => c.UUID)), "Relationship column identities diverged");
            originalOwner.Alias = "changed_alias";
            Check(copiedOwner.Alias != "changed_alias", "Identity cache sharing exposed mutable table aliases");
        });
        test("concurrent detached discoveries preserve one logical path and separate sibling paths", () =>
        {
            var root = new Table(typeof(StressRoot), "schema"); var ids = new string[128];
            Parallel.For(0, ids.Length, i =>
            {
                var copy = SnapshotGraph.Capture(root);
                var owner = copy.Relationships.TryGet(nameof(StressRoot.Owner)).ChildTable;
                ids[i] = owner.UUID + ":" + new Column(owner, "late").UUID;
                Check(owner.UUID != copy.Relationships.TryGet(nameof(StressRoot.Backup)).ChildTable.UUID, "Sibling paths collapsed");
            });
            Check(ids.Distinct().Count() == 1, "Concurrent snapshots assigned different logical identities");
        });
        test("model identity lookup scopes expire with their graphs", () =>
        {
            var weak = Ephemeral();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(!weak.IsAlive, "An identity lookup retained an otherwise unused model graph");
        });
        foreach (var flavor in Enum.GetValues<DbFlavor>())
            test("compact identities preserve repeated captured SQL and explicit aliases " + flavor, () =>
            {
                var root = new Table(typeof(StressRoot), "schema"); root.Alias = "explicit table alias";
                var query = new SelectQuery<StressRoot>(root).SelectRootColumns()
                    .Select(x => x.Owner).Select(x => x.Backup).Select(x => x.Projects)
                    .Where(x => x.Projects.First().Tickets.First().Category.Id, 1, SqlOp.EQ).OrderByAuto();
                var snapshot = query.Capture(); string sql = snapshot.Render(flavor).Text;
                Check(sql == snapshot.Render(flavor).Text, "Repeated rendering assigned different aliases");
                Check(sql.Contains("explicit table alias"), "Explicit table alias was replaced");
                Check(root.Alias == "explicit table alias", "Rendering mutated a model alias");
                var other = new Table(typeof(StressRoot), "other_schema");
                Check(other.UUID != root.UUID, "Schemas shared a root identity");
            });
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference Ephemeral()
    {
        var table = new Table(typeof(StressRoot), "ephemeral");
        _ = table.Relationships.TryGet(nameof(StressRoot.Owner)).ChildTable.UUID;
        _ = new Column(table, "late").UUID;
        return new WeakReference(table.Identities);
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
