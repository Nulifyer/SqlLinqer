using System;
using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace SqlLinqer.Connections
{
    // Owns the connection and the transaction of one execution. Every path that runs commands takes them here:
    // a single command, the batches of one insert, a table-valued insert, and a stream. The scope releases only
    // what the library opened. A caller's transaction and a session connection are used and left as they are.
    internal sealed class ExecutionScope : IDisposable
    {
        private readonly bool _ownsConnection;
        private bool _ownsTransaction;

        internal DbConnection Connection { get; }
        // The caller's transaction, or the owned transaction once one is begun, or null.
        internal DbTransaction Transaction { get; private set; }

        private ExecutionScope(DbConnection connection, bool ownsConnection, DbTransaction existingTransaction)
        {
            Connection = connection;
            _ownsConnection = ownsConnection;
            Transaction = existingTransaction;
        }

        // independent takes a fresh connection even on a session connector. Table type and lease management use it.
        private static ExecutionScope Create(BaseConnector connector, DbTransaction existingTransaction, bool independent)
        {
            if (independent) return new ExecutionScope(connector.CreateConnection(), true, null);
            var connection = connector.ResolveConnection(existingTransaction, out bool ownsConnection);
            return new ExecutionScope(connection, ownsConnection, existingTransaction);
        }

        internal static ExecutionScope Open(BaseConnector connector, DbTransaction existingTransaction, bool independent = false)
        {
            var scope = Create(connector, existingTransaction, independent);
            try
            {
                if (scope.Connection.State != ConnectionState.Open) scope.Connection.Open();
                return scope;
            }
            catch
            {
                // A failed disposal must not hide the reason the connection could not open.
                try { scope.Dispose(); }
                catch { }
                throw;
            }
        }

        internal static async Task<ExecutionScope> OpenAsync(BaseConnector connector, DbTransaction existingTransaction,
            CancellationToken cancellationToken, bool independent = false)
        {
            var scope = Create(connector, existingTransaction, independent);
            try
            {
                if (scope.Connection.State != ConnectionState.Open)
                    await scope.Connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return scope;
            }
            catch
            {
                try { scope.Dispose(); }
                catch { }
                throw;
            }
        }

        // Opens the library's own transaction unless the caller supplied one.
        // netstandard2.0 has no asynchronous transaction lifecycle contract, so this has no asynchronous form.
        internal void BeginOwnedTransaction(IsolationLevel isolationLevel)
        {
            if (Transaction != null) return;
            Transaction = Connection.BeginTransaction(isolationLevel);
            _ownsTransaction = true;
        }

        // Takes over a transaction that a helper began on this connection. A caller's own transaction stays the caller's.
        internal void Adopt(DbTransaction transaction)
        {
            if (ReferenceEquals(transaction, Transaction)) return;
            Transaction = transaction;
            _ownsTransaction = true;
        }

        internal void Commit()
        {
            if (_ownsTransaction) Transaction.Commit();
        }

        // A failed rollback must not hide the original error.
        internal void Rollback()
        {
            if (!_ownsTransaction) return;
            try { Transaction.Rollback(); }
            catch { }
        }

        public void Dispose()
        {
            Exception failure = null;
            if (_ownsTransaction)
            {
                try { Transaction?.Dispose(); }
                catch (Exception error) { failure = error; }
            }
            if (_ownsConnection)
            {
                try { Connection.Dispose(); }
                catch (Exception error) { if (failure == null) failure = error; }
            }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
