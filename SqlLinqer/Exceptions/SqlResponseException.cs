using System;
using SqlLinqer.Components.Render;

namespace SqlLinqer.Exceptions
{
    /// <summary>
    /// Retains the rendered command and original exception for a database execution or result-mapping failure.
    /// InnerException retains provider details; Query retains SQL text and bindings. Validation can throw before execution creates this wrapper.
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

        /// <summary>
        /// Returns diagnostic SQL with input parameter values substituted. Output bindings remain placeholders.
        /// </summary>
        /// <returns>The failed command's diagnostic text.</returns>
        public string GetQueryAsString()
        {
            return Query.ToString();
        }
    }
}
