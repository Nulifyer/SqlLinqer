using System.Collections;
using System.Data;
using System.Data.Common;
using SqlLinqer;
using SqlLinqer.Connections;

namespace SqlLinqer.Tests.Regression;

internal sealed class FakeDatabase
{
    public string FailAt;
    public int AsyncOpens, AsyncCommands, AsyncReads, AsyncNextResults, SyncCommands;
    public int DisposedReaders;
    public CommandBehavior LastReaderBehavior;
    public CancellationToken LastOpenToken, LastCommandToken, LastReadToken, LastNextToken;
    public Func<CancellationToken, Task> BeforeAsyncCommand, BeforeAsyncRead;

    public string FailSql;
    public int FailOnExecution;
    public bool FailRollback;
    public int Rows = 2;
    public long Total = 22;
    public Func<object> ScalarFactory;
    public string[] JsonRows;
    public Func<FakeCommand, DbDataReader> ReaderFactory;
    public readonly List<FakeConnection> Connections = new();
    public readonly List<FakeCommand> Executed = new();

    public void ThrowIf(string stage)
    {
        if (FailAt == stage) throw new InvalidOperationException(stage);
    }
}

internal sealed class FakeConnector(FakeDatabase database, DbFlavor flavor = DbFlavor.PostgreSql)
    : BaseConnector(flavor, new ConnectorOptions())
{
    public int Created { get; private set; }
    public override DbConnection CreateConnection()
    {
        Created++;
        return new FakeConnection(database);
    }
}

internal sealed class FakeConnection : DbConnection
{
    internal readonly FakeDatabase DatabaseState;
    private ConnectionState _state;
    public bool IsDisposed { get; private set; }
    internal readonly List<FakeCommand> Commands = new();
    internal readonly List<FakeTransaction> Transactions = new();

    public FakeConnection() : this(new FakeDatabase()) { }
    internal FakeConnection(FakeDatabase database)
    {
        DatabaseState = database;
        database.Connections.Add(this);
    }

    public override string ConnectionString { get; set; }
    public override string Database => "regression";
    public override string DataSource => "fake";
    public override string ServerVersion => "1";
    public override ConnectionState State => _state;
    public override void ChangeDatabase(string name) { }
    public override void Open()
    {
        DatabaseState.ThrowIf("open");
        _state = ConnectionState.Open;
    }
    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        DatabaseState.AsyncOpens++;
        DatabaseState.LastOpenToken = cancellationToken;
        DatabaseState.ThrowIf("open");
        _state = ConnectionState.Open;
    }
    public override void Close() => _state = ConnectionState.Closed;
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            // Disposal failures belong to explicit calls, never the finalizer thread.
            base.Dispose(false);
            return;
        }
        IsDisposed = true;
        Close();
        base.Dispose(disposing);
        DatabaseState.ThrowIf("dispose-connection");
    }
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        DatabaseState.ThrowIf("begin");
        var transaction = new FakeTransaction(this, isolationLevel);
        Transactions.Add(transaction);
        return transaction;
    }
    protected override DbCommand CreateDbCommand()
    {
        DatabaseState.ThrowIf("command");
        var command = new FakeCommand(this);
        Commands.Add(command);
        return command;
    }
}

internal sealed class FakeTransaction(FakeConnection connection, IsolationLevel isolationLevel) : DbTransaction
{
    public int Commits { get; private set; }
    public int Rollbacks { get; private set; }
    public bool IsDisposed { get; private set; }
    public bool Completed;
    public override IsolationLevel IsolationLevel => isolationLevel;
    protected override DbConnection DbConnection => Completed ? null : connection;
    public override void Commit() { Commits++; connection.DatabaseState.ThrowIf("commit"); }
    public override void Rollback()
    {
        Rollbacks++;
        if (connection.DatabaseState.FailRollback) throw new InvalidOperationException("rollback");
    }
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            base.Dispose(false);
            return;
        }
        IsDisposed = true;
        base.Dispose(disposing);
        connection.DatabaseState.ThrowIf("dispose-transaction");
    }
}

