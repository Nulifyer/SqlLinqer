using System;
using System.Data.Common;

namespace SqlLinqer.Connections
{
    /// <summary>
    /// Creates a fresh provider connection through an application factory, preserving property-based authentication such as tokens and credential callbacks.
    /// The factory must return fresh connections, which the connector owns and disposes. Supplied transactions provide the query connection; generated TVP type commands still use separate factory connections to the same server and write database. Configure login timeout in the factory.
    /// </summary>
    public sealed class ConnectionFactoryConnector : BaseConnector
    {
        private readonly Func<DbConnection> _factory;

        /// <summary>Creates a connector without copying or replacing provider properties.</summary>
        /// <param name="factory">Creates a fresh connection with its authentication properties configured.</param>
        /// <param name="dbFlavor">The database dialect and JSON result format.</param>
        /// <param name="options">Command options. Configure connection timeout in the factory.</param>
        public ConnectionFactoryConnector(Func<DbConnection> factory, DbFlavor dbFlavor, ConnectorOptions options = null)
            : base(dbFlavor, options)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <inheritdoc/>
        public override DbConnection CreateConnection()
            => _factory() ?? throw new InvalidOperationException("The connection factory returned null.");
    }
}
