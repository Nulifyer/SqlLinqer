using System.Collections;
using System.Data;
using System.Data.Common;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SqlLinqer;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;

namespace SqlLinqer.RegressionTests;

internal static class CallableRegressionTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static DataTable Users(bool empty = false, bool nullId = false)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int)); table.Columns.Add("display_name", typeof(string));
        table.Columns.Add("Status", typeof(int)); table.Columns.Add("Optional", typeof(int));
        table.Columns.Add("IgnoredExtra", typeof(string));
        if (!empty) table.Rows.Add(nullId ? DBNull.Value : 7, "雪", 1, DBNull.Value, "extra");
        return table;
    }

    private static DataTable Totals()
    {
        var table = new DataTable(); table.Columns.Add("Value", typeof(long)); table.Rows.Add(22L); return table;
    }

    private static DbParameter Parameter(FakeCommand command, string name)
    {
        var match = Regex.Match(command.CommandText, "@" + Regex.Escape(name) + @" = (@P\d+)\b");
        Check(match.Success, "Parameter assignment absent: " + name);
        return command.Parameters[match.Groups[1].Value];
    }

    private static FakeDatabase Database(params DataTable[] tables) => new()
    {
        ReaderFactory = command => new ClosingReader(new DataTableReader(tables), () =>
        {
            Parameter(command, "Total").Value = 22;
            Parameter(command, "Counter").Value = 6;
            Parameter(command, "Message").Value = "done";
            var returnId = Regex.Match(command.CommandText, @"EXEC (@P\d+) =").Groups[1].Value;
            command.Parameters[returnId].Value = 17;
        }),
    };

    internal static void Run(Action<string, Action> test)
    {
        foreach (var flavor in new[] { DbFlavor.SqlServer, DbFlavor.AzureSql })
        {
            test($"{flavor} callable maps ordinary rows two sets and delayed outputs", () =>
            {
                var database = Database(Users(), Totals()); var connector = new FakeConnector(database, flavor);
                connector.Options.DefaultSchema = "tenant]schema";
                var args = new SearchUsers.Args { ManagerId = 12, Counter = 5 };
                var call = SearchUsers.Call(args);
                args.ManagerId = 99;
                var response = call.Execute(connector);
                Check(response.State == ResponseState.Valid, response.Error?.ToString());
                var result = response.Result;
                Check(result.Users.Single().Id == 7 && result.Users[0].Name == "雪"
                    && result.Users[0].Status == UserStatus.Active && result.Users[0].Optional == null
                    && result.Users[0].ApiOnly == null, "SQL model row mapping differs");
                Check(result.Counts.SequenceEqual(new[] { 22L }) && result.Total == 22
                    && result.Counter == 6 && result.Message == "done" && result.ReturnCode == 17, "Typed results/outputs differ");
                var command = database.Executed.Single();
                Check(command.CommandText.Contains("[tenant]]schema].[SearchUsers]")
                    && !command.CommandText.Contains("@Name ="), "Schema quoting or omitted default differs");
                Check((int)Parameter(command, "ManagerId").Value == 12 && args.Counter == 5, "Input snapshot or input mutation differs");
                Check(Parameter(command, "Counter").Direction == ParameterDirection.InputOutput
                    && Parameter(command, "Total").Direction == ParameterDirection.Output, "Output directions differ");
                Check(database.Connections.Single().IsDisposed && command.IsDisposed
                    && database.Connections[0].Transactions.Single().Commits == 1, "Owned lifecycle differs");
                Check(call.Render(connector).Text == call.Render(connector).Text, "Rendering changed invocation state");
                Check(call.Render(connector).GetTextWithParameterValues().StartsWith("EXEC @P1 = "),
                    "Diagnostic rendering replaced the return-code binding with a literal");
            });

            test($"{flavor} callable preserves caller transaction on success", () =>
            {
                var database = Database(Users(), Totals()); var connector = new FakeConnector(database, flavor);
                using var connection = new FakeConnection(database); connection.Open();
                using var transaction = (FakeTransaction)connection.BeginTransaction();
                var response = SearchUsers.Call(new SearchUsers.Args()).Execute(connector, transaction);
                Check(response.State == ResponseState.Valid, response.Error?.ToString());
                Check(connector.Created == 0 && !connection.IsDisposed && !transaction.IsDisposed
                    && transaction.Commits == 0 && transaction.Rollbacks == 0
                    && database.Executed.Single().UsedTransaction == transaction, "Caller ownership changed");
            });
        }

        test("callable typed NULL differs from an omitted parameter", () =>
        {
            var database = Database(Users(), Totals());
            var response = SearchUsers.Call(new SearchUsers.Args { ManagerId = null, Name = new SqlArgument<string>(null) })
                .Execute(new FakeConnector(database, DbFlavor.SqlServer));
            Check(response.State == ResponseState.Valid, response.Error?.ToString());
            var command = database.Executed.Single();
            Check(Parameter(command, "ManagerId").Value == DBNull.Value && Parameter(command, "ManagerId").DbType == DbType.Int32
                && Parameter(command, "Name").Value == DBNull.Value && Parameter(command, "Name").DbType == DbType.String,
                "Declared types were lost on SQL NULL");
        });

        test("callable preserves provider metadata and table parameter", () =>
        {
            using var ids = new DataTable(); ids.Columns.Add("Id", typeof(int)); ids.Rows.Add(3);
            var database = new FakeDatabase { ReaderFactory = _ => new DataTable().CreateDataReader() };
            var response = NativeInputs.Call(new NativeInputs.Args { Ids = ids, Amount = 12.25m, Stamp = DateTime.UtcNow })
                .Execute(new FakeConnector(database, DbFlavor.SqlServer));
            Check(response.State == ResponseState.Valid, response.Error?.ToString());
            var command = database.Executed.Single();
            var amount = Parameter(command, "Amount"); var stamp = (FakeParameter)Parameter(command, "Stamp");
            var tvp = (FakeParameter)Parameter(command, "Ids");
            Check(amount.Precision == 12 && amount.Scale == 2 && stamp.SqlDbType == SqlDbType.DateTime2
                && stamp.Scale == 7 && tvp.SqlDbType == SqlDbType.Structured && tvp.TypeName == "dbo.IdList"
                && ReferenceEquals(tvp.Value, ids), "Provider metadata changed");
        });

        test("callable accepts empty result sets and skips update counts", () =>
        {
            var database = Database(new DataTable(), Users(empty: true), new DataTable(), Totals());
            var response = SearchUsers.Call(new SearchUsers.Args()).Execute(new FakeConnector(database, DbFlavor.SqlServer));
            Check(response.State == ResponseState.Valid && response.Result.Users.Count == 0, response.Error?.ToString());
        });

        foreach (var failure in new[] { "missing", "extra", "null", "missing-column" })
            test("callable rejects result contract mismatch: " + failure, () =>
            {
                var users = Users(nullId: failure == "null");
                if (failure == "missing-column") users.Columns.Remove("display_name");
                var tables = failure == "missing" ? new[] { users }
                    : failure == "extra" ? new[] { users, Totals(), Totals() } : new[] { users, Totals() };
                var database = Database(tables);
                var response = SearchUsers.Call(new SearchUsers.Args()).Execute(new FakeConnector(database, DbFlavor.SqlServer));
                Check(response.State == ResponseState.Error && response.Result == null, "Mismatch produced a valid response");
                var transaction = database.Connections.Single().Transactions.Single();
                Check(transaction.Rollbacks == 1 && transaction.Commits == 0 && transaction.IsDisposed
                    && database.Connections[0].IsDisposed && database.Executed.Single().IsDisposed, "Mapping failure did not roll back and dispose");
            });

        test("callable preserves caller ownership on mapping failure", () =>
        {
            var database = Database(Users()); var connector = new FakeConnector(database, DbFlavor.SqlServer);
            using var connection = new FakeConnection(database); connection.Open();
            using var transaction = (FakeTransaction)connection.BeginTransaction();
            var response = SearchUsers.Call(new SearchUsers.Args()).Execute(connector, transaction);
            Check(response.State == ResponseState.Error && !connection.IsDisposed && !transaction.IsDisposed
                && transaction.Rollbacks == 0 && transaction.Commits == 0, "Failure changed caller ownership");
        });

        foreach (string stage in new[] { "open", "command", "parameter", "begin", "execute", "commit" })
            test("callable handles connector failure: " + stage, () =>
            {
                var database = Database(Users(), Totals()); database.FailAt = stage;
                var response = SearchUsers.Call(new SearchUsers.Args()).Execute(new FakeConnector(database, DbFlavor.SqlServer));
                Check(response.State == ResponseState.Error && database.Connections.Single().IsDisposed, "Failure escaped or connection leaked");
                Check(database.Connections[0].Commands.All(c => c.IsDisposed), "Command leaked");
            });

        test("callable definitions support external shared models and distinct routine identities", () =>
        {
            var first = ExternalOne.Call(new ExternalArgs { Value = 1 }).Render(DbFlavor.SqlServer);
            var second = ExternalTwo.Call(new ExternalArgs { Value = 2 }).Render(DbFlavor.SqlServer);
            Check(first.Text.Contains("[dbo].[One]") && second.Text.Contains("[custom].[Two]")
                && (int)first.Parameters.GetAll().Single().Value == 1 && (int)second.Parameters.GetAll().Single().Value == 2,
                "Shared models lost routine identity or argument isolation");
        });

        test("callable maps immutable row constructors and nullable scalar sets", () =>
        {
            var rows = new DataTable(); rows.Columns.Add("display_name", typeof(string)); rows.Rows.Add("雪");
            var scalars = new DataTable(); scalars.Columns.Add("Value", typeof(int)); scalars.Rows.Add(DBNull.Value);
            var database = new FakeDatabase { ReaderFactory = _ => new DataTableReader(new[] { rows, scalars }) };
            var response = ImmutableRows.Call(new ExternalArgs()).Execute(new FakeConnector(database, DbFlavor.SqlServer));
            Check(response.State == ResponseState.Valid && response.Result.Rows.Single().Name == "雪"
                && response.Result.Values.Single() == null, response.Error?.ToString());
        });

        test("callable rejects unsupported providers before creating a connection", () =>
        {
            foreach (var flavor in new[] { DbFlavor.PostgreSql, DbFlavor.MySql, DbFlavor.Sqlite, DbFlavor.DuckDb })
            {
                var connector = new FakeConnector(new FakeDatabase(), flavor);
                Throws<NotSupportedException>(() => SearchUsers.Call(new SearchUsers.Args()).Execute(connector));
                Check(connector.Created == 0, "Unsupported flavor opened a connection");
            }
        });

        test("callable validates definitions and arguments before execution", () =>
        {
            Throws<ArgumentNullException>(() => SearchUsers.Call(null));
            Throws<InvalidOperationException>(() => MissingAttribute.Call(new ExternalArgs()));
            Throws<InvalidOperationException>(() => InvalidName.Call(new InvalidName.Args()));
            Throws<InvalidOperationException>(() => InvalidSets.Call(new ExternalArgs()));
            Throws<InvalidOperationException>(() => UnsizedOutput.Call(new ExternalArgs()));
            Throws<InvalidOperationException>(() => ConflictingTypes.Call(new ExternalArgs()));
            Throws<InvalidOperationException>(() => OptionalOutput.Call(new OptionalOutput.Args()));
            Throws<ArgumentException>(() => NativeInputs.Call(new NativeInputs.Args()));
        });
    }

    [SqlProcedure("SearchUsers")]
    public sealed class SearchUsers : SqlLinqerCallable<SearchUsers, SearchUsers.Args, SearchUsers.Result>
    {
        public class Args
        {
            public int? ManagerId { get; set; }
            public SqlArgument<string> Name { get; set; }
            public int Counter { get; set; }
        }
        public class Result
        {
            [SqlResultSet(0)] public List<UserRow> Users { get; set; }
            [SqlResultSet(1)] public long[] Counts { get; set; }
            [SqlOutputParameter] public int Total { get; set; }
            [SqlOutputParameter] public int Counter { get; set; }
            [SqlOutputParameter(Size = 40)] public string Message { get; set; }
            [SqlReturnValue] public int ReturnCode { get; set; }
        }
    }
    public enum UserStatus { Inactive, Active }
    public class UserRow
    {
        public int Id { get; set; }
        [SqlColumn("display_name"), JsonIgnore] public string Name { get; set; }
        public UserStatus Status { get; set; }
        public int? Optional { get; set; }
        [SqlIgnore] public string ApiOnly { get; set; }
    }
    [SqlProcedure("NativeInputs")]
    public sealed class NativeInputs : SqlLinqerCallable<NativeInputs, NativeInputs.Args, ExternalResult>
    {
        public class Args
        {
            [SqlParameter(TypeName = "dbo.IdList")] public DataTable Ids { get; set; }
            [SqlParameter(Precision = 12, Scale = 2)] public decimal Amount { get; set; }
            [SqlParameter(SqlDbType = SqlDbType.DateTime2, Scale = 7)] public DateTime Stamp { get; set; }
        }
    }
    public class ExternalArgs { public int Value; }
    public class ExternalResult { }
    [SqlProcedure("One", Schema = "dbo")]
    public sealed class ExternalOne : SqlLinqerCallable<ExternalOne, ExternalArgs, ExternalResult> { }
    [SqlProcedure("Two", Schema = "custom")]
    public sealed class ExternalTwo : SqlLinqerCallable<ExternalTwo, ExternalArgs, ExternalResult> { }
    public sealed class MissingAttribute : SqlLinqerCallable<MissingAttribute, ExternalArgs, ExternalResult> { }
    [SqlProcedure("Invalid")]
    public sealed class InvalidName : SqlLinqerCallable<InvalidName, InvalidName.Args, ExternalResult>
    { public class Args { [SqlParameter("bad;name")] public int Value; } }
    [SqlProcedure("Invalid")]
    public sealed class InvalidSets : SqlLinqerCallable<InvalidSets, ExternalArgs, InvalidSets.Result>
    { public class Result { [SqlResultSet(1)] public List<int> Rows { get; set; } } }
    [SqlProcedure("Invalid")]
    public sealed class UnsizedOutput : SqlLinqerCallable<UnsizedOutput, ExternalArgs, UnsizedOutput.Result>
    { public class Result { [SqlOutputParameter] public string Text { get; set; } } }
    [SqlProcedure("Invalid")]
    public sealed class ConflictingTypes : SqlLinqerCallable<ConflictingTypes, ExternalArgs, ConflictingTypes.Result>
    { public class Result { [SqlOutputParameter("Value")] public long Value { get; set; } } }
    [SqlProcedure("Invalid")]
    public sealed class OptionalOutput : SqlLinqerCallable<OptionalOutput, OptionalOutput.Args, OptionalOutput.Result>
    {
        public class Args { public SqlArgument<int> Value { get; set; } }
        public class Result { [SqlOutputParameter] public int Value { get; set; } }
    }
    public class ImmutableRow
    {
        [SqlColumn("display_name")] public string Name { get; }
        public ImmutableRow(string name) { Name = name; }
    }
    [SqlProcedure("ImmutableRows")]
    public sealed class ImmutableRows : SqlLinqerCallable<ImmutableRows, ExternalArgs, ImmutableRows.Result>
    {
        public class Result
        {
            [SqlResultSet(0)] public IReadOnlyList<ImmutableRow> Rows { get; set; }
            [SqlResultSet(1)] public List<int?> Values { get; set; }
        }
    }

    // Output values become available on Close, as with SqlClient. A premature output read sees DBNull.
    private sealed class ClosingReader(DbDataReader inner, Action closed) : DbDataReader
    {
        private bool _closed;
        public override void Close() { if (_closed) return; _closed = true; inner.Close(); closed(); }
        protected override void Dispose(bool disposing) { if (disposing) { Close(); inner.Dispose(); } base.Dispose(disposing); }
        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => _closed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override bool Read() => inner.Read();
        public override bool NextResult() => inner.NextResult();
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length) => inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
        public override DataTable GetSchemaTable() => inner.GetSchemaTable();
    }
}
