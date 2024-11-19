using System;
using SqlLinqer.Components.Render;

namespace SqlLinqer.Exceptions
{
    /// <summary>
    /// An exception that occurs while executing a query
    /// </summary>
    public sealed class SqlResponseException : Exception
    {
        /// <summary>
        /// The query that was being executed when the error occured.
        /// </summary>
        public RenderedQuery Query { get; }

        /// <summary>
        /// An exception that occurs while executing a query
        /// </summary>
        /// <param name="query">The query that was being executed</param>
        /// <param name="innerException">The error that was thrown.</param>
        public SqlResponseException(RenderedQuery query, Exception innerException) : base(innerException.Message, innerException)
        {
            Query = query;
        }

        public string GetQueryAsString()
        {
            return Query.ToString();
        }
    }
}
