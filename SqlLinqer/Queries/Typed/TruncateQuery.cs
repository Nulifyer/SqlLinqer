using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Connections;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Queries.Interfaces;

namespace SqlLinqer.Queries.Typed
{
    /// <summary>
    /// Builds TRUNCATE for the mapped table, removing every row without a WHERE filter. SQLite TRUNCATE is unsupported.
    /// DuckDB deletes all rows without resetting sequences. Affected-row reporting follows the provider.
    /// </summary>
    /// <typeparam name="T">The mapped root model type.</typeparam>
    public class TruncateQuery<T>
        : Core.TruncateQuery
        , IQueryTyped
        , IQueryExecutable<int>
        , IAsyncQueryExecutable<int>
        where T : class
    {

        /// <summary>
        /// Creates a TRUNCATE builder for the supplied or cached model mapping.
        /// DuckDB deletes all rows without resetting sequences.
        /// </summary>
        /// <param name="default_schema">The fallback schema for models without an explicit SqlTable schema. This does not bind an execution connector.</param>
        public TruncateQuery(string default_schema = null) : this(Table.GetCached<T>(default_schema)) { }

        /// <summary>
        /// Creates a TRUNCATE builder for the supplied or cached model mapping.
        /// DuckDB deletes all rows without resetting sequences.
        /// </summary>
        /// <param name="table">The root table mapping, whose database objects must already exist.</param>
        public TruncateQuery(Table table) : base(table) { }

        /// <inheritdoc/>
        public SqlResponse<int> Execute(DbTransaction existingTransaction = null)
        {
            return this.Execute(ExecutionConnector, existingTransaction);
        }

        /// <inheritdoc/>
        public SqlResponse<int> Execute(BaseConnector connector, DbTransaction existingTransaction = null)
        {
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

            var rendered = this.Render(connector);
            var response = connector.ExecuteNonQuery(rendered, existingTransaction);

            return response;
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<int>> ExecuteAsync(DbTransaction existingTransaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await this.ExecuteAsync(ExecutionConnector, existingTransaction, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<SqlResponse<int>> ExecuteAsync(BaseConnector connector, DbTransaction existingTransaction = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (connector == null)
                throw new ArgumentNullException(nameof(connector), "Either pass a connector or set the default connector.");

            var rendered = this.Render(connector);
            var response = await connector.ExecuteNonQueryAsync(rendered, existingTransaction, cancellationToken: cancellationToken).ConfigureAwait(false);

            return response;
        }
    }
}
