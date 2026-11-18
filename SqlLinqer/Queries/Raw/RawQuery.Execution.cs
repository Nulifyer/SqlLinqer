using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Raw
{
    // Each terminal follows the library's two forms: one uses the bound or default connector, and one takes a connector.
    // Each also runs either with a caller transaction (or a pooled connection when none is supplied) or on exactly the
    // supplied open connection, where SqlLinqer opens no transaction and never closes or disposes the connection.
    public partial class RawQuery
    {
        private static BaseConnector Require(BaseConnector connector)
            => connector ?? throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

        private static DbConnection Require(DbConnection connection)
            => connection ?? throw new ArgumentNullException(nameof(connection));

        /// <summary>Executes the command and returns its affected-row count.</summary>
        /// <param name="transaction">An optional caller-owned transaction. Without one, an owned transaction commits on success unless WithoutTransaction was called.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The provider's affected-row count and execution state.</returns>
        public SqlResponse<int> Execute(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => Execute(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <summary>Executes the command and returns its affected-row count.</summary>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction. Without one, an owned transaction commits on success unless WithoutTransaction was called.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The provider's affected-row count and execution state.</returns>
        public SqlResponse<int> Execute(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteNonQuery(Render(connector), transaction, null, !_withoutTransaction, commandTimeout);

        /// <summary>Executes the command and returns its affected-row count. Runs on the supplied open connection, without a transaction.</summary>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The provider's affected-row count and execution state.</returns>
        public SqlResponse<int> Execute(DbConnection connection, TimeSpan? commandTimeout = null)
            => Execute(ExecutionConnector, connection, commandTimeout: commandTimeout);

        /// <summary>Executes the command and returns its affected-row count. Runs on the supplied open connection, without a transaction.</summary>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The provider's affected-row count and execution state.</returns>
        public SqlResponse<int> Execute(BaseConnector connector, DbConnection connection, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteNonQuery(Render(connector), null, Require(connection), false, commandTimeout);

        /// <summary>Asynchronously executes the command and returns its affected-row count. Requested cancellation throws.</summary>
        /// <param name="transaction">An optional caller-owned transaction. Without one, an owned transaction commits on success unless WithoutTransaction was called.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The provider's affected-row count and execution state.</returns>
        public Task<SqlResponse<int>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteAsync(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Asynchronously executes the command and returns its affected-row count. Requested cancellation throws.</summary>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction. Without one, an owned transaction commits on success unless WithoutTransaction was called.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The provider's affected-row count and execution state.</returns>
        public Task<SqlResponse<int>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteNonQueryAsync(Render(connector), transaction, null, !_withoutTransaction, cancellationToken, commandTimeout);

        /// <summary>Asynchronously executes the command and returns its affected-row count. Runs on the supplied open connection, without a transaction. Requested cancellation throws.</summary>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="cancellationToken">Cancels command execution and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The provider's affected-row count and execution state.</returns>
        public Task<SqlResponse<int>> ExecuteAsync(DbConnection connection, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteAsync(ExecutionConnector, connection, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Asynchronously executes the command and returns its affected-row count. Runs on the supplied open connection, without a transaction. Requested cancellation throws.</summary>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="cancellationToken">Cancels command execution and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The provider's affected-row count and execution state.</returns>
        public Task<SqlResponse<int>> ExecuteAsync(BaseConnector connector, DbConnection connection, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteNonQueryAsync(Render(connector), null, Require(connection), false, cancellationToken, commandTimeout);

        /// <summary>Reads the first row's first value and converts it to T. SQL NULL is allowed only when T can hold null.</summary>
        /// <typeparam name="T">The scalar result type.</typeparam>
        /// <param name="transaction">An optional caller-owned transaction. Without one, an owned transaction commits on success unless WithoutTransaction was called.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The converted scalar and execution state.</returns>
        public SqlResponse<T> ExecuteScalar<T>(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => ExecuteScalar<T>(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <summary>Reads the first row's first value and converts it to T. SQL NULL is allowed only when T can hold null.</summary>
        /// <typeparam name="T">The scalar result type.</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction. Without one, an owned transaction commits on success unless WithoutTransaction was called.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The converted scalar and execution state.</returns>
        public SqlResponse<T> ExecuteScalar<T>(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteScalar<T>(Render(connector), transaction, null, !_withoutTransaction, commandTimeout);

        /// <summary>Reads the first row's first value and converts it to T. SQL NULL is allowed only when T can hold null. Runs on the supplied open connection, without a transaction.</summary>
        /// <typeparam name="T">The scalar result type.</typeparam>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The converted scalar and execution state.</returns>
        public SqlResponse<T> ExecuteScalar<T>(DbConnection connection, TimeSpan? commandTimeout = null)
            => ExecuteScalar<T>(ExecutionConnector, connection, commandTimeout: commandTimeout);

        /// <summary>Reads the first row's first value and converts it to T. SQL NULL is allowed only when T can hold null. Runs on the supplied open connection, without a transaction.</summary>
        /// <typeparam name="T">The scalar result type.</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The converted scalar and execution state.</returns>
        public SqlResponse<T> ExecuteScalar<T>(BaseConnector connector, DbConnection connection, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteScalar<T>(Render(connector), null, Require(connection), false, commandTimeout);

        /// <summary>Asynchronously reads the first row's first value and converts it to T. SQL NULL is allowed only when T can hold null. Requested cancellation throws.</summary>
        /// <typeparam name="T">The scalar result type.</typeparam>
        /// <param name="transaction">An optional caller-owned transaction. Without one, an owned transaction commits on success unless WithoutTransaction was called.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The converted scalar and execution state.</returns>
        public Task<SqlResponse<T>> ExecuteScalarAsync<T>(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteScalarAsync<T>(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Asynchronously reads the first row's first value and converts it to T. SQL NULL is allowed only when T can hold null. Requested cancellation throws.</summary>
        /// <typeparam name="T">The scalar result type.</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction. Without one, an owned transaction commits on success unless WithoutTransaction was called.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The converted scalar and execution state.</returns>
        public Task<SqlResponse<T>> ExecuteScalarAsync<T>(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteScalarAsync<T>(Render(connector), transaction, null, !_withoutTransaction, cancellationToken, commandTimeout);

        /// <summary>Asynchronously reads the first row's first value and converts it to T. SQL NULL is allowed only when T can hold null. Runs on the supplied open connection, without a transaction. Requested cancellation throws.</summary>
        /// <typeparam name="T">The scalar result type.</typeparam>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="cancellationToken">Cancels command execution and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The converted scalar and execution state.</returns>
        public Task<SqlResponse<T>> ExecuteScalarAsync<T>(DbConnection connection, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteScalarAsync<T>(ExecutionConnector, connection, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Asynchronously reads the first row's first value and converts it to T. SQL NULL is allowed only when T can hold null. Runs on the supplied open connection, without a transaction. Requested cancellation throws.</summary>
        /// <typeparam name="T">The scalar result type.</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="cancellationToken">Cancels command execution and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The converted scalar and execution state.</returns>
        public Task<SqlResponse<T>> ExecuteScalarAsync<T>(BaseConnector connector, DbConnection connection, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteScalarAsync<T>(Render(connector), null, Require(connection), false, cancellationToken, commandTimeout);

        /// <summary>Maps each row onto T by column name, ignoring case, or reads a single column into a scalar list. The command runs without an owned transaction.</summary>
        /// <typeparam name="T">A row class, or a scalar type for a single-column result.</typeparam>
        /// <param name="transaction">An optional caller-owned transaction. Without one, the command runs without a transaction.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped rows and execution state.</returns>
        public SqlResponse<List<T>> ExecuteList<T>(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => ExecuteList<T>(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <summary>Maps each row onto T by column name, ignoring case, or reads a single column into a scalar list. The command runs without an owned transaction.</summary>
        /// <typeparam name="T">A row class, or a scalar type for a single-column result.</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction. Without one, the command runs without a transaction.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped rows and execution state.</returns>
        public SqlResponse<List<T>> ExecuteList<T>(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderList<T>(Render(connector), transaction, null, false, commandTimeout);

        /// <summary>Maps each row onto T by column name, ignoring case, or reads a single column into a scalar list. Runs on the supplied open connection, without a transaction.</summary>
        /// <typeparam name="T">A row class, or a scalar type for a single-column result.</typeparam>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped rows and execution state.</returns>
        public SqlResponse<List<T>> ExecuteList<T>(DbConnection connection, TimeSpan? commandTimeout = null)
            => ExecuteList<T>(ExecutionConnector, connection, commandTimeout: commandTimeout);

        /// <summary>Maps each row onto T by column name, ignoring case, or reads a single column into a scalar list. Runs on the supplied open connection, without a transaction.</summary>
        /// <typeparam name="T">A row class, or a scalar type for a single-column result.</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped rows and execution state.</returns>
        public SqlResponse<List<T>> ExecuteList<T>(BaseConnector connector, DbConnection connection, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderList<T>(Render(connector), null, Require(connection), false, commandTimeout);

        /// <summary>Asynchronously maps each row onto T by column name, ignoring case, or reads a single column into a scalar list. Requested cancellation throws. The command runs without an owned transaction.</summary>
        /// <typeparam name="T">A row class, or a scalar type for a single-column result.</typeparam>
        /// <param name="transaction">An optional caller-owned transaction. Without one, the command runs without a transaction.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped rows and execution state.</returns>
        public Task<SqlResponse<List<T>>> ExecuteListAsync<T>(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteListAsync<T>(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Asynchronously maps each row onto T by column name, ignoring case, or reads a single column into a scalar list. Requested cancellation throws. The command runs without an owned transaction.</summary>
        /// <typeparam name="T">A row class, or a scalar type for a single-column result.</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction. Without one, the command runs without a transaction.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped rows and execution state.</returns>
        public Task<SqlResponse<List<T>>> ExecuteListAsync<T>(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderListAsync<T>(Render(connector), transaction, null, false, cancellationToken, commandTimeout);

        /// <summary>Asynchronously maps each row onto T by column name, ignoring case, or reads a single column into a scalar list. Runs on the supplied open connection, without a transaction. Requested cancellation throws.</summary>
        /// <typeparam name="T">A row class, or a scalar type for a single-column result.</typeparam>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="cancellationToken">Cancels command execution and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped rows and execution state.</returns>
        public Task<SqlResponse<List<T>>> ExecuteListAsync<T>(DbConnection connection, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteListAsync<T>(ExecutionConnector, connection, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Asynchronously maps each row onto T by column name, ignoring case, or reads a single column into a scalar list. Runs on the supplied open connection, without a transaction. Requested cancellation throws.</summary>
        /// <typeparam name="T">A row class, or a scalar type for a single-column result.</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="cancellationToken">Cancels command execution and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped rows and execution state.</returns>
        public Task<SqlResponse<List<T>>> ExecuteListAsync<T>(BaseConnector connector, DbConnection connection, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderListAsync<T>(Render(connector), null, Require(connection), false, cancellationToken, commandTimeout);

        /// <summary>Buffers the result in a DataTable that the caller must dispose. The command runs without an owned transaction.</summary>
        /// <param name="transaction">An optional caller-owned transaction. Without one, the command runs without a transaction.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The buffered table and execution state.</returns>
        public SqlResponse<DataTable> ExecuteDataTable(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => ExecuteDataTable(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <summary>Buffers the result in a DataTable that the caller must dispose. The command runs without an owned transaction.</summary>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction. Without one, the command runs without a transaction.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The buffered table and execution state.</returns>
        public SqlResponse<DataTable> ExecuteDataTable(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderDataTable(Render(connector), transaction, null, false, commandTimeout);

        /// <summary>Buffers the result in a DataTable that the caller must dispose. Runs on the supplied open connection, without a transaction.</summary>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The buffered table and execution state.</returns>
        public SqlResponse<DataTable> ExecuteDataTable(DbConnection connection, TimeSpan? commandTimeout = null)
            => ExecuteDataTable(ExecutionConnector, connection, commandTimeout: commandTimeout);

        /// <summary>Buffers the result in a DataTable that the caller must dispose. Runs on the supplied open connection, without a transaction.</summary>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The buffered table and execution state.</returns>
        public SqlResponse<DataTable> ExecuteDataTable(BaseConnector connector, DbConnection connection, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderDataTable(Render(connector), null, Require(connection), false, commandTimeout);

        /// <summary>Asynchronously buffers the result in a DataTable that the caller must dispose. Requested cancellation throws. The command runs without an owned transaction.</summary>
        /// <param name="transaction">An optional caller-owned transaction. Without one, the command runs without a transaction.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The buffered table and execution state.</returns>
        public Task<SqlResponse<DataTable>> ExecuteDataTableAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteDataTableAsync(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Asynchronously buffers the result in a DataTable that the caller must dispose. Requested cancellation throws. The command runs without an owned transaction.</summary>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction. Without one, the command runs without a transaction.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The buffered table and execution state.</returns>
        public Task<SqlResponse<DataTable>> ExecuteDataTableAsync(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderDataTableAsync(Render(connector), transaction, null, false, cancellationToken, commandTimeout);

        /// <summary>Asynchronously buffers the result in a DataTable that the caller must dispose. Runs on the supplied open connection, without a transaction. Requested cancellation throws.</summary>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="cancellationToken">Cancels command execution and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The buffered table and execution state.</returns>
        public Task<SqlResponse<DataTable>> ExecuteDataTableAsync(DbConnection connection, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteDataTableAsync(ExecutionConnector, connection, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Asynchronously buffers the result in a DataTable that the caller must dispose. Runs on the supplied open connection, without a transaction. Requested cancellation throws.</summary>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="cancellationToken">Cancels command execution and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The buffered table and execution state.</returns>
        public Task<SqlResponse<DataTable>> ExecuteDataTableAsync(BaseConnector connector, DbConnection connection, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderDataTableAsync(Render(connector), null, Require(connection), false, cancellationToken, commandTimeout);

        /// <summary>Reads a command that returns several result sets, mapping each one into its own collection as ExecuteList maps rows. The number of result sets must equal the number of tuple elements. The command runs without an owned transaction.</summary>
        /// <typeparam name="TResults">A value tuple of collections in result-set order, such as (List&lt;Order&gt;, List&lt;OrderLine&gt;).</typeparam>
        /// <param name="transaction">An optional caller-owned transaction. Without one, the command runs without a transaction.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped collections and execution state.</returns>
        public SqlResponse<TResults> ExecuteLists<TResults>(DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => ExecuteLists<TResults>(ExecutionConnector, transaction, commandTimeout: commandTimeout);

        /// <summary>Reads a command that returns several result sets, mapping each one into its own collection as ExecuteList maps rows. The number of result sets must equal the number of tuple elements. The command runs without an owned transaction.</summary>
        /// <typeparam name="TResults">A value tuple of collections in result-set order, such as (List&lt;Order&gt;, List&lt;OrderLine&gt;).</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction. Without one, the command runs without a transaction.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped collections and execution state.</returns>
        public SqlResponse<TResults> ExecuteLists<TResults>(BaseConnector connector, DbTransaction transaction = null, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderLists<TResults>(Render(connector), transaction, null, false, commandTimeout);

        /// <summary>Reads a command that returns several result sets, mapping each one into its own collection as ExecuteList maps rows. The number of result sets must equal the number of tuple elements. Runs on the supplied open connection, without a transaction.</summary>
        /// <typeparam name="TResults">A value tuple of collections in result-set order, such as (List&lt;Order&gt;, List&lt;OrderLine&gt;).</typeparam>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped collections and execution state.</returns>
        public SqlResponse<TResults> ExecuteLists<TResults>(DbConnection connection, TimeSpan? commandTimeout = null)
            => ExecuteLists<TResults>(ExecutionConnector, connection, commandTimeout: commandTimeout);

        /// <summary>Reads a command that returns several result sets, mapping each one into its own collection as ExecuteList maps rows. The number of result sets must equal the number of tuple elements. Runs on the supplied open connection, without a transaction.</summary>
        /// <typeparam name="TResults">A value tuple of collections in result-set order, such as (List&lt;Order&gt;, List&lt;OrderLine&gt;).</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped collections and execution state.</returns>
        public SqlResponse<TResults> ExecuteLists<TResults>(BaseConnector connector, DbConnection connection, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderLists<TResults>(Render(connector), null, Require(connection), false, commandTimeout);

        /// <summary>Asynchronously reads a command that returns several result sets, mapping each one into its own collection as ExecuteList maps rows. The number of result sets must equal the number of tuple elements. The command runs without an owned transaction. Requested cancellation throws.</summary>
        /// <typeparam name="TResults">A value tuple of collections in result-set order, such as (List&lt;Order&gt;, List&lt;OrderLine&gt;).</typeparam>
        /// <param name="transaction">An optional caller-owned transaction. Without one, the command runs without a transaction.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped collections and execution state.</returns>
        public Task<SqlResponse<TResults>> ExecuteListsAsync<TResults>(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteListsAsync<TResults>(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Asynchronously reads a command that returns several result sets, mapping each one into its own collection as ExecuteList maps rows. The number of result sets must equal the number of tuple elements. The command runs without an owned transaction. Requested cancellation throws.</summary>
        /// <typeparam name="TResults">A value tuple of collections in result-set order, such as (List&lt;Order&gt;, List&lt;OrderLine&gt;).</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction. Without one, the command runs without a transaction.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped collections and execution state.</returns>
        public Task<SqlResponse<TResults>> ExecuteListsAsync<TResults>(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderListsAsync<TResults>(Render(connector), transaction, null, false, cancellationToken, commandTimeout);

        /// <summary>Asynchronously reads a command that returns several result sets, mapping each one into its own collection as ExecuteList maps rows. The number of result sets must equal the number of tuple elements. Runs on the supplied open connection, without a transaction. Requested cancellation throws.</summary>
        /// <typeparam name="TResults">A value tuple of collections in result-set order, such as (List&lt;Order&gt;, List&lt;OrderLine&gt;).</typeparam>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="cancellationToken">Cancels command execution and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped collections and execution state.</returns>
        public Task<SqlResponse<TResults>> ExecuteListsAsync<TResults>(DbConnection connection, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteListsAsync<TResults>(ExecutionConnector, connection, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Asynchronously reads a command that returns several result sets, mapping each one into its own collection as ExecuteList maps rows. The number of result sets must equal the number of tuple elements. Runs on the supplied open connection, without a transaction. Requested cancellation throws.</summary>
        /// <typeparam name="TResults">A value tuple of collections in result-set order, such as (List&lt;Order&gt;, List&lt;OrderLine&gt;).</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="cancellationToken">Cancels command execution and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>The mapped collections and execution state.</returns>
        public Task<SqlResponse<TResults>> ExecuteListsAsync<TResults>(BaseConnector connector, DbConnection connection, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderListsAsync<TResults>(Render(connector), null, Require(connection), false, cancellationToken, commandTimeout);

        /// <summary>Streams rows without buffering them, mapping each row as ExecuteList does. The SQL renders now and runs on enumeration, without an owned transaction. Dispose enumeration after an early exit. Requested cancellation throws.</summary>
        /// <typeparam name="T">A row class, or a scalar type for a single-column result.</typeparam>
        /// <param name="transaction">An optional caller-owned transaction. Without one, the command runs without a transaction.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>A deferred sequence. Mapping and database failures throw SqlResponseException during enumeration.</returns>
        public IAsyncEnumerable<T> ExecuteAsyncStream<T>(DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteAsyncStream<T>(ExecutionConnector, transaction, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Streams rows without buffering them, mapping each row as ExecuteList does. The SQL renders now and runs on enumeration, without an owned transaction. Dispose enumeration after an early exit. Requested cancellation throws.</summary>
        /// <typeparam name="T">A row class, or a scalar type for a single-column result.</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="transaction">An optional caller-owned transaction. Without one, the command runs without a transaction.</param>
        /// <param name="cancellationToken">Cancels connection opening, command execution, and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>A deferred sequence. Mapping and database failures throw SqlResponseException during enumeration.</returns>
        public IAsyncEnumerable<T> ExecuteAsyncStream<T>(BaseConnector connector, DbTransaction transaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderListAsyncStream<T>(Render(connector), transaction, null, cancellationToken, commandTimeout);

        /// <summary>Streams rows without buffering them, mapping each row as ExecuteList does. The SQL renders now and runs on enumeration, on the supplied open connection and without a transaction. Dispose enumeration after an early exit. Requested cancellation throws.</summary>
        /// <typeparam name="T">A row class, or a scalar type for a single-column result.</typeparam>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="cancellationToken">Cancels command execution and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>A deferred sequence. Mapping and database failures throw SqlResponseException during enumeration.</returns>
        public IAsyncEnumerable<T> ExecuteAsyncStream<T>(DbConnection connection, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteAsyncStream<T>(ExecutionConnector, connection, cancellationToken, commandTimeout: commandTimeout);

        /// <summary>Streams rows without buffering them, mapping each row as ExecuteList does. The SQL renders now and runs on enumeration, on the supplied open connection and without a transaction. Dispose enumeration after an early exit. Requested cancellation throws.</summary>
        /// <typeparam name="T">A row class, or a scalar type for a single-column result.</typeparam>
        /// <param name="connector">The connector to execute with.</param>
        /// <param name="connection">An open caller-owned connection. It stays open.</param>
        /// <param name="cancellationToken">Cancels command execution and result loading.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution.</param>
        /// <returns>A deferred sequence. Mapping and database failures throw SqlResponseException during enumeration.</returns>
        public IAsyncEnumerable<T> ExecuteAsyncStream<T>(BaseConnector connector, DbConnection connection, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => Require(connector).ExecuteReaderListAsyncStream<T>(Render(connector), null, Require(connection), cancellationToken, commandTimeout);
    }
}
