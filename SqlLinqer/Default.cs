using SqlLinqer.Connections;

namespace SqlLinqer
{
    /// <summary>
    /// Contains the default parameters for <see cref="SqlLinqer"/> 
    /// </summary>
    public static class Default
    {
        /// <summary>
        /// The default database connector
        /// </summary>
        public static BaseConnector Connector { get; set; }
    }
}
