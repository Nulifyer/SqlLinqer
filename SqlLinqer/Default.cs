using SqlLinqer.Connections;

namespace SqlLinqer
{
    /// <summary>
    /// Contains the default parameters for <see cref="SqlLinqer"/> 
    /// </summary>
    public static class Default
    {
        /// <summary>
        /// The connector used at execution time when a query has no retained or explicit execution connector.
        /// Model schemas are selected at query construction and do not change with this default.
        /// </summary>
        public static BaseConnector Connector { get; set; }
    }
}
