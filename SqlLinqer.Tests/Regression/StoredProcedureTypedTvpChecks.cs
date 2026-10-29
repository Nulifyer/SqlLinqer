using System.Data;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Typed;

namespace SqlLinqer.Tests.Regression;

internal static partial class StoredProcedureRegressionTests
{
    private static void RunTypedTvps(Action<string, Action> test)
    {
        test("procedure TVPs share insert column order aliases exclusions nulls decimal and enum storage", () =>
        {
            var rows = new[] { new ObjectTvpRow { Id = 7, Amount = 12.3400m, Status = UserStatus.Active, Payload = new byte[] { 1, 2 } }, new ObjectTvpRow { Id = 8 } };
            using var expected = new InsertQuery<ObjectTvpRow>().InsertAuto(rows).Inserts.RenderAsDataTable();
            var query = new ObjectTvpCall<ObjectTvpRow[]>().Render(new ObjectTvpArgs<ObjectTvpRow[]> { Rows = rows }, DbFlavor.SqlServer);
            using var actual = (DataTable)query.Parameters.GetAll().Single(p => p.Value is DataTable).Value;
            Check(actual.Columns.Cast<DataColumn>().Select(c => (c.ColumnName, c.DataType))
                .SequenceEqual(expected.Columns.Cast<DataColumn>().Select(c => (c.ColumnName, c.DataType))), "Procedure TVP layout differs from insert mapping");
            Check(actual.Columns.Cast<DataColumn>().Select(c => (c.ColumnName, c.DataType)).SequenceEqual(new[]
                { ("row_id", typeof(int)), ("Amount", typeof(decimal)), ("Status", typeof(int)), ("Payload", typeof(byte[])) }),
                "TVP schema differs from the independent SQL table contract");
            Check(actual.Rows.Count == 2 && (int)actual.Rows[0]["row_id"] == 7 && (int)actual.Rows[1]["row_id"] == 8
                && ((byte[])actual.Rows[0]["Payload"]).SequenceEqual(new byte[] { 1, 2 })
                && actual.Rows[1]["Status"] == DBNull.Value && actual.Rows[1]["Payload"] == DBNull.Value,
                "TVP rows, order, binary values, or nulls differ from input expectations");
            Check(actual.Columns.Cast<DataColumn>().Select(c => c.ColumnName).SequenceEqual(new[] { "row_id", "Amount", "Status", "Payload" })
                && actual.Columns["Status"].DataType == typeof(int) && (int)actual.Rows[0]["Status"] == 1
                && (decimal)actual.Rows[0]["Amount"] == 12.34m && actual.Rows[1]["Amount"] == DBNull.Value, "Typed TVP value mapping differs");
        });
        foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql }) foreach (bool asynchronous in new[] { false, true })
        {
            test($"procedure TVPs accept single models arrays lists and deferred rows, {flavor}, async={asynchronous}", () =>
            {
                var row = new ObjectTvpRow { Id = 7 };
                Verify(row, 1); Verify(new[] { row }, 1); Verify(new List<ObjectTvpRow> { row }, 1);
                Verify(Array.Empty<ObjectTvpRow>(), 0); Verify(new List<ObjectTvpRow>(), 0);
                int enumerations = 0;
                IEnumerable<ObjectTvpRow> Deferred() { enumerations++; yield return row; }
                Verify(Deferred(), 1);
                Check(enumerations == 1, "Deferred TVP rows were enumerated more than once");
                Verify(new SqlArgument<ObjectTvpRow[]>(new[] { row }), 1);
                void Verify<T>(T inputRows, int count)
                {
                    bool disposed = false;
                    var db = new FakeDatabase
                    {
                        ReaderFactory = command =>
                        {
                            var table = (DataTable)Parameter(command, "Rows").Value;
                            table.Disposed += (_, _) => disposed = true;
                            Check(table.Columns.Count == 4 && table.Rows.Count == count, "Typed table lost its schema or rows");
                            return new ClosingReader(Numbers(42).CreateDataReader(), () => SetReturnCode(command, 0));
                        }
                    };
                    var call = new ObjectTvpCall<T>(); var args = new ObjectTvpArgs<T> { Rows = inputRows };
                    var connector = new FakeConnector(db, flavor);
                    var response = asynchronous ? call.ExecuteAsync(args, connector).GetAwaiter().GetResult() : call.Execute(args, connector);
                    Check(response.State == ResponseState.Valid && response.Result.Single() == 42 && disposed, response.Error?.ToString());
                }
            });
            test($"procedure typed TVPs omit null and reject explicit NULL or null rows before connecting, {flavor}, async={asynchronous}", () =>
            {
                var connector = new FakeConnector(new FakeDatabase(), flavor);
                var omitted = new ObjectTvpCall<ObjectTvpRow[]>().Render(new ObjectTvpArgs<ObjectTvpRow[]>(), flavor);
                Check(omitted.Parameters.Count == 2, "Null TVP was not omitted");
                Reject(new SqlArgument<ObjectTvpRow[]>(null));
                Reject(new ObjectTvpRow[] { null });
                void Reject<T>(T rows)
                {
                    var procedure = new ObjectTvpCall<T>(); var input = new ObjectTvpArgs<T> { Rows = rows };
                    Throws<ArgumentException>(() => { if (asynchronous) procedure.ExecuteAsync(input, connector).GetAwaiter().GetResult(); else procedure.Execute(input, connector); });
                }
                Check(connector.Created == 0, "Invalid TVP opened a connection");
            });
        }
        test("multi callable typed TVPs snapshot objects collections and binary cells before await", () =>
        {
            var row = new ObjectTvpRow { Id = 7, Payload = new byte[] { 1, 2 } };
            var rows = new List<ObjectTvpRow> { row };
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var db = MultiDatabase(Numbers(42), Numbers(43)); db.BeforeAsyncCommand = _ => gate.Task;
            var task = new ObjectTvpMultiCall().ExecuteAsync(new ObjectTvpArgs<IEnumerable<ObjectTvpRow>> { Rows = rows }, new FakeConnector(db, DbFlavor.SqlServer));
            try { row.Id = 99; row.Payload[0] = 9; rows.Clear(); }
            finally { gate.SetResult(); }
            Check(task.GetAwaiter().GetResult().State == ResponseState.Valid, "Typed multi TVP failed");
            var table = (DataTable)Parameter(db.Executed.Single(), "Rows").Value;
            Check(table.Rows.Count == 1 && (int)table.Rows[0]["row_id"] == 7 && ((byte[])table.Rows[0]["Payload"])[0] == 1,
                "Async call observed changes to typed TVP inputs");
        });
        test("typed TVP mapping rejects scalar rows duplicate columns and complex cells before connecting", () =>
        {
            Reject(new[] { 1 }); Reject(new DuplicateTvpRow[0]); Reject(new ComplexTvpRow[0]);
            void Reject<T>(T rows)
            {
                var connector = new FakeConnector(new FakeDatabase(), DbFlavor.SqlServer);
                Throws<InvalidOperationException>(() => new ObjectTvpCall<T>().Execute(new ObjectTvpArgs<T> { Rows = rows }, connector));
                Check(connector.Created == 0, "Invalid typed TVP opened a connection");
            }
        });
    }

    public sealed class ObjectTvpRow
    {
        [SqlColumn("row_id")] public int Id { get; set; }
        public decimal? Amount { get; set; }
        public UserStatus? Status { get; set; }
        public byte[] Payload { get; set; }
        [SqlIgnore] public object Ignored { get; set; }
        [SqlAutoInsertExclude] public object LocalOnly { get; set; }
    }
    private sealed class DuplicateTvpRow { [SqlColumn("Id")] public int First { get; set; } public int Id { get; set; } }
    private sealed class ComplexTvpRow { public object Value { get; set; } }
    private sealed class ObjectTvpArgs<T> { [SqlParameter(TypeName = "dbo.ObjectRows")] public T Rows { get; set; } }
    [SqlProcedure("ObjectTvp")]
    private sealed class ObjectTvpCall<T> : SqlLinqerCallable<ObjectTvpArgs<T>, int> { }
    [SqlProcedure("ObjectTvpMulti")]
    private sealed class ObjectTvpMultiCall : SqlLinqerMultiCallable<ObjectTvpArgs<IEnumerable<ObjectTvpRow>>, (int[], int[])> { }
}