internal sealed class FakeCommand(FakeConnection connection) : DbCommand
{
    public bool UseStreamingMode { get; set; }
    public bool IsDisposed { get; private set; }
    public DbTransaction UsedTransaction => DbTransaction;
    public override string CommandText { get; set; }
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection DbConnection { get; set; } = connection;
    protected override DbTransaction DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameters();
    public override void Cancel() { }
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter()
    {
        connection.DatabaseState.ThrowIf("parameter");
        return new FakeParameter();
    }
    private void Execute()
    {
        connection.DatabaseState.Executed.Add(this);
        if (connection.DatabaseState.FailOnExecution == connection.DatabaseState.Executed.Count)
            throw new InvalidOperationException("execution " + connection.DatabaseState.FailOnExecution);
        connection.DatabaseState.ThrowIf("execute");
        if (connection.DatabaseState.FailSql != null && CommandText.TrimStart().StartsWith(connection.DatabaseState.FailSql))
            throw new InvalidOperationException("SQL stage failure: " + connection.DatabaseState.FailSql);
    }
    public override int ExecuteNonQuery() { connection.DatabaseState.SyncCommands++; Execute(); return 1; }
    public override object ExecuteScalar() { connection.DatabaseState.SyncCommands++; Execute(); return ScalarValue(); }
    private object ScalarValue() => connection.DatabaseState.ScalarFactory != null
        ? connection.DatabaseState.ScalarFactory() : connection.DatabaseState.Total;
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        connection.DatabaseState.SyncCommands++;
        return CreateReader();
    }
    private async Task BeforeAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        connection.DatabaseState.LastCommandToken = cancellationToken;
        if (connection.DatabaseState.BeforeAsyncCommand != null)
            await connection.DatabaseState.BeforeAsyncCommand(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        connection.DatabaseState.AsyncCommands++;
    }
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        await BeforeAsync(cancellationToken); Execute(); return 1;
    }
    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        await BeforeAsync(cancellationToken); Execute(); return ScalarValue();
    }
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        connection.DatabaseState.LastReaderBehavior = behavior;
        await BeforeAsync(cancellationToken);
        return new AsyncTrackingReader(CreateReader(), connection.DatabaseState);
    }
    private DbDataReader CreateReader()
    {
        Execute();
        if (connection.DatabaseState.ReaderFactory != null)
            return connection.DatabaseState.ReaderFactory(this);
        var table = new DataTable();
        if (CommandText.Contains("RETURNING") || CommandText.Contains("OUTPUT"))
        {
            table.Columns.Add("Id", typeof(int));
            table.Rows.Add(7);
        }
        else
        {
            table.Columns.Add("json", typeof(string));
            if (connection.DatabaseState.JsonRows != null)
            {
                foreach (string json in connection.DatabaseState.JsonRows)
                    table.Rows.Add(json);
            }
            else
            {
                for (int i = 0; i < connection.DatabaseState.Rows; i++)
                    table.Rows.Add("{\"Id\":7,\"Name\":\"sample\"}");
            }
        }
        return table.CreateDataReader();
    }
    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
        if (disposing) connection.DatabaseState.ThrowIf("dispose-command");
    }
}

internal sealed class FakeParameter : DbParameter
{
    public SqlDbType SqlDbType { get; set; }
    public string TypeName { get; set; }
    public override byte Precision { get; set; }
    public override byte Scale { get; set; }
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    public override string ParameterName { get; set; }
    public override string SourceColumn { get; set; }
    public override object Value { get; set; }
    public override bool SourceColumnNullMapping { get; set; }
    public override int Size { get; set; }
    public override void ResetDbType() { }
}

internal sealed class FakeParameters : DbParameterCollection
{
    private readonly List<DbParameter> _items = new();
    public override int Count => _items.Count;
    public override object SyncRoot => this;
    public override int Add(object value) { _items.Add((DbParameter)value); return Count - 1; }
    public override void AddRange(Array values) { foreach (var value in values) Add(value); }
    public override void Clear() => _items.Clear();
    public override bool Contains(object value) => _items.Contains((DbParameter)value);
    public override bool Contains(string name) => IndexOf(name) >= 0;
    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();
    public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
    public override int IndexOf(string name) => _items.FindIndex(p => p.ParameterName == name);
    public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
    public override void Remove(object value) => _items.Remove((DbParameter)value);
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    public override void RemoveAt(string name) => RemoveAt(IndexOf(name));
    protected override DbParameter GetParameter(int index) => _items[index];
    protected override DbParameter GetParameter(string name) => _items[IndexOf(name)];
    protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
    protected override void SetParameter(string name, DbParameter value) => _items[IndexOf(name)] = value;
}
