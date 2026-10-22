using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using LinqToDB.Common;
using LinqToDB.Interceptors;
using EfEvent = Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData;
using LinqEvent = LinqToDB.Interceptors.CommandEventData;

namespace SqlLinqer.Comparisons;

internal sealed class CommandLog
{
    public int Count { get; private set; }
    public bool Capture { get; set; }
    public List<object> Commands { get; } = [];
    public void Reset(bool capture = false) { Count = 0; Capture = capture; Commands.Clear(); }
    public void Record(DbCommand command)
    {
        Count++;
        if (Capture) Commands.Add(new
        {
            sql = command.CommandText,
            parameters = command.Parameters.Cast<DbParameter>().Select(p => new { name = p.ParameterName, value = p.Value is DBNull ? null : p.Value }).ToArray()
        });
    }
}

// Dapper, SqlKata and SqlLinqer accept an ADO.NET connection wrapper. EF and
// LINQ to DB use their native interceptors to preserve provider-specific types.
internal sealed class LoggedConnection(string connectionString, CommandLog log) : DbConnection
{
    private readonly SqliteConnection inner = new(connectionString);
    [AllowNull] public override string ConnectionString { get => inner.ConnectionString; set => inner.ConnectionString = value; }
    public override string Database => inner.Database;
    public override string DataSource => inner.DataSource;
    public override string ServerVersion => inner.ServerVersion;
    public override ConnectionState State => inner.State;
    public override void Open() => inner.Open();
    public override Task OpenAsync(CancellationToken token) => inner.OpenAsync(token);
    public override void Close() => inner.Close();
    public override void ChangeDatabase(string databaseName) => inner.ChangeDatabase(databaseName);
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => inner.BeginTransaction(isolationLevel);
    protected override DbCommand CreateDbCommand() => new LoggedCommand(inner.CreateCommand(), this, log);
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); GC.SuppressFinalize(this); }

    private sealed class LoggedCommand(DbCommand inner, LoggedConnection owner, CommandLog log) : DbCommand
    {
        [AllowNull] public override string CommandText { get => inner.CommandText; set => inner.CommandText = value; }
        public override int CommandTimeout { get => inner.CommandTimeout; set => inner.CommandTimeout = value; }
        public override CommandType CommandType { get => inner.CommandType; set => inner.CommandType = value; }
        public override bool DesignTimeVisible { get => inner.DesignTimeVisible; set => inner.DesignTimeVisible = value; }
        public override UpdateRowSource UpdatedRowSource { get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value; }
        protected override DbConnection? DbConnection { get => owner; set { if (value != owner) throw new NotSupportedException(); } }
        protected override DbTransaction? DbTransaction { get => inner.Transaction; set => inner.Transaction = value; }
        protected override DbParameterCollection DbParameterCollection => inner.Parameters;
        public override void Cancel() => inner.Cancel();
        public override void Prepare() => inner.Prepare();
        protected override DbParameter CreateDbParameter() => inner.CreateParameter();
        public override int ExecuteNonQuery() { log.Record(inner); return inner.ExecuteNonQuery(); }
        public override object? ExecuteScalar() { log.Record(inner); return inner.ExecuteScalar(); }
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) { log.Record(inner); return inner.ExecuteReader(behavior); }
        public override Task<int> ExecuteNonQueryAsync(CancellationToken token) { log.Record(inner); return inner.ExecuteNonQueryAsync(token); }
        public override Task<object?> ExecuteScalarAsync(CancellationToken token) { log.Record(inner); return inner.ExecuteScalarAsync(token); }
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
        { log.Record(inner); return inner.ExecuteReaderAsync(behavior, token); }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); GC.SuppressFinalize(this); }
    }
}

internal sealed class EfCommandLog(CommandLog log) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, EfEvent data, InterceptionResult<DbDataReader> result)
    { log.Record(command); return result; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, EfEvent data,
        InterceptionResult<DbDataReader> result, CancellationToken token = default)
    { log.Record(command); return ValueTask.FromResult(result); }
}

internal sealed class LinqCommandLog(CommandLog log) : ICommandInterceptor
{
    public DbCommand CommandInitialized(LinqEvent data, DbCommand command) => command;
    public Option<int> ExecuteNonQuery(LinqEvent data, DbCommand command, Option<int> result)
    { log.Record(command); return result; }
    public Task<Option<int>> ExecuteNonQueryAsync(LinqEvent data, DbCommand command, Option<int> result, CancellationToken token)
    { log.Record(command); return Task.FromResult(result); }
    public void AfterExecuteReader(LinqEvent data, DbCommand command, CommandBehavior behavior, DbDataReader reader) { }
    public void BeforeReaderDispose(LinqEvent data, DbCommand? command, DbDataReader reader) { }
    public Task BeforeReaderDisposeAsync(LinqEvent data, DbCommand? command, DbDataReader reader) => Task.CompletedTask;
    public Option<DbDataReader> ExecuteReader(LinqEvent eventData, DbCommand command, CommandBehavior behavior, Option<DbDataReader> result)
    { log.Record(command); return result; }
    public Task<Option<DbDataReader>> ExecuteReaderAsync(LinqEvent eventData, DbCommand command, CommandBehavior behavior,
        Option<DbDataReader> result, CancellationToken token)
    { log.Record(command); return Task.FromResult(result); }
    public Option<object?> ExecuteScalar(LinqEvent eventData, DbCommand command, Option<object?> result)
    { log.Record(command); return result; }
    public Task<Option<object?>> ExecuteScalarAsync(LinqEvent eventData, DbCommand command, Option<object?> result, CancellationToken token)
    { log.Record(command); return Task.FromResult(result); }
}
