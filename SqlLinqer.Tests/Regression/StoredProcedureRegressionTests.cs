using System.Collections;
using System.Data;
using System.Data.Common;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SqlLinqer.Modeling;

namespace SqlLinqer.Tests.Regression;

internal static partial class StoredProcedureRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        RunResponses(test);
        RunAudit(test);
    }
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

    private static DbParameter Parameter(FakeCommand command, string name)
    {
        var match = Regex.Match(command.CommandText, "@" + Regex.Escape(name) + @" = (@P\d+)\b");
        Check(match.Success, "Parameter assignment absent: " + name);
        return command.Parameters[match.Groups[1].Value];
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
    public class ExternalArgs { public int Value; }
    // Output values become available on Close, as with SqlClient. A premature output read sees DBNull.
    private sealed class ClosingReader(DbDataReader inner, Action closed, int? recordsAffected = null) : DbDataReader
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
        public override int RecordsAffected => recordsAffected ?? inner.RecordsAffected;
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
