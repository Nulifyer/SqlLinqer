using System.Data;
using SqlLinqer.Connections;

namespace SqlLinqer.Tests.Regression;

internal static partial class StoredProcedureRegressionTests
{
    private static void RunCallableOwnershipProofs(Action<string, Action> test)
    {
        foreach (bool asynchronous in new[] { false, true })
        {
            foreach (string outcome in asynchronous ? new[] { "success", "mapping", "commit", "cancel" } : new[] { "success", "mapping", "commit" })
                test($"generated TVP tables close after readers on {outcome}, async={asynchronous}", () =>
                {
                    using var cancellation = new CancellationTokenSource();
                    bool readerClosed = false, tableDisposed = false;
                    var database = new FakeDatabase
                    {
                        FailAt = outcome == "commit" ? "commit" : null,
                        ReaderFactory = command =>
                        {
                            var table = (DataTable)Parameter(command, "Rows").Value;
                            table.Disposed += (_, _) =>
                            {
                                Check(readerClosed, "Disposed TVP payload before closing the database reader");
                                tableDisposed = true;
                            };
                            return new ClosingReader(ProofRows(outcome == "mapping").CreateDataReader(),
                                () => { readerClosed = true; SetReturnCode(command, 0); });
                        }
                    };
                    if (outcome == "cancel") database.BeforeAsyncRead = _ => { cancellation.Cancel(); return Task.CompletedTask; };
                    var connector = new FakeConnector(database, DbFlavor.SqlServer);
                    var input = new ObjectTvpArgs<ObjectTvpRow[]> { Rows = new[] { new ObjectTvpRow { Id = 7 } } };
                    try
                    {
                        var response = asynchronous
                            ? new ObjectTvpCall<ObjectTvpRow[]>().ExecuteAsync(input, connector, cancellationToken: cancellation.Token).GetAwaiter().GetResult()
                            : new ObjectTvpCall<ObjectTvpRow[]>().Execute(input, connector);
                        Check(outcome != "cancel", "Expected cancellation was returned as a response");
                        Check(response.State == (outcome == "success" ? ResponseState.Valid : ResponseState.Error), response.Error?.ToString());
                        if (outcome != "success") Check(response.Result == null && response.ReturnCode == null && response.RowsAffected == null,
                            "Failed TVP call retained output");
                    }
                    catch (OperationCanceledException error) when (outcome == "cancel" && error.CancellationToken == cancellation.Token) { }
                    var connection = database.Connections.Single(); var transaction = connection.Transactions.Single();
                    Check(readerClosed && tableDisposed && connection.IsDisposed && connection.Commands.All(c => c.IsDisposed),
                        "Owned payload, reader, command, or connection leaked");
                    Check(transaction.Rollbacks == (outcome == "success" ? 0 : 1), "Failed TVP call did not roll back owned work");
                });
            foreach (bool mappingFailure in new[] { false, true })
                test($"callable TVP cleanup preserves caller-owned DataTables, failed={mappingFailure}, async={asynchronous}", () =>
                {
                    using var inputTable = Numbers(7);
                    bool inputDisposed = false, boundTableDisposed = false, readerClosed = false;
                    inputTable.Disposed += (_, _) => inputDisposed = true;
                    var database = new FakeDatabase
                    {
                        ReaderFactory = command =>
                        {
                            var bound = (DataTable)Parameter(command, "Rows").Value;
                            Check(ReferenceEquals(inputTable, bound) != asynchronous, "Async TVP did not copy its caller table, or sync replaced it");
                            bound.Disposed += (_, _) => boundTableDisposed = true;
                            return new ClosingReader(ProofRows(mappingFailure).CreateDataReader(), () => { readerClosed = true; SetReturnCode(command, 0); });
                        }
                    };
                    var args = new ObjectTvpArgs<DataTable> { Rows = inputTable };
                    var connector = new FakeConnector(database, DbFlavor.SqlServer);
                    var response = asynchronous ? new ObjectTvpCall<DataTable>().ExecuteAsync(args, connector).GetAwaiter().GetResult()
                        : new ObjectTvpCall<DataTable>().Execute(args, connector);
                    Check(response.State == (mappingFailure ? ResponseState.Error : ResponseState.Valid), response.Error?.ToString());
                    Check(readerClosed && !inputDisposed && boundTableDisposed == asynchronous && (int)inputTable.Rows[0][0] == 7,
                        "Cleanup disposed the caller's table or leaked an owned snapshot");
                });
        }
    }

    private static DataTable ProofRows(bool invalid)
    {
        if (!invalid) return Numbers(42);
        var table = new DataTable(); table.Columns.Add("Value", typeof(string)); table.Rows.Add("invalid-number");
        return table;
    }
}
