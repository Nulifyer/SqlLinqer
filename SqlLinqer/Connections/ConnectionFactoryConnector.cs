using System;
using System.Data.Common;

namespace SqlLinqer.Connections
{
    /// <summary>
    /// Creates fresh provider connections through an application factory. Configure connection settings, including login timeout, pooling, and authentication, in the factory.
    /// The factory must return fresh connections, which the connector opens and disposes. Supplied transactions provide the query connection; generated TVP type commands still use separate factory connections to the same server and write database.
    /// </summary>
    public sealed class ConnectionFactoryConnector : BaseConnector
    {
        private readonly Func<DbConnection> _factory;

        /// <summary>Creates a connector without copying or replacing provider properties.</summary>
        /// <param name="factory">Creates a fresh connection with all provider settings configured. Connection strings and provider properties are used without modification.</param>
        /// <param name="dbFlavor">The database dialect and JSON result format.</param>
        /// <param name="options">Schema, batching, and command execution options. Configure connection settings in the factory.</param>
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
