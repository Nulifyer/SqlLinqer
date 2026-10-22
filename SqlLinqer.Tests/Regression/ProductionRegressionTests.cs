using System.Globalization;
using SqlLinqer;
using SqlLinqer.Components.Render;
using SqlLinqer.Components.Select;
using SqlLinqer.Extensions.StringExtensions;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static class ProductionRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (DbFlavor flavor in Enum.GetValues<DbFlavor>())
        {
            test($"embedded identifier delimiters are escaped for {flavor}", () =>
            {
                string input = flavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? "odd]name"
                    : flavor == DbFlavor.MySql ? "odd`name" : "odd\"name";
                string expected = flavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? "[odd]]name]"
                    : flavor == DbFlavor.MySql ? "`odd``name`" : "\"odd\"\"name\"";
                Check(input.DbWrap(flavor) == expected, "Identifier delimiter was not escaped");
            });
            test($"apostrophes in JSON projection names are escaped for {flavor}", () =>
            {
                Check("O'Reilly".DbWrapString(flavor) == "'O''Reilly'", "Literal apostrophe was not escaped");
                var query = new SelectQuery<CompilationRegressionTests.Row>();
                string rendered = new SelectFuncStatement(SqlFunc.CONCAT, "O'Reilly", "value")
                    .Render(query, new ParameterCollection(), flavor, true, true);
                Check(rendered.Contains(flavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? "[O'Reilly]" : "'O''Reilly'"),
                    "Projection emitted an invalid output name");
            });
        }
        foreach (DbFlavor flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql, DbFlavor.MySql, DbFlavor.PostgreSql })
        {
            test($"legacy prequoted names remain compatible for {flavor}", () =>
            {
                string quoted = flavor is DbFlavor.SqlServer or DbFlavor.AzureSql ? "[name]"
                    : flavor == DbFlavor.MySql ? "`name`" : "\"name\"";
                Check(quoted.DbWrap(flavor) == quoted, "Legacy identifier was quoted twice");
                Check("'value'".DbWrapString(flavor) == "'value'", "Legacy literal was quoted twice");
            });
        }
        foreach (DbFlavor flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql, DbFlavor.MySql })
        {
            test($"legacy SOUNDDEX marker renders SOUNDEX for {flavor}", () =>
            {
                var query = new SelectQuery<CompilationRegressionTests.Row>();
                string sql = new SelectFuncStatement(SqlFunc.SOUNDDEX, "phonetic", "Smith")
                    .Render(query, new ParameterCollection(), flavor, false, true);
                Check(sql.StartsWith("SOUNDEX("), "Phonetic function name is misspelled");
            });
        }
        test("missing execution connectors identify the connector parameter", () =>
        {
            var query = new SelectQuery<CompilationRegressionTests.Row>();
            CheckConnectorArgument(() => query.Execute((SqlLinqer.Connections.BaseConnector)null));
            CheckConnectorArgument(() => query.ExecuteAsync((SqlLinqer.Connections.BaseConnector)null).GetAwaiter().GetResult());
            var procedure = new ProcedureProbe();
            CheckConnectorArgument(() => procedure.Execute(new ProcedureProbe.Input(), (SqlLinqer.Connections.BaseConnector)null));
            CheckConnectorArgument(() => procedure.ExecuteAsync(new ProcedureProbe.Input(), (SqlLinqer.Connections.BaseConnector)null).GetAwaiter().GetResult());
        });
    }

    private static void CheckConnectorArgument(Action action)
    {
        try { action(); }
        catch (ArgumentNullException error)
        {
            Check(error.ParamName == "connector", "Argument name and message were reversed");
            return;
        }
        throw new Exception("Expected ArgumentNullException");
    }

    internal static async Task CheckScalars(SqlLinqer.Connections.BaseConnector connector)
    {
        await Scalar<int?>("SELECT 7", 7);
        await Scalar<int?>("SELECT NULL", null);
        await Scalar<string>("SELECT NULL", null);
        await Scalar<string>("SELECT 'unused' WHERE 1 = 0", null);
        await Scalar<Guid>("SELECT 'ad04bbca-76b2-4cf4-9572-29499fa89e7d'", new Guid("ad04bbca-76b2-4cf4-9572-29499fa89e7d"));
        await Scalar<Guid?>("SELECT NULL", null);
        await Scalar<ResponseState>("SELECT 1", ResponseState.Valid);
        await Scalar<ResponseState?>("SELECT 1", ResponseState.Valid);
        await Scalar<ResponseState>("SELECT 'Valid'", ResponseState.Valid);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            await Scalar<decimal>("SELECT '1.25'", 1.25m);
        }
        finally { CultureInfo.CurrentCulture = previous; }
        Check(connector.ExecuteScalar<int>(new RenderedQuery("SELECT NULL")).State == ResponseState.Error,
            "SQL NULL silently became a non-nullable integer");
        Check((await connector.ExecuteScalarAsync<int>(new RenderedQuery("SELECT NULL"))).State == ResponseState.Error,
            "Async SQL NULL silently became a non-nullable integer");

        async Task Scalar<T>(string sql, T expected)
        {
            var synchronous = connector.ExecuteScalar<T>(new RenderedQuery(sql));
            var asynchronous = await connector.ExecuteScalarAsync<T>(new RenderedQuery(sql));
            Check(synchronous.State == ResponseState.Valid, synchronous.Error?.ToString());
            Check(asynchronous.State == ResponseState.Valid, asynchronous.Error?.ToString());
            Check(EqualityComparer<T>.Default.Equals(synchronous.Result, expected), $"Unexpected synchronous scalar for {sql}");
            Check(EqualityComparer<T>.Default.Equals(asynchronous.Result, expected), $"Unexpected asynchronous scalar for {sql}");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
