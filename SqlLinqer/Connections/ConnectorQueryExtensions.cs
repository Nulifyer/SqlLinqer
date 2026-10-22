using System;
using SqlLinqer.Queries.Typed;
using SqlLinqer.Queries.TypedExt;

namespace SqlLinqer
{
    /// <summary>Creates model queries bound to a connector and its current fallback schema.</summary>
    public static class ConnectorQueryExtensions
    {
        /// <summary>Creates a SELECT without selecting columns. No connection is opened.</summary>
        /// <typeparam name="T">The mapped model class.</typeparam>
        /// <param name="connector">The connector retained by the query.</param>
        /// <returns>A bound SELECT builder.</returns>
        public static SelectQuery<T> BeginSelect<T>(this Connections.BaseConnector connector) where T : class
            => new SelectQuery<T>(Schema(connector)) { BoundConnector = connector };

        /// <summary>Creates a bound SELECT with automatic column and relationship selection.</summary>
        /// <typeparam name="T">The mapped model class.</typeparam>
        /// <param name="connector">The connector retained by the query.</param>
        /// <returns>A SELECT using SqlAutoSelect configuration.</returns>
        public static SelectQuery<T> BeginSelectAuto<T>(this Connections.BaseConnector connector) where T : class
            => connector.BeginSelect<T>().SelectAuto();

        /// <summary>Creates a bound aggregate or named-projection initializer.</summary>
        /// <typeparam name="T">The mapped root model class.</typeparam>
        /// <param name="connector">The connector retained by the projection.</param>
        /// <returns>An initializer whose BuildQuery result retains the connector.</returns>
        public static SelectQueryInitializer<T> BeginSelectBuilder<T>(this Connections.BaseConnector connector) where T : class
            => new SelectQueryInitializer<T>(Schema(connector)) { BoundConnector = connector };

        /// <summary>Creates an empty bound insert builder.</summary>
        /// <typeparam name="T">The mapped model class.</typeparam>
        /// <param name="connector">The connector retained by the query.</param>
        /// <returns>An insert builder.</returns>
        public static InsertQuery<T> BeginInsert<T>(this Connections.BaseConnector connector) where T : class
            => new InsertQuery<T>(Schema(connector)) { BoundConnector = connector };

        /// <summary>Creates a bound insert builder that returns affected primary keys.</summary>
        /// <typeparam name="T">The mapped model class.</typeparam>
        /// <typeparam name="TKey">The primary-key type.</typeparam>
        /// <param name="connector">The connector retained by the query.</param>
        /// <returns>An insert builder retaining generated keys.</returns>
        public static InsertWithPrimaryKeyQuery<T, TKey> BeginInsert<T, TKey>(this Connections.BaseConnector connector) where T : class
            => new InsertWithPrimaryKeyQuery<T, TKey>(Schema(connector)) { BoundConnector = connector };

        /// <summary>Creates a bound update builder. Add a filter before execution.</summary>
        /// <typeparam name="T">The mapped model class.</typeparam>
        /// <param name="connector">The connector retained by the query.</param>
        /// <returns>An update builder.</returns>
        public static UpdateQuery<T> BeginUpdate<T>(this Connections.BaseConnector connector) where T : class
            => new UpdateQuery<T>(Schema(connector)) { BoundConnector = connector };

        /// <summary>Creates a bound update builder that returns affected primary keys. Add a filter before execution.</summary>
        /// <typeparam name="T">The mapped model class.</typeparam>
        /// <typeparam name="TKey">The primary-key type.</typeparam>
        /// <param name="connector">The connector retained by the query.</param>
        /// <returns>An update builder retaining affected keys.</returns>
        public static UpdateWithPrimaryKeyQuery<T, TKey> BeginUpdate<T, TKey>(this Connections.BaseConnector connector) where T : class
            => new UpdateWithPrimaryKeyQuery<T, TKey>(Schema(connector)) { BoundConnector = connector };

        /// <summary>Creates a bound delete builder. Add a filter before execution.</summary>
        /// <typeparam name="T">The mapped model class.</typeparam>
        /// <param name="connector">The connector retained by the query.</param>
        /// <returns>A delete builder.</returns>
        public static DeleteQuery<T> BeginDelete<T>(this Connections.BaseConnector connector) where T : class
            => new DeleteQuery<T>(Schema(connector)) { BoundConnector = connector };

        /// <summary>Creates a bound delete builder that returns affected primary keys. Add a filter before execution.</summary>
        /// <typeparam name="T">The mapped model class.</typeparam>
        /// <typeparam name="TKey">The primary-key type.</typeparam>
        /// <param name="connector">The connector retained by the query.</param>
        /// <returns>A delete builder retaining affected keys.</returns>
        public static DeleteWithPrimaryKeyQuery<T, TKey> BeginDelete<T, TKey>(this Connections.BaseConnector connector) where T : class
            => new DeleteWithPrimaryKeyQuery<T, TKey>(Schema(connector)) { BoundConnector = connector };

        /// <summary>Creates a bound command to remove every row. SQLite TRUNCATE is unsupported.</summary>
        /// <typeparam name="T">The mapped model class.</typeparam>
        /// <param name="connector">The connector retained by the query.</param>
        /// <returns>A truncate builder.</returns>
        public static TruncateQuery<T> BeginTruncate<T>(this Connections.BaseConnector connector) where T : class
            => new TruncateQuery<T>(Schema(connector)) { BoundConnector = connector };

        private static string Schema(Connections.BaseConnector connector)
            => (connector ?? throw new ArgumentNullException(nameof(connector))).Options.DefaultSchema;
    }
}
