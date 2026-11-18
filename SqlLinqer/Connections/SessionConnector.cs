using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Render;

namespace SqlLinqer.Connections
{
    /// <summary>
    /// Runs every command on one open connection that the caller owns. Create it with <see cref="BaseConnector.OnConnection"/>.
    /// It never opens, closes, or disposes the connection, and returns an error response when the connection is not open.
    /// It otherwise follows its source connector: a write opens and completes its own transaction on the connection,
    /// and a transaction passed to a terminal is used as given.
    /// A connection runs one command at a time, so do not share this connector between concurrent operations,
    /// and do not keep it longer than the connection. SQL Server table type management still uses separate connections.
    /// </summary>
    public sealed class SessionConnector : BaseConnector
    {
        /// <summary>The connector this session was created from. Its options and command hooks apply.</summary>
        public BaseConnector Source { get; }

        /// <summary>The caller-owned connection that every command runs on.</summary>
        public DbConnection Connection { get; }

        internal SessionConnector(BaseConnector source, DbConnection connection) : base(source)
        {
            Source = source;
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        internal override DbConnection SessionConnection => Connection;

        /// <summary>Creates a fresh connection through the source connector. Commands of this connector do not use it.</summary>
        /// <returns>A new provider connection configured for the database.</returns>
        public override DbConnection CreateConnection() => Source.CreateConnection();

        /// <inheritdoc/>
        protected override SqlResponse<T> ExecuteCommand<T>(RenderedQuery query, Func<SqlResponse<T>> action)
            => Source.RunCommand(query, action);

        /// <inheritdoc/>
        protected override Task<SqlResponse<T>> ExecuteCommandAsync<T>(RenderedQuery query, Func<Task<SqlResponse<T>>> action, CancellationToken cancellationToken)
            => Source.RunCommandAsync(query, action, cancellationToken);

        /// <inheritdoc/>
        protected override IAsyncEnumerable<T> ExecuteCommandAsyncStream<T>(RenderedQuery query,
            Func<CancellationToken, IAsyncEnumerable<T>> action, CancellationToken cancellationToken)
            => Source.RunCommandAsyncStream(query, action, cancellationToken);
    }
}
