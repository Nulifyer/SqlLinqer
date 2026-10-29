using System;
using System.Data.Common;

namespace SqlLinqer.Connections
{
    /// <summary>
    /// Configures schema fallback, insert batching, and command timeouts for a connector.
    /// The connector retains this options object; configure it before sharing the connector.
    /// </summary>
    public class ConnectorOptions
    {
        /// <summary>Reuses persistent, contract-named SQL Server TVP types by default. False selects per-execution creation and cleanup. Managed types become eligible for cleanup after their leases and retention expire.</summary>
        public bool ReuseTvpTypes { get; set; } = true;
        /// <summary>The existing schema for managed reusable TVP types. Null uses the insert table schema, connector default, or dbo. Configure before sharing the connector.</summary>
        public string ReusableTvpSchema { get; set; }

        /// <summary>Enables lease tracking and opportunistic online cleanup for managed TVPs by default. Requires ReuseTvpTypes and all consumers to use the lease-aware execution protocol. False disables this connector's automatic cleanup checks.</summary>
        public bool AutoCleanupTvpTypes { get; set; } = true;
        /// <summary>The idle lease duration, renewed on execution using database UTC time. Defaults to five minutes. Must be positive and at most 365 days.</summary>
        public TimeSpan TvpLeaseDuration { get; set; } = TimeSpan.FromMinutes(5);
        /// <summary>Additional retention after a node lease expires. Defaults to one day. Must be nonnegative and at most 365 days.</summary>
        public TimeSpan TvpTypeRetention { get; set; } = TimeSpan.FromDays(1);
        /// <summary>The minimum interval between opportunistic cleanup attempts per connector and database/schema. Defaults to one hour. Must be positive and at most 365 days. No background timer is started.</summary>
        public TimeSpan TvpCleanupInterval { get; set; } = TimeSpan.FromHours(1);

        /// <summary>Optional execution observer. Configure before sharing the connector. Observer failures do not replace query results or errors.</summary>
        public SqlLinqer.Diagnostics.IQueryObserver Observer { get; set; }
        /// <summary>
        /// Optional provider interruption callback, used instead of DbCommand.Cancel for cancellation and execution deadlines.
        /// It runs on another thread and must interrupt only the supplied command's connection, return promptly, and tolerate repeated calls.
        /// Configure before sharing the connector. Its lifetime is synchronized with command completion, including streamed result disposal.
        /// Without this hook, interruption depends on the provider; Microsoft.Data.Sqlite 10 does not interrupt running queries.
        /// </summary>
        public Action<DbCommand> CancelCommand { get; set; }
        /// <summary>
        /// The fallback schema for model mappings that do not specify SqlTable.Schema. Defaults to null.
        /// Model queries resolve this at construction; typed callables resolve it at rendering or execution.
        /// </summary>
        public string DefaultSchema { get; set; }
        /// <summary>
        /// The ordinary insert batching budget, defaulting to 2,100 parameters. Non-positive values disable this configured budget.
        /// SQL Server and Azure SQL still cap commands at 2,100 parameters and 1,000 VALUES rows. Batches share one write transaction; Render does not apply execution batching.
        /// </summary>
        public int ParameterLimit { get; set; }
        /// <summary>
        /// The default command execution timeout, defaulting to 15 seconds. A per-execution commandTimeout overrides it without changing this value. Zero disables the timeout.
        /// DuckDB and connectors with CancelCommand apply a deadline to execution and result loading, with a supported range of zero through 4294967294 milliseconds. Deadline failures return an error containing TimeoutException.
        /// </summary>
        public TimeSpan CommandTimeout { get; set; }

        /// <summary>
        /// A set of options to configure a <see cref="BaseConnector"/>
        /// </summary>
        public ConnectorOptions()
        {
            DefaultSchema = null;
            ParameterLimit = 2100;
            CommandTimeout = TimeSpan.FromSeconds(15);
        }
    }
}
