using System.Text.RegularExpressions;
using SqlLinqer;
using SqlLinqer.Components.Update;
using SqlLinqer.Queries.Typed;
using Row = SqlLinqer.Tests.Regression.UpdateOperationProviderChecks.Row;

namespace SqlLinqer.Tests.Regression;

internal static class UpdateOperationRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        string Clean(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();
        foreach (var flavor in Enum.GetValues<DbFlavor>())
            test($"set-if-null and custom operations render a valid assignment target for {flavor}", () =>
            {
                bool bare = flavor is DbFlavor.PostgreSql or DbFlavor.Sqlite or DbFlavor.DuckDb;
                string quote = flavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? @"\[label\]" : flavor == DbFlavor.MySql ? "`label`" : "\"label\"";
                string target = bare ? " SET " + quote : @" SET \S+\." + quote;
                string setIfNull = Clean(new UpdateQuery<Row>().Update(x => x.Label, new SetIfNullOperation("a")).Where(x => x.Id, 1, SqlOp.EQ).Render(flavor).Text);
                Check(Regex.IsMatch(setIfNull, target + @" = COALESCE\(\S+\." + quote + @", \S+\)"), setIfNull);
                string custom = Clean(new UpdateQuery<Row>().Update(x => x.Label, new UpdateOperationProviderChecks.UpperCase()).Where(x => x.Id, 1, SqlOp.EQ).Render(flavor).Text);
                Check(Regex.IsMatch(custom, target + @" = UPPER\(\S+\." + quote + @"\)"), custom);
            });
        test("the assignment target renderer is public for application-defined operations", () =>
        {
            var method = typeof(UpdateStatement).GetMethod(nameof(UpdateStatement.RenderTarget), new[] { typeof(DbFlavor) });
            Check(method != null && method.IsPublic, "RenderTarget is not callable from application code");
        });
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
