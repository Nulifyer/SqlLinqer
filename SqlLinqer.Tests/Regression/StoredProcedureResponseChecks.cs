using System.Data;
using System.Text.RegularExpressions;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.Interfaces;

namespace SqlLinqer.Tests.Regression;

internal static partial class CallableRegressionTests
{
    internal static void RunResponses(Action<string, Action> test)
    {
        foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
            foreach (bool asynchronous in new[] { false, true })
            {
                SqlStoredProcedureResponse<UserRow> Run(StoredProcedureQuery<ExternalArgs, UserRow> query, BaseConnector connector,
                    System.Data.Common.DbTransaction transaction = null)
                    => asynchronous ? query.ExecuteAsync(connector, transaction).GetAwaiter().GetResult()
                        : query.Execute(connector, transaction);

                test($"procedure response exposes standard metadata without result fields, {flavor}, async={asynchronous}", () =>
                {
                    var database = MetadataDatabase(22L, rowsAffected: 3);
                    var response = Run(PagedUsers(), new FakeConnector(database, flavor));
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
                        var response = Run(PagedUsers(), new FakeConnector(MetadataDatabase(total, empty: true), flavor));
                        Check(response.State == ResponseState.Valid && response.Result.Count == 0 && response.RowsAffected == null
                            && response.TotalResults == (total == DBNull.Value ? null : (long?)total), response.Error?.ToString());
                    }
                });
                test($"procedure query captures inputs and renders metadata, {flavor}, async={asynchronous}", () =>
                {
                    var input = new ExternalArgs { Value = 7 };
                    var query = new StoredProcedureQuery<ExternalArgs, UserRow>("PagedUsers", input, "dbo", "total_count");
                    input.Value = 99;
                    var database = MetadataDatabase(22L);
                    var connector = new FakeConnector(database, flavor);
                    var rendered = query.Render(connector);
                    Check(rendered.Text.Contains("[dbo].[PagedUsers]") && rendered.Text.Contains(" OUTPUT"), "Render lost schema or metadata");
                    var response = Run(query, connector);
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
                        var response = Run(PagedUsers(), new FakeConnector(database, flavor));
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
                    var response = Run(PagedUsers(), connector, transaction);
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
                    var response = Run(new StoredProcedureQuery<ExternalArgs, UserRow>("PagedUsers", new ExternalArgs()), new FakeConnector(database, flavor));
                    Check(response.State == ResponseState.Valid && response.TotalResults == null && response.RowsAffected == 0 && response.ReturnCode == 9,
                        response.Error?.ToString());
                });
            }

        test("procedure response resets metadata on errors and result replacement", () =>
        {
            var response = new SqlStoredProcedureResponse<string>();
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
            Throws<InvalidOperationException>(() => new StoredProcedureQuery<ExternalArgs, UserRow>("Invalid", new ExternalArgs(), totalResultsParameter: "invalid;name"));
            Throws<InvalidOperationException>(() => new StoredProcedureQuery<ExternalArgs, UserRow>("Invalid", new ExternalArgs(), totalResultsParameter: "Value"));
            Throws<InvalidOperationException>(() => new StoredProcedureQuery<ExternalArgs, UserRow>(" ", new ExternalArgs()));
            Throws<InvalidOperationException>(() => new StoredProcedureQuery<ExternalArgs, UserRow>("Valid", new ExternalArgs(), schema: " "));
            Throws<ArgumentNullException>(() => new StoredProcedureQuery<ExternalArgs, UserRow>("Valid", null));
            Throws<NotSupportedException>(() => PagedUsers().Render(DbFlavor.PostgreSql));
        });
        test("procedure metadata async cancellation preserves token and ownership", () =>
        {
            var database = MetadataDatabase(22L);
            using var cancellation = new CancellationTokenSource();
            database.BeforeAsyncRead = _ => { cancellation.Cancel(); return Task.CompletedTask; };
            try
            {
                PagedUsers().ExecuteAsync(new FakeConnector(database, DbFlavor.SqlServer),
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
                Check(PagedUsers().Execute().TotalResults == 22, "Sync default connector differs");
                Check(PagedUsers().ExecuteAsync().GetAwaiter().GetResult().TotalResults == 22,
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
                var query = new StoredProcedureQuery<EmptyInput, int>("ScalarOutput", new EmptyInput());
                var response = asynchronous ? query.ExecuteAsync(connector).GetAwaiter().GetResult() : query.Execute(connector);
                Check(response.State == ResponseState.Valid && response.Result.Single() == 42 && response.ReturnCode == 0
                    && response.TotalResults == null && response.RowsAffected == null, response.Error?.ToString());
                Check(database.Executed.Single().Parameters.Count == 1, "Empty input generated a parameter");
            }
        });
        test("procedure query supports select execution interfaces and retained connectors", () =>
        {
            var database = MetadataDatabase(22L);
            var connector = new FakeConnector(database, DbFlavor.SqlServer);
            var query = FactoryRow.BeginStoredProcedure("PagedUsers", new ExternalArgs(), connector, totalResultsParameter: "total_count");
            var first = ((IQueryExecutable<List<FactoryRow>>)query).Execute();
            var second = ((IAsyncQueryExecutable<List<FactoryRow>>)query).ExecuteAsync().GetAwaiter().GetResult();
            var third = ((IQueryExecutable<List<FactoryRow>>)query).Execute(connector);
            var fourth = ((IAsyncQueryExecutable<List<FactoryRow>>)query).ExecuteAsync(connector).GetAwaiter().GetResult();
            Check(new[] { first, second, third, fourth }.All(r => r.State == ResponseState.Valid && r.Result.Single().Id == 7
                && r is SqlStoredProcedureResponse<FactoryRow> metadata && metadata.TotalResults == 22), "Execution interface lost row data or metadata");
            Check(database.Executed.Count == 4, "Retained connector was not used");
            StoredProcedureQuery<FactoryRow> legacy = FactoryRow.BeginStoredProcedure("Legacy", connector);
            Check(legacy != null && FactoryRow.BeginStoredProcedure("Legacy", "dbo") != null, "Legacy overload resolution changed");
        });
    }

    private class EmptyInput { }
    private class FactoryRow : SqlLinqerObject<FactoryRow> { public int Id { get; set; } }

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

    private static StoredProcedureQuery<ExternalArgs, UserRow> PagedUsers()
        => new("PagedUsers", new ExternalArgs(), totalResultsParameter: "@total_count");
}
