using System.Data;
using System.Text.RegularExpressions;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.Regression;

internal static partial class StoredProcedureRegressionTests
{
    internal static void RunResponses(Action<string, Action> test)
    {
        RunCallableModels(test);
        foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
            foreach (bool asynchronous in new[] { false, true })
            {
                SqlCallableResponse<TOutput> Run<TInput, TOutput>(SqlLinqerCallable<TInput, TOutput> procedure, TInput input, BaseConnector connector,
                    System.Data.Common.DbTransaction transaction = null) where TInput : class
                    => asynchronous ? procedure.ExecuteAsync(input, connector, transaction).GetAwaiter().GetResult()
                        : procedure.Execute(input, connector, transaction);

                test($"procedure ordinary inputs omit null and preserve empty and supplied values, {flavor}, async={asynchronous}", () =>
                {
                    foreach (string name in new[] { null, string.Empty, "  ", "雪" })
                    {
                        var input = new OrdinaryProcedureInput
                        {
                            Name = name,
                            PlainName = name!,
                            Count = name == null ? null : 0,
                            Enabled = name == null ? null : false,
                            Amount = name == null ? null : 0m
                        };
                        var procedure = new InputProcedure();
                        var database = InputDatabase();
                        var response = Run(procedure, input, new FakeConnector(database, flavor));
                        Check(response.State == ResponseState.Valid, response.Error?.ToString());
                        var command = database.Executed.Single();
                        if (name == null)
                            Check(command.Parameters.Count == 2 && !command.CommandText.Contains("@name =")
                                && !command.CommandText.Contains("@PlainName =") && !command.CommandText.Contains("@Count =")
                                && !command.CommandText.Contains("@Enabled =") && !command.CommandText.Contains("@Amount ="),
                                "Null input properties were not omitted");
                        else
                            Check((string)Parameter(command, "name").Value == name && (string)Parameter(command, "PlainName").Value == name
                                && (int)Parameter(command, "Count").Value == 0 && !(bool)Parameter(command, "Enabled").Value
                                && (decimal)Parameter(command, "Amount").Value == 0m, "Supplied input values changed");
                    }
                });
                test($"procedure explicit arguments distinguish omission SQL NULL and values, {flavor}, async={asynchronous}", () =>
                {
                    var inputs = new[]
                    {
                        new ExplicitProcedureInput(),
                        new ExplicitProcedureInput { Name = new SqlArgument<string>(null), Count = new SqlArgument<int?>(null), Enabled = new SqlArgument<bool?>(null) },
                        new ExplicitProcedureInput { Name = string.Empty, Count = 0, Enabled = false },
                        new ExplicitProcedureInput { Name = "雪", Count = 7, Enabled = true }
                    };
                    for (int i = 0; i < inputs.Length; i++)
                    {
                        var procedure = new ExplicitInputProcedure();
                        var database = InputDatabase();
                        var response = Run(procedure, inputs[i], new FakeConnector(database, flavor));
                        Check(response.State == ResponseState.Valid, response.Error?.ToString());
                        var command = database.Executed.Single();
                        if (i == 0) Check(command.Parameters.Count == 2, "Omitted arguments were sent");
                        else if (i == 1)
                            Check(Parameter(command, "name").Value == DBNull.Value && Parameter(command, "Count").Value == DBNull.Value
                                && Parameter(command, "Enabled").Value == DBNull.Value, "Explicit SQL NULL was omitted or replaced");
                        else
                            Check((string)Parameter(command, "name").Value == (i == 2 ? string.Empty : "雪")
                                && (int)Parameter(command, "Count").Value == (i == 2 ? 0 : 7)
                                && (bool)Parameter(command, "Enabled").Value == (i == 3), "Explicit values changed");
                    }
                });
                test($"procedure response exposes standard metadata without result fields, {flavor}, async={asynchronous}", () =>
                {
                    var database = MetadataDatabase(22L, rowsAffected: 3);
                    var response = Run(PagedUsers(), new ExternalArgs(), new FakeConnector(database, flavor));
                    Check(response.State == ResponseState.Valid && response.Result.Single().Name == "雪"
                        && response.TotalResults == 22 && response.ReturnCode == 17 && response.RowsAffected == 3, response.Error?.ToString());
                    Check(Parameter(database.Executed.Single(), "total_count").DbType == DbType.Int64, "Standard count must preserve Int64 totals");
                    Check(database.Connections.Single().Transactions.Single().Commits == 1, "Procedure response lost owned commit");
                    var dictionary = response.ToDictionary();
                    Check((long)dictionary[nameof(response.TotalResults)] == 22 && (int)dictionary[nameof(response.ReturnCode)] == 17
                        && (int)dictionary[nameof(response.RowsAffected)] == 3, "Response dictionary omitted metadata");
                });
                test($"procedure response preserves empty pages, zero and unknown counts, {flavor}, async={asynchronous}", () =>
                {
                    foreach (object total in new object[] { 22L, 0L, DBNull.Value, long.MaxValue })
                    {
                        var response = Run(PagedUsers(), new ExternalArgs(), new FakeConnector(MetadataDatabase(total, empty: true), flavor));
                        Check(response.State == ResponseState.Valid && response.Result.Count == 0 && response.RowsAffected == null
                            && response.TotalResults == (total == DBNull.Value ? null : (long?)total), response.Error?.ToString());
                    }
                });
                test($"procedure query captures inputs and renders metadata, {flavor}, async={asynchronous}", () =>
                {
                    var input = new ExternalArgs { Value = 7 };
                    var procedure = new PagedSchemaProcedure();
                    var database = MetadataDatabase(22L);
                    var connector = new FakeConnector(database, flavor);
                    var rendered = procedure.Render(input, connector);
                    Check(rendered.Text.Contains("[dbo].[PagedUsers]") && rendered.Text.Contains(" OUTPUT"), "Render lost schema or metadata");
                    var response = Run(procedure, input, connector);
                    input.Value = 99;
                    Check(response.State == ResponseState.Valid && (int)Parameter(database.Executed.Single(), "Value").Value == 7,
                        response.Error?.ToString());
                    Check(database.Executed.Single().CommandText == rendered.Text, "Rendered and executed commands differ");
                });
                test($"procedure response clears partial results and metadata on mapping or cleanup failures, {flavor}, async={asynchronous}", () =>
                {
                    foreach (string failure in new[] { "negative", "fractional", "overflow", "invalid", "missing-set", "commit", "dispose-command" })
                    {
                        object total = failure == "negative" ? -1L : failure == "fractional" ? 1.5m
                            : failure == "overflow" ? ulong.MaxValue : failure == "invalid" ? "bad" : 22L;
                        var database = MetadataDatabase(total, missingSet: failure == "missing-set");
                        if (failure is "commit" or "dispose-command") database.FailAt = failure;
                        var response = Run(PagedUsers(), new ExternalArgs(), new FakeConnector(database, flavor));
                        Check(response.State == ResponseState.Error && response.Result == null && response.TotalResults == null
                            && response.ReturnCode == null && response.RowsAffected == null, "Partial metadata escaped after " + failure);
                        Check(database.Connections.Single().IsDisposed, "Failure leaked connection: " + failure);
                        if (failure != "dispose-command")
                            Check(database.Connections.Single().Transactions.Single().Rollbacks == 1, "Failure did not roll back: " + failure);
                    }
                });
                test($"procedure response preserves caller transaction ownership on invalid totals, {flavor}, async={asynchronous}", () =>
                {
                    var database = MetadataDatabase(-1L); var connector = new FakeConnector(database, flavor);
                    using var connection = new FakeConnection(database); connection.Open();
                    using var transaction = (FakeTransaction)connection.BeginTransaction();
                    var response = Run(PagedUsers(), new ExternalArgs(), connector, transaction);
                    Check(response.State == ResponseState.Error && !connection.IsDisposed && !transaction.IsDisposed
                        && transaction.Commits == 0 && transaction.Rollbacks == 0 && connector.Created == 0, "Invalid total changed caller ownership");
                });
                test($"procedure response does not infer totals from rows or status, {flavor}, async={asynchronous}", () =>
                {
                    var database = new FakeDatabase
                    {
                        ReaderFactory = command => new ClosingReader(Users().CreateDataReader(),
                        () => SetReturnCode(command, 9), 0)
                    };
                    var response = Run(PagedUsers(), new ExternalArgs(), new FakeConnector(database, flavor));
                    Check(response.State == ResponseState.Valid && response.TotalResults == null && response.RowsAffected == 0 && response.ReturnCode == 9,
                        response.Error?.ToString());
                });
            }

        test("procedure response resets metadata on errors and result replacement", () =>
        {
            var response = new SqlCallableResponse<string>();
            Check(response.State == ResponseState.Error && response.TotalResults == null && response.ReturnCode == null && response.RowsAffected == null,
                "Unset response exposes metadata");
            response.SetResult(new List<string> { "first" }, 22, 17, 3);
            response.SetError(new Exception("failed"));
            Check(response.Result == null && response.TotalResults == null && response.ReturnCode == null && response.RowsAffected == null,
                "Failure retained prior result metadata");
            response.SetResult(new List<string> { "next" }, 0, 0, 0);
            ((SqlResponse<List<string>>)response).SetResult(new List<string> { "last" });
            Check(response.State == ResponseState.Valid && response.Error == null && response.TotalResults == null
                && response.ReturnCode == null && response.RowsAffected == null, "Replacing a result retained unrelated metadata");
            Throws<ArgumentOutOfRangeException>(() => response.SetResult(new List<string> { "invalid" }, -1, 0, 0));
            Throws<ArgumentOutOfRangeException>(() => response.SetResult(new List<string> { "invalid" }, 0, 0, -1));
            Check(response.Result.Single() == "last", "Invalid count assignment changed the response");
        });
        test("procedure count mappings validate before execution", () =>
        {
            Throws<InvalidOperationException>(() => new InvalidStandardCountProcedure().Render(new StandardCountInput(), DbFlavor.SqlServer));
            Throws<InvalidOperationException>(() => new InvalidAliasCountProcedure().Render(new AliasCountInput(), DbFlavor.SqlServer));
            Throws<InvalidOperationException>(() => new EmptyProcedureMapping());
            Throws<InvalidOperationException>(() => new EmptySchemaMapping());
            Throws<ArgumentNullException>(() => PagedUsers().Render(null, DbFlavor.SqlServer));
            Throws<NotSupportedException>(() => PagedUsers().Render(new ExternalArgs(), DbFlavor.PostgreSql));
        });
        test("procedure metadata async cancellation preserves token and ownership", () =>
        {
            var database = MetadataDatabase(22L);
            using var cancellation = new CancellationTokenSource();
            database.BeforeAsyncRead = _ => { cancellation.Cancel(); return Task.CompletedTask; };
            try
            {
                PagedUsers().ExecuteAsync(new ExternalArgs(), new FakeConnector(database, DbFlavor.SqlServer),
                    cancellationToken: cancellation.Token).GetAwaiter().GetResult();
                throw new Exception("Expected cancellation");
            }
            catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token) { }
            Check(database.Connections.Single().Transactions.Single().Rollbacks == 1 && database.Connections.Single().IsDisposed,
                "Cancellation lost owned rollback or cleanup");
        });
        test("procedure metadata honors the default connector in both execution styles", () =>
        {
            var previous = Default.Connector;
            try
            {
                Default.Connector = new FakeConnector(MetadataDatabase(22L), DbFlavor.SqlServer);
                Check(PagedUsers().Execute(new ExternalArgs()).TotalResults == 22, "Sync default connector differs");
                Check(PagedUsers().ExecuteAsync(new ExternalArgs()).GetAwaiter().GetResult().TotalResults == 22,
                    "Async default connector differs");
            }
            finally { Default.Connector = previous; }
        });
        test("procedure query supports empty input and scalar output models", () =>
        {
            foreach (bool asynchronous in new[] { false, true })
            {
                var database = new FakeDatabase
                {
                    ReaderFactory = command =>
                {
                    var table = new DataTable(); table.Columns.Add("Value", typeof(int)); table.Rows.Add(42);
                    return new ClosingReader(table.CreateDataReader(), () => SetReturnCode(command, 0), -1);
                }
                };
                var connector = new FakeConnector(database, DbFlavor.SqlServer);
                var procedure = new ScalarProcedure();
                var response = asynchronous ? procedure.ExecuteAsync(new EmptyInput(), connector).GetAwaiter().GetResult() : procedure.Execute(new EmptyInput(), connector);
                Check(response.State == ResponseState.Valid && response.Result.Single() == 42 && response.ReturnCode == 0
                    && response.TotalResults == null && response.RowsAffected == null, response.Error?.ToString());
                Check(database.Executed.Single().Parameters.Count == 2, "Empty input generated a parameter");
            }
        });

    }

