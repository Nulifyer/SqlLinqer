using System;
using System.Linq;
using System.Data.Common;

namespace SqlLinqer.Connections
{
    /// <summary>
    /// Creates fresh provider connections from a template's type and connection string. Provider authentication properties are not copied.
    /// AccessToken, AccessTokenCallback, and SqlCredential are not copied; use ConnectionFactoryConnector for authentication through provider properties.
    /// </summary>
    public class ReplicatorConnector : BaseConnector
    {
        private readonly Type _type;
        private readonly string _connectionString;

        /// <summary>
        /// Creates a connector from a provider template without retaining its authentication-property values.
        /// </summary>
        /// <param name="connection">A template provider connection. Its type and connection string are copied; the template itself is not used for query execution.</param>
        /// <param name="dbFlavor">The SQL dialect and JSON transport format.</param>
        /// <param name="options">Connector settings, or null to use their defaults.</param>
        public ReplicatorConnector(DbConnection connection, DbFlavor dbFlavor, ConnectorOptions options) : base(dbFlavor, options)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            _type = connection.GetType();
            var connectionParams = new DbConnectionStringBuilder { ConnectionString = connection.ConnectionString };

            // Embedded providers accept no server connection timeout setting.
            if (DbFlavor == DbFlavor.Sqlite || DbFlavor == DbFlavor.DuckDb)
            {
                _connectionString = connection.ConnectionString;
                return;
            }

            string connTimeoutKey;
            switch (DbFlavor)
            {
                case DbFlavor.PostgreSql:
                    connTimeoutKey = "Timeout";
                    break;
                case DbFlavor.MySql:
                case DbFlavor.SqlServer:
                case DbFlavor.AzureSql:
                    connTimeoutKey = "Connect Timeout";
                    break;
                default:
                    throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {DbFlavor} is not supported");
            }

            foreach (var key in connectionParams.Keys.Cast<string>().ToArray())
            {
                string normalized = key.Replace(" ", "").ToLowerInvariant();
                if ((DbFlavor == DbFlavor.PostgreSql && normalized == "timeout")
                    || (DbFlavor != DbFlavor.PostgreSql && (normalized == "connectiontimeout" || normalized == "connecttimeout")))
                    connectionParams.Remove(key);
            }

            connectionParams[connTimeoutKey] = Convert.ToInt32(Options.ConnectionTimeout.TotalSeconds);
            _connectionString = connectionParams.ConnectionString;
        }
        /// <summary>
        /// Creates a connector from a provider template without retaining its authentication-property values.
        /// </summary>
        /// <param name="connection">A template provider connection. Its type and connection string are copied; the template itself is not used for query execution.</param>
        /// <param name="dbFlavor">The SQL dialect and JSON transport format.</param>
        public ReplicatorConnector(DbConnection connection, DbFlavor dbFlavor) : this(connection, dbFlavor, new ConnectorOptions()) { }

        /// <inheritdoc/>
        public override DbConnection CreateConnection()
        {
            DbConnection conn = (DbConnection)Activator.CreateInstance(_type);
            try
            {
                conn.ConnectionString = _connectionString;
                return conn;
            }
            catch
            {
                conn.Dispose();
                throw;
            }
        }
    }
}
