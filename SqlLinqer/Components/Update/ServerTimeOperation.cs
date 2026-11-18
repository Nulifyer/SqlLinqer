using System;
using SqlLinqer.Components.Render;
using SqlLinqer.Queries.Core;

namespace SqlLinqer.Components.Update
{
    /// <summary>
    /// Sets a column to the database server's current time instead of a value sent by the application.
    /// SQL Server, Azure SQL, MySQL, and SQLite assign the current UTC time. PostgreSQL and DuckDB assign the current timestamp with its time zone,
    /// which is an exact instant in a time-zone-aware column and the session time zone's local time in a plain timestamp column.
    /// </summary>
    public class ServerTimeOperation : UpdateOperation
    {
        /// <summary>Renders the assignment of the server's current time to the target column.</summary>
        /// <param name="query">The update query being rendered.</param>
        /// <param name="statement">The assignment whose column receives the time.</param>
        /// <param name="parameters">The bindings for the command. This operation adds none.</param>
        /// <param name="flavor">The target database.</param>
        /// <returns>The rendered assignment.</returns>
        public override string Render(UpdateQuery query, UpdateStatement statement, ParameterCollection parameters, DbFlavor flavor)
        {
            return $"{statement.RenderTarget(flavor)} = {Expression(flavor)}";
        }

        // The expression a write assigns. Server-time reads use ReadUtc so every provider returns the same shape.
        internal static string Expression(DbFlavor flavor)
        {
            switch (flavor)
            {
                case DbFlavor.SqlServer:
                case DbFlavor.AzureSql:
                    return "SYSUTCDATETIME()";
                case DbFlavor.MySql:
                    return "UTC_TIMESTAMP(6)";
                case DbFlavor.Sqlite:
                    return "strftime('%Y-%m-%dT%H:%M:%fZ', 'now')";
                case DbFlavor.PostgreSql:
                    return "CURRENT_TIMESTAMP";
                case DbFlavor.DuckDb:
                    // DuckDB resolves a bare CURRENT_TIMESTAMP in a conflict assignment as a column name.
                    return "now()";
                default:
                    throw new NotSupportedException($"The database flavor {flavor} is not supported.");
            }
        }

        // A query returning the current UTC time as a timestamp without a zone.
        internal static string ReadUtc(DbFlavor flavor)
        {
            switch (flavor)
            {
                case DbFlavor.PostgreSql:
                    return "SELECT CURRENT_TIMESTAMP AT TIME ZONE 'UTC'";
                case DbFlavor.DuckDb:
                    return "SELECT epoch_us(now())";
                case DbFlavor.Sqlite:
                    return "SELECT strftime('%Y-%m-%d %H:%M:%f', 'now')";
                default:
                    return "SELECT " + Expression(flavor);
            }
        }
    }
}
