using System.Data;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.Regression;

internal static partial class StoredProcedureRegressionTests
{
    private static void RunCallableModels(Action<string, Action> test)
    {
        foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
            foreach (bool asynchronous in new[] { false, true })
            {
                SqlCallableResponse<UserRow> Run<TInput>(SqlLinqerCallable<TInput, UserRow> procedure, TInput input,
                    BaseConnector connector, System.Data.Common.DbTransaction transaction = null) where TInput : class
                    => asynchronous ? procedure.ExecuteAsync(input, connector, transaction).GetAwaiter().GetResult()
                        : procedure.Execute(input, connector, transaction);

                test($"procedure classes accept ordinary inputs on every call, {flavor}, async={asynchronous}", () =>
                {
                    var procedure = new InputProcedure();
                    var database = InputDatabase();
                    var connector = new FakeConnector(database, flavor);
                    var input = new OrdinaryProcedureInput();
                    foreach (string name in new[] { null, string.Empty, "雪" })
                    {
                        input.Name = name;
                        input.PlainName = name!;
                        input.Count = name == null ? null : 7;
                        var response = Run(procedure, input, connector);
                        Check(response.State == ResponseState.Valid && response.Result.Single().Id == 7, response.Error?.ToString());
                        var command = database.Executed.Last();
                        Check(command.CommandText.Contains("[dbo].[Inputs]"), "Procedure class lost its database name or schema");
                        if (name == null) Check(command.Parameters.Count == 2, "Default inputs were not omitted");
                        else Check((string)Parameter(command, "name").Value == name && (int)Parameter(command, "Count").Value == 7,
                            "Procedure retained values from an earlier call");
                    }
                    Check(database.Executed.Count == 3, "Procedure calls were not independent");
                });
                test($"procedure classes accept optional explicit argument controls, {flavor}, async={asynchronous}", () =>
                {
                    var procedure = new ExplicitInputProcedure();
                    var database = InputDatabase();
                    var connector = new FakeConnector(database, flavor);
                    Check(Run(procedure, new ExplicitProcedureInput(), connector).State == ResponseState.Valid, "Omitted explicit input failed");
                    Check(database.Executed.Last().Parameters.Count == 2, "Explicit omission was sent");
                    var response = Run(procedure, new ExplicitProcedureInput { Name = new SqlArgument<string>(null) }, connector);
                    Check(response.State == ResponseState.Valid && Parameter(database.Executed.Last(), "name").Value == DBNull.Value,
                        "Explicit SQL NULL was omitted");
                });
                test($"procedure classes return metadata and retain caller transaction ownership, {flavor}, async={asynchronous}", () =>
                {
                    var database = MetadataDatabase(22L);
                    var connector = new FakeConnector(database, flavor);
                    var procedure = new MetadataProcedure(connector);
                    using var connection = new FakeConnection(database); connection.Open();
                    using var transaction = (FakeTransaction)connection.BeginTransaction();
                    var response = asynchronous
                        ? procedure.ExecuteAsync(new ExternalArgs(), transaction).GetAwaiter().GetResult()
                        : procedure.Execute(new ExternalArgs(), transaction);
                    Check(response.State == ResponseState.Valid && response.TotalResults == 22 && response.ReturnCode == 17
                        && !transaction.IsDisposed && transaction.Commits == 0 && transaction.Rollbacks == 0
                        && !connection.IsDisposed && connector.Created == 0, "Procedure class changed caller transaction ownership");
                    var otherDatabase = MetadataDatabase(33L);
                    var otherConnector = new FakeConnector(otherDatabase, flavor);
                    Check(Run(procedure, new ExternalArgs(), otherConnector).TotalResults == 33, "Explicit connector override was ignored");
                    Check((asynchronous ? procedure.ExecuteAsync(new ExternalArgs()).GetAwaiter().GetResult()
                        : procedure.Execute(new ExternalArgs())).TotalResults == 22, "Override replaced the retained connector");
                });
            }

        test("procedure classes use default connectors and render without executing", () =>
        {
            var previous = Default.Connector;
            try
            {
                var database = InputDatabase();
                var connector = new FakeConnector(database, DbFlavor.SqlServer);
                Default.Connector = connector;
                var procedure = new InputProcedure();
                Check(procedure.Render(new OrdinaryProcedureInput()).Text.Contains("[dbo].[Inputs]"), "Default render lost name");
                Check(procedure.Render(new OrdinaryProcedureInput(), DbFlavor.AzureSql).Text.Contains("[dbo].[Inputs]"), "Flavor render differs");
                Check(connector.Created == 0, "Rendering opened a connection");
                Check(procedure.Execute(new OrdinaryProcedureInput()).State == ResponseState.Valid
                    && procedure.ExecuteAsync(new OrdinaryProcedureInput()).GetAwaiter().GetResult().State == ResponseState.Valid,
                    "Procedure class did not use the default connector");
                Throws<ArgumentNullException>(() => procedure.Execute(null));
                Throws<NotSupportedException>(() => procedure.Render(new OrdinaryProcedureInput(), DbFlavor.Sqlite));
            }
            finally { Default.Connector = previous; }
        });
        test("procedure attributes validate mappings before opening connections", () =>
        {
            Throws<InvalidOperationException>(() => new MissingProcedureMapping());
            Throws<InvalidOperationException>(() => new EmptyProcedureMapping());
            Throws<InvalidOperationException>(() => new EmptySchemaMapping());
            var first = new FirstMappedProcedure().Render(new ExternalArgs(), DbFlavor.SqlServer);
            var second = new SecondMappedProcedure().Render(new ExternalArgs(), DbFlavor.SqlServer);
            Check(first.Text.Contains("[one].[First]") && second.Text.Contains("[two].[Second]")
                && !first.Text.Contains("[two].[Second]"), "Shared input and output types lost their procedure attributes");
            var quoted = new QuotedMappedProcedure().Render(new ExternalArgs(), DbFlavor.SqlServer);
            Check(quoted.Text.Contains("[o'ne].[O'Brien]]Rows]") && quoted.Text.Contains("N'[o''ne].[O''Brien]]Rows]'"),
                "Automatic metadata lookup did not escape the procedure identifier as a string literal");
        });
        test("procedure classes propagate cancellation tokens before opening connections", () =>
        {
            var connector = new FakeConnector(InputDatabase(), DbFlavor.SqlServer);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try
            {
                new InputProcedure().ExecuteAsync(new OrdinaryProcedureInput(), connector, cancellationToken: cancellation.Token)
                    .GetAwaiter().GetResult();
                throw new Exception("Expected cancellation");
            }
            catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token) { }
            Check(connector.Created == 0, "Cancelled procedure opened a connection");
        });
    }

    private sealed class MissingProcedureMapping : SqlLinqerCallable<ExternalArgs, UserRow> { }
    [SqlProcedure(" ")]
    private sealed class EmptyProcedureMapping : SqlLinqerCallable<ExternalArgs, UserRow> { }
    [SqlProcedure("EmptySchema", Schema = " ")]
    private sealed class EmptySchemaMapping : SqlLinqerCallable<ExternalArgs, UserRow> { }
    [SqlProcedure("First", Schema = "one")]
    private sealed class FirstMappedProcedure : SqlLinqerCallable<ExternalArgs, UserRow> { }
    [SqlProcedure("Second", Schema = "two")]
    private sealed class SecondMappedProcedure : SqlLinqerCallable<ExternalArgs, UserRow> { }
    [SqlProcedure("O'Brien]Rows", Schema = "o'ne")]
    private sealed class QuotedMappedProcedure : SqlLinqerCallable<ExternalArgs, UserRow> { }
    [SqlProcedure("Inputs", Schema = "dbo")]
    private sealed class InputProcedure : SqlLinqerCallable<OrdinaryProcedureInput, UserRow> { }
    [SqlProcedure("Inputs")]
    private sealed class ExplicitInputProcedure : SqlLinqerCallable<ExplicitProcedureInput, UserRow> { }
    [SqlProcedure("PagedUsers")]
    private sealed class MetadataProcedure : SqlLinqerCallable<ExternalArgs, UserRow>
    {
        public MetadataProcedure(BaseConnector connector) : base(connector) { }
    }
}