#nullable enable
    private class OrdinaryProcedureInput
    {
        [SqlParameter("name", Size = 80)] public string? Name { get; set; }
        public string PlainName { get; set; } = null!;
        public int? Count { get; set; }
        public bool? Enabled { get; set; }
        public decimal? Amount { get; set; }
    }
    private class ExplicitProcedureInput
    {
        [SqlParameter("name", Size = 80)] public SqlArgument<string?> Name { get; set; }
        public SqlArgument<int?> Count { get; set; }
        public SqlArgument<bool?> Enabled { get; set; }
    }
#nullable restore
    private static FakeDatabase InputDatabase() => new()
    {
        ReaderFactory = command => new ClosingReader(Users().CreateDataReader(), () => SetReturnCode(command, 0))
    };

    private class StandardCountInput { public long TotalResults { get; set; } }
    private class AliasCountInput { public long total_count { get; set; } }
    private class EmptyInput { }

    private static void SetReturnCode(FakeCommand command, int value)
        => command.Parameters[Regex.Match(command.CommandText, @"EXEC (@P\d+) =").Groups[1].Value].Value = value;

    private static FakeDatabase MetadataDatabase(object total, bool empty = false, int rowsAffected = -1, bool missingSet = false) => new()
    {
        ReaderFactory = command => new ClosingReader((missingSet ? new DataTable() : Users(empty)).CreateDataReader(), () =>
        {
            Parameter(command, "total_count").Value = total;
            SetReturnCode(command, 17);
        }, rowsAffected),
    };

    [SqlProcedure("PagedUsers")]
    private sealed class PagedProcedure : SqlLinqerCallable<ExternalArgs, UserRow> { }
    [SqlProcedure("PagedUsers", Schema = "dbo")]
    private sealed class PagedSchemaProcedure : SqlLinqerCallable<ExternalArgs, UserRow> { }
    [SqlProcedure("Invalid")]
    private sealed class InvalidStandardCountProcedure : SqlLinqerCallable<StandardCountInput, UserRow> { }
    [SqlProcedure("Invalid")]
    private sealed class InvalidAliasCountProcedure : SqlLinqerCallable<AliasCountInput, UserRow> { }
    [SqlProcedure("ScalarOutput")]
    private sealed class ScalarProcedure : SqlLinqerCallable<EmptyInput, int> { }
    private static PagedProcedure PagedUsers() => new();
}
