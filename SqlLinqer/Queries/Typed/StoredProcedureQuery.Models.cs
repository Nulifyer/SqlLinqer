using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;
using SqlLinqer.Connections;
using SqlLinqer.Modeling;
using SqlLinqer.Queries.Interfaces;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// Executes a SQL Server or Azure SQL procedure with an input model and one tabular output row model.
    /// Public input fields and readable properties become parameters unless marked SqlIgnore. Row mapping uses SQL attributes.
    /// </summary>
    /// <typeparam name="TInput">The input model, including optional SqlParameter and SqlArgument metadata.</typeparam>
    /// <typeparam name="TOutput">The returned row model.</typeparam>
    public sealed class StoredProcedureQuery<TInput, TOutput> : IQueryExecutable<List<TOutput>>, IAsyncQueryExecutable<List<TOutput>>
        where TInput : class
    {
        private static readonly ConcurrentDictionary<(string Name, string Schema, string TotalResultsParameter), CallableDefinition> Definitions
            = new ConcurrentDictionary<(string Name, string Schema, string TotalResultsParameter), CallableDefinition>();
        private readonly CallableInvocation<Procedure, TInput, RowResult> _call;
        internal BaseConnector BoundConnector { get; set; }
        private BaseConnector ExecutionConnector => BoundConnector ?? Default.Connector;

        /// <summary>
        /// Captures input member values without executing SQL. Later member assignments do not change this query.
        /// Referenced arrays and DataTables are not deep-copied and must remain unchanged until execution finishes.
        /// </summary>
        /// <param name="name">One procedure identifier, without a schema prefix.</param>
        /// <param name="input">The input model. Use an empty model for procedures without inputs.</param>
        /// <param name="schema">An explicit schema, or null to use the execution connector default.</param>
        /// <param name="totalResultsParameter">The optional output parameter supplying an unpaged total, with an optional leading @. Binds an Int64 output. Null requests no total.</param>
        public StoredProcedureQuery(string name, TInput input, string schema = null, string totalResultsParameter = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var key = (name, schema, totalResultsParameter);
            if (!Definitions.TryGetValue(key, out var definition))
                definition = Definitions.GetOrAdd(key, CreateDefinition);
            _call = new CallableInvocation<Procedure, TInput, RowResult>(input, definition);
        }

        /// <summary>Renders the procedure and provider bindings without opening a connection.</summary>
        /// <param name="flavor">SQL Server or Azure SQL.</param>
        /// <returns>The parameterized command, including RETURN and configured total bindings.</returns>
        public RenderedQuery Render(DbFlavor flavor) => _call.RenderWithMetadata(flavor);

        /// <summary>Renders the procedure using the connector dialect and fallback schema without opening a connection.</summary>
        /// <param name="connector">The SQL Server or Azure SQL connector.</param>
        /// <returns>The parameterized command and provider bindings.</returns>
        public RenderedQuery Render(BaseConnector connector)
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            return _call.RenderWithMetadata(connector);
        }

        /// <summary>Executes using the retained or current default connector and returns rows and standard metadata.</summary>
        /// <param name="transaction">An optional caller-owned transaction. The caller retains ownership.</param>
        /// <returns>The output rows, execution state, optional total, RETURN code, and affected-row count.</returns>
        public SqlStoredProcedureResponse<TOutput> Execute(DbTransaction transaction = null) => Execute(ExecutionConnector, transaction);

        /// <summary>Buffers the output rows and validates metadata before committing an owned transaction. Failure clears all output.</summary>
        /// <param name="connector">The SQL Server or Azure SQL connector used for this execution.</param>
        /// <param name="transaction">An optional caller-owned transaction. The caller retains ownership.</param>
        /// <returns>The output rows and standard metadata. Unknown counts remain null. A nonzero RETURN code does not set an error state.</returns>
        public SqlStoredProcedureResponse<TOutput> Execute(BaseConnector connector, DbTransaction transaction = null)
            => Response(_call.ExecuteWithMetadata(connector, transaction));

        /// <summary>Asynchronously executes using the retained or current default connector. Requested cancellation throws.</summary>
        /// <param name="transaction">An optional caller-owned transaction. The caller retains ownership.</param>
        /// <param name="cancellationToken">Cancels opening, execution, and result loading.</param>
        /// <returns>The output rows and standard metadata.</returns>
        public Task<SqlStoredProcedureResponse<TOutput>> ExecuteAsync(DbTransaction transaction = null, CancellationToken cancellationToken = default)
            => ExecuteAsync(ExecutionConnector, transaction, cancellationToken);

        /// <summary>Asynchronously buffers output rows and validates metadata before committing an owned transaction. Requested cancellation throws.</summary>
        /// <param name="connector">The SQL Server or Azure SQL connector used for this execution.</param>
        /// <param name="transaction">An optional caller-owned transaction. The caller retains ownership.</param>
        /// <param name="cancellationToken">Cancels opening, execution, and result loading.</param>
        /// <returns>The output rows and standard metadata. Unknown counts remain null.</returns>
        public async Task<SqlStoredProcedureResponse<TOutput>> ExecuteAsync(BaseConnector connector, DbTransaction transaction = null,
            CancellationToken cancellationToken = default)
            => Response(await _call.ExecuteWithMetadataAsync(connector, transaction, cancellationToken).ConfigureAwait(false));

        SqlResponse<List<TOutput>> IQueryExecutable<List<TOutput>>.Execute(DbTransaction transaction) => Execute(transaction);
        SqlResponse<List<TOutput>> IQueryExecutable<List<TOutput>>.Execute(BaseConnector connector, DbTransaction transaction) => Execute(connector, transaction);
        async Task<SqlResponse<List<TOutput>>> IAsyncQueryExecutable<List<TOutput>>.ExecuteAsync(DbTransaction transaction, CancellationToken cancellationToken)
            => await ExecuteAsync(transaction, cancellationToken).ConfigureAwait(false);
        async Task<SqlResponse<List<TOutput>>> IAsyncQueryExecutable<List<TOutput>>.ExecuteAsync(BaseConnector connector, DbTransaction transaction, CancellationToken cancellationToken)
            => await ExecuteAsync(connector, transaction, cancellationToken).ConfigureAwait(false);

        private static SqlStoredProcedureResponse<TOutput> Response(SqlResponse<CallableInvocation<Procedure, TInput, RowResult>.ProcedureResult> response)
        {
            var result = new SqlStoredProcedureResponse<TOutput>();
            if (response.State == ResponseState.Valid)
                result.SetResult(response.Result.Result.Rows, response.Result.TotalResults, response.Result.ReturnCode, response.Result.RowsAffected);
            else result.SetError(response.Error);
            return result;
        }

        private static CallableDefinition CreateDefinition((string Name, string Schema, string TotalResultsParameter) key)
            => new CallableDefinition(typeof(Procedure), typeof(TInput), typeof(RowResult),
                new SqlProcedure(key.Name) { Schema = key.Schema }, key.TotalResultsParameter);

        private sealed class Procedure : SqlLinqerCallable<Procedure, TInput, RowResult> { }
        private sealed class RowResult
        {
            public RowResult() { }
            [SqlResultSet(0)] public List<TOutput> Rows { get; set; }
        }
    }
}
