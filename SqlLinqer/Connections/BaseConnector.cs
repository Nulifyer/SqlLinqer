using System;
using System.Data.Common;
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
