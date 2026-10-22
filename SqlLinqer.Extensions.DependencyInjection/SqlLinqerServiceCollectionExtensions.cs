using System;
using System.Data.Common;
using System.Linq;
using SqlLinqer;
using SqlLinqer.Connections;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>Registers reusable SqlLinqer connectors. Provider data sources and factory dependencies must have singleton lifetimes.</summary>
    public static class SqlLinqerServiceCollectionExtensions
    {
        /// <summary>Registers one default singleton connector. The factory must return a fresh connection on every call.</summary>
        public static IServiceCollection AddSqlLinqer(this IServiceCollection services, Func<DbConnection> connectionFactory,
            DbFlavor flavor, Action<ConnectorOptions> configure = null)
        {
            if (connectionFactory == null) throw new ArgumentNullException(nameof(connectionFactory));
            return AddSqlLinqer(services, _ => connectionFactory(), flavor, (_, options) => configure?.Invoke(options));
        }

        /// <summary>Registers one default singleton connector using singleton factory dependencies. Configuration runs once when the connector is first resolved.</summary>
        public static IServiceCollection AddSqlLinqer(this IServiceCollection services, Func<IServiceProvider, DbConnection> connectionFactory,
            DbFlavor flavor, Action<IServiceProvider, ConnectorOptions> configure = null)
        {
            Validate(services, connectionFactory);
            if (services.Any(d => d.ServiceType == typeof(BaseConnector) && !d.IsKeyedService))
                throw new InvalidOperationException("A default SqlLinqer connector is already registered. Use AddKeyedSqlLinqer for additional databases.");
            services.AddSingleton<BaseConnector>(provider => Create(provider, connectionFactory, flavor, configure));
            return services;
        }

        /// <summary>Registers a named singleton connector. The factory must return a fresh connection on every call.</summary>
        public static IServiceCollection AddKeyedSqlLinqer(this IServiceCollection services, object key, Func<DbConnection> connectionFactory,
            DbFlavor flavor, Action<ConnectorOptions> configure = null)
        {
            if (connectionFactory == null) throw new ArgumentNullException(nameof(connectionFactory));
            return AddKeyedSqlLinqer(services, key, _ => connectionFactory(), flavor, (_, options) => configure?.Invoke(options));
        }

        /// <summary>Registers a keyed singleton connector using singleton factory dependencies. The application owns registered data sources.</summary>
        public static IServiceCollection AddKeyedSqlLinqer(this IServiceCollection services, object key, Func<IServiceProvider, DbConnection> connectionFactory,
            DbFlavor flavor, Action<IServiceProvider, ConnectorOptions> configure = null)
        {
            Validate(services, connectionFactory);
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (services.Any(d => d.ServiceType == typeof(BaseConnector) && d.IsKeyedService && Equals(d.ServiceKey, key)))
                throw new InvalidOperationException("A SqlLinqer connector is already registered for this key.");
            services.AddKeyedSingleton<BaseConnector>(key, (provider, _) => Create(provider, connectionFactory, flavor, configure));
            return services;
        }

        private static void Validate(IServiceCollection services, Delegate factory)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
        }
        private static BaseConnector Create(IServiceProvider provider, Func<IServiceProvider, DbConnection> factory,
            DbFlavor flavor, Action<IServiceProvider, ConnectorOptions> configure)
        {
            var options = new ConnectorOptions();
            configure?.Invoke(provider, options);
            return new ConnectionFactoryConnector(() => factory(provider), flavor, options);
        }
    }
}
