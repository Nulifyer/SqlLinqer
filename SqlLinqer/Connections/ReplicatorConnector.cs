using System;
using System.Linq;
using System.Data.Common;

namespace SqlLinqer.Connections
{
    /// <summary>
    /// Creates new connections to the database by replicating the example connection passed to it.
    /// </summary>
    public class ReplicatorConnector : BaseConnector
    {
        private readonly Type _type;
        private readonly string _connectionString;

        /// <summary>
        /// Creates new connections to the database by replicating the example connection passed to it.
        /// </summary>
        /// <param name="connection">The connection to replicate</param>        
        /// <param name="dbFlavor">The type of Sql database</param>
        /// <param name="options">A set of configuration options</param>
        public ReplicatorConnector(DbConnection connection, DbFlavor dbFlavor, ConnectorOptions options) : base(dbFlavor, options)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            _type = connection.GetType();
            var connectionParams = new DbConnectionStringBuilder { ConnectionString = connection.ConnectionString };

            string connTimeoutKey;
            switch (DbFlavor)
            {
                case DbFlavor.PostgreSql:
                    connTimeoutKey = "Timeout";
                    break;
                case DbFlavor.MySql:
                case DbFlavor.SqlServer:
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
        /// Creates new connections to the database by replicating the example connection passed to it.
        /// </summary>
        /// <param name="connection">The connection to replicate</param>        
        /// <param name="dbFlavor">The type of Sql database</param>
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
