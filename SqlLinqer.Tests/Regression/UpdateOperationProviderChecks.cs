using SqlLinqer.Components.Render;
using SqlLinqer.Components.Update;
using SqlLinqer.Connections;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Tests.Regression;

// Runs the set-if-null operation and an application-defined operation on every live provider.
internal static class UpdateOperationProviderChecks
{
    internal static void Run(BaseConnector db, Action<string, Action> test)
    {
        foreach (bool asynchronous in new[] { false, true })
            test($"update operations: set-if-null and a custom operation in updates and conflict updates, async={asynchronous}",
                () => CheckProvider(db, asynchronous).GetAwaiter().GetResult());
    }

    private static async Task CheckProvider(BaseConnector db, bool asynchronous)
    {
        string Q(string value) => value.DbWrap(db.DbFlavor);
        string table = (string.IsNullOrEmpty(db.Options.DefaultSchema) ? "" : Q(db.Options.DefaultSchema) + ".") + Q("operation_rows");
        try
        {
            db.BeginSql($"CREATE TABLE {table} ({Q("id")} INTEGER PRIMARY KEY, {Q("label")} VARCHAR(20) NULL)").Execute().EnsureSuccess();
            db.BeginInsert<Row>().InsertAuto(new Row { Id = 1 }).Execute().EnsureSuccess();
            async Task<string> Label() => (asynchronous
                ? await db.BeginSelect<Row>().SelectRootColumns().Where(x => x.Id, 1, SqlOp.EQ).ExecuteAsync().GetResultOrThrowAsync()
                : db.BeginSelect<Row>().SelectRootColumns().Where(x => x.Id, 1, SqlOp.EQ).Execute().GetResultOrThrow()).Single().Label;
            async Task Update(UpdateOperation operation)
            {
                var update = db.BeginUpdate<Row>().Update(x => x.Label, operation).Where(x => x.Id, 1, SqlOp.EQ);
                int affected = asynchronous ? await update.ExecuteAsync().GetResultOrThrowAsync() : update.Execute().GetResultOrThrow();
                Check(affected == 1, "The update did not affect the row");
            }

            await Update(new SetIfNullOperation("first"));
            Check(await Label() == "first", "A NULL column did not receive the value");
            await Update(new SetIfNullOperation("second"));
            Check(await Label() == "first", "An existing value was replaced");

            // An operation written outside the library assigns through the public target renderer.
            await Update(new UpperCase());
            Check(await Label() == "FIRST", "A custom operation did not assign through RenderTarget");

            var conflict = db.BeginInsert<Row>().InsertAuto(new Row { Id = 1, Label = "ignored" }).OnConflict(x => x.Id)
                .OnConflictUpdate(x => x.Label, new SetIfNullOperation("third"));
            (asynchronous ? await conflict.ExecuteAsync() : conflict.Execute()).EnsureSuccess();
            Check(await Label() == "FIRST", "A conflict update replaced an existing value");
            db.BeginUpdate<Row>().Update(x => x.Label, (string)null).Where(x => x.Id, 1, SqlOp.EQ).Execute().EnsureSuccess();
            (asynchronous ? await conflict.ExecuteAsync() : conflict.Execute()).EnsureSuccess();
            Check(await Label() == "third", "A conflict update did not fill a NULL column");
        }
        finally { db.BeginSql($"DROP TABLE IF EXISTS {table}").Execute(); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    internal sealed class UpperCase : UpdateOperation
    {
        public override string Render(UpdateQuery query, UpdateStatement statement, ParameterCollection parameters, DbFlavor flavor)
            => $"{statement.RenderTarget(flavor)} = UPPER({statement.UpdateColumn.Render(flavor)})";
    }

    [SqlTable("operation_rows")]
    public sealed class Row
    {
        [SqlPrimaryKey(false), SqlColumn("id")] public int Id { get; set; }
        [SqlColumn("label")] public string Label { get; set; }
    }
}
