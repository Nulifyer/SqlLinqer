using System.Collections.Generic;
using System.Data.Common;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Json;
using SqlLinqer.Components.Render;

namespace SqlLinqer.Connections
{
    public abstract partial class BaseConnector
    {
        private static readonly JsonSerializerOptions JsonOptions = ModelConfigResolver.CreateOptions();

        private static readonly JsonSerializerOptions SqliteJsonOptions = CreateSqliteJsonOptions();

        private static JsonSerializerOptions CreateSqliteJsonOptions()
        {
            var options = ModelConfigResolver.CreateOptions();
            options.Converters.Add(new DatabaseHexBytesConverter());
            return options;
        }

        /// <summary>
        /// Executes database JSON and buffers it into typed rows using SqlLinqer's SQL model contract.
        /// The command must return JSON in the dialect's transport format.
        /// </summary>
        /// <typeparam name="T">The mapped row type.</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <returns>The buffered rows and execution state. An empty JSON result array produces an empty list.</returns>
        public SqlResponse<List<T>> ExecuteReaderJson<T>(RenderedQuery query, DbTransaction existingTransaction = null) where T : class
        {
            var mapper = CreateJsonMapper<T>();
            return ExecuteOnConnection(query, existingTransaction, false, command =>
            {
                using (var reader = command.ExecuteReader())
                    return mapper.Read(reader);
            });
        }

        private JsonResultMapper<T> CreateJsonMapper<T>() where T : class
        {
            var options = DbFlavor == DbFlavor.Sqlite ? SqliteJsonOptions : JsonOptions;
            return new JsonResultMapper<T>(options, JsonResultsRowByRow());
        }

        /// <summary>
        /// Controls if the ExecuteReaderJson function parses results row by row or as a single blob
        /// </summary>
        public virtual bool JsonResultsRowByRow() => SqlLinqer.Compilation.SqlDialect.For(DbFlavor).JsonRows;

        /// <summary>
        /// Asynchronously executes database JSON and buffers it into typed rows using SqlLinqer's SQL model contract. Requested cancellation throws.
        /// The command must return JSON in the dialect's transport format.
        /// </summary>
        /// <typeparam name="T">The mapped row type.</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result loading.</param>
        /// <returns>The buffered rows and execution state. An empty JSON result array produces an empty list.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public Task<SqlResponse<List<T>>> ExecuteReaderJsonAsync<T>(RenderedQuery query,
            DbTransaction existingTransaction = null, CancellationToken cancellationToken = default) where T : class
        {
            var mapper = CreateJsonMapper<T>();
            return ExecuteOnConnectionAsync(query, existingTransaction, false, async command =>
            {
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    return await mapper.ReadAsync(reader, cancellationToken).ConfigureAwait(false);
            }, cancellationToken);
        }
    }
}
