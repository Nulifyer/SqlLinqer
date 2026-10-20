using System;
using System.Data.Common;

namespace SqlLinqer.Connections
{
    /// <summary>Creates provider connections using an application-supplied factory.</summary>
    /// <remarks>
    /// The factory must return a new connection on every call. The connector owns and disposes
    /// these connections. Caller-supplied transactions bypass the factory and remain caller-owned.
    /// Configure login timeout and authentication in the factory; ConnectionTimeout does not rewrite
    /// its connections. CommandTimeout still applies to commands executed by this connector.
    /// </remarks>
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
