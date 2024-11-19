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
            _type = connection.GetType();
            _connectionString = connection.ConnectionString;

            var connectionParams = _connectionString
                .TrimEnd(';')
                .Split(';')
                .Select(x => x.Split('='))
                .ToDictionary(x => x.FirstOrDefault(), x => x.LastOrDefault());

            string connTimeoutKey = "connectiontimeout";
            if (!connectionParams.ContainsKey(connTimeoutKey))
            {
                switch (this.DbFlavor)
                {
                    case DbFlavor.PostgreSql:
                        connTimeoutKey = "Timeout";
                        break;
                    case DbFlavor.MySql:
                    case DbFlavor.SqlServer:
                        connTimeoutKey = "Connect Timeout";
                        break;
                    default:
                        throw new NotSupportedException($"The {nameof(SqlLinqer.DbFlavor)} {this.DbFlavor} is not supported");
                }
            }

            if (connectionParams.ContainsKey(connTimeoutKey))
                connectionParams[connTimeoutKey] = Options.ConnectionTimeout.TotalSeconds.ToString();
            else
                connectionParams.Add(connTimeoutKey, Options.ConnectionTimeout.TotalSeconds.ToString());
            _connectionString = string.Join(";", connectionParams.Select(x => $"{x.Key}={x.Value}"));
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
            conn.ConnectionString = _connectionString;
            return conn;
        }
    }
}
