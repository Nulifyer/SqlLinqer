using System;

namespace SqlLinqer.Connections
{
    /// <summary>
    /// Configures schema fallback, insert batching, and command timeouts for a connector.
    /// The connector retains this options object; configure it before sharing the connector.
    /// </summary>
    public class ConnectorOptions
    {
        /// <summary>Optional execution observer. Configure before sharing the connector. Observer failures do not replace query results or errors.</summary>
        public SqlLinqer.Diagnostics.IQueryObserver Observer { get; set; }
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
        /// DuckDB applies a deadline to execution and result loading, with a supported range of zero through 4294967294 milliseconds. Deadline failures return an error containing TimeoutException.
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
