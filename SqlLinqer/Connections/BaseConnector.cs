using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Render;

namespace SqlLinqer.Connections
{
    /// <summary>
    /// Creates database connections, binds rendered parameters, and maps command results into response objects.
    /// Each owned execution obtains and disposes its own connection. Supplied transactions retain caller ownership of their connection, commit, rollback, and disposal.
    /// </summary>
    public abstract partial class BaseConnector
    {
        /// <summary>
        /// The type of Sql database
        /// </summary>
        public DbFlavor DbFlavor { get; }

        /// <summary>
        /// A set of configuration options
        /// </summary>
        public ConnectorOptions Options { get; }

        /// <summary>
        /// Creates connections and executes commands to the database
        /// </summary>
        /// <param name="dbFlavor">The type of Sql database</param>
        /// <param name="options">A set of configuration options</param>
        protected BaseConnector(DbFlavor dbFlavor, ConnectorOptions options)
        {
            DbFlavor = dbFlavor;
            Options = options ?? new ConnectorOptions();
        }

        /// <summary>
        /// Creates a fresh provider connection. A caller using it directly owns opening, closing, and disposal.
        /// </summary>
        /// <returns>A new provider connection configured for the database.</returns>
        public abstract DbConnection CreateConnection();

        /// <summary>
        /// Creates a connection and opens it. The caller owns the open connection and disposes it.
        /// If opening fails, the connection is disposed and the provider's exception is thrown.
        /// </summary>
        /// <returns>An open provider connection.</returns>
        public DbConnection OpenConnection()
        {
            var connection = CreateConnection();
            try
            {
                if (connection.State != ConnectionState.Open)
                    connection.Open();
                return connection;
            }
            catch
            {
                // A failed disposal must not hide the reason the connection could not open.
                try { connection.Dispose(); }
                catch { }
                throw;
            }
        }

        /// <summary>
        /// Asynchronously creates a connection and opens it. The caller owns the open connection and disposes it.
        /// If opening fails or is cancelled, the connection is disposed and the exception is thrown.
        /// </summary>
        /// <param name="cancellationToken">Cancels provider connection opening.</param>
        /// <returns>An open provider connection.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connection = CreateConnection();
            try
            {
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return connection;
            }
            catch
            {
                try { connection.Dispose(); }
                catch { }
                throw;
            }
        }

        // Set only on the copy that OnConnection returns. Every command of that copy runs on this connection.
        private DbConnection _sessionConnection;

        /// <summary>
        /// Returns a connector that runs every command on one open connection, which the caller owns.
        /// Use it to keep session state, such as an advisory lock or a temporary table, across typed queries.
        /// The returned connector never opens, closes, or disposes the connection, and reports an error response when it is not open.
        /// It otherwise behaves as this connector does: a write opens and completes its own transaction on the connection,
        /// and a transaction passed to a terminal is used as given. Do not run its commands concurrently.
        /// SQL Server TVP type management still uses separate connections.
        /// </summary>
        /// <param name="connection">An open connection without an active transaction. Pass an active transaction to each terminal instead.</param>
        /// <returns>A shallow copy of this connector bound to the connection. It shares this connector's options.</returns>
        public BaseConnector OnConnection(DbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            var session = (BaseConnector)MemberwiseClone();
            session._sessionConnection = connection;
            return session;
        }

        private void ImportFromTempQueryData(DbCommand command, RenderedQuery queryData)
        {
            command.CommandText = queryData.Text;

            foreach (var param in queryData.Parameters.GetAll())
            {
                DbParameter dbParam = command.CreateParameter();
                dbParam.ParameterName = DbFlavor == DbFlavor.DuckDb ? param.Placehodler.TrimStart('$') : param.Placehodler;
                if (DbFlavor == DbFlavor.DuckDb && param is TvpParameter)
                    throw new NotSupportedException("DuckDB does not support table-valued parameters.");

                switch (param.Value)
                {
                    case null:
                        dbParam.Value = DBNull.Value;
                        break;
                    default:
                        dbParam.Value = DbFlavor == DbFlavor.DuckDb && param.Value is Enum
                            ? Convert.ChangeType(param.Value, Enum.GetUnderlyingType(param.Value.GetType()))
                            : param.Value;
                        break;
                }

                if (param.DbType.HasValue)
                    dbParam.DbType = param.DbType.Value;

                if (param is CallableParameter callableParameter)
                    callableParameter.Configure(dbParam);

                if (DbFlavor == DbFlavor.Sqlite && param.Value is Enum enumValue)
                    dbParam.Value = Convert.ChangeType(enumValue, Enum.GetUnderlyingType(enumValue.GetType()));

                if (param is TvpParameter tvpParam)
                {
                    if (DbFlavor == DbFlavor.Sqlite)
                        throw new NotSupportedException("SQLite does not support table-valued parameters.");
                    var parameterType = dbParam.GetType();
                    parameterType.GetProperty("TypeName")?.SetValue(dbParam, tvpParam.TypeName);
                    var sqlDbType = parameterType.GetProperty("SqlDbType");
                    if (sqlDbType != null)
                        sqlDbType.SetValue(dbParam, Enum.Parse(sqlDbType.PropertyType, "Structured"));
                }

                command.Parameters.Add(dbParam);
            }
        }

        internal TimeSpan ResolveCommandTimeout(TimeSpan? commandTimeout)
        {
            var timeout = commandTimeout ?? Options.CommandTimeout;
            double maximumSeconds = DbFlavor == DbFlavor.DuckDb || Options.CancelCommand != null ? (uint.MaxValue - 1) / 1000.0 : int.MaxValue;
            if (timeout < TimeSpan.Zero || timeout.TotalSeconds > maximumSeconds)
                throw new ArgumentOutOfRangeException(nameof(commandTimeout), "Command timeout must be zero or a supported positive duration.");
            return timeout;
        }

        private static void PreProcessCommand(DbCommand command, TimeSpan commandTimeout)
            => command.CommandTimeout = (int)Math.Ceiling(commandTimeout.TotalSeconds);
    }
}
