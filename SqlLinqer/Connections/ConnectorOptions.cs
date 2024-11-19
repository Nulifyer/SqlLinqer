using System;

namespace SqlLinqer.Connections
{
    /// <summary>
    /// A set of options to configure a <see cref="BaseConnector"/>
    /// </summary>
    public class ConnectorOptions
    {
        /// <summary>
        /// The default schema to use if not set on the model
        /// </summary>
        public string DefaultSchema { get; set; }
        /// <summary>
        /// The parameter limit of the database connection
        /// </summary>
        public int ParameterLimit { get; set; }
        /// <summary>
        /// The timeout for making a connection to the database
        /// </summary>
        public TimeSpan ConnectionTimeout { get; set; }
        /// <summary>
        /// The timeout for completing a command to the database
        /// </summary>
        public TimeSpan CommandTimeout { get; set; }

        /// <summary>
        /// A set of options to configure a <see cref="BaseConnector"/>
        /// </summary>
        public ConnectorOptions()
        {
            DefaultSchema = null;
            ParameterLimit = 2100;
            ConnectionTimeout = TimeSpan.FromSeconds(15);
            CommandTimeout = TimeSpan.FromSeconds(15);
        }
    }
}
