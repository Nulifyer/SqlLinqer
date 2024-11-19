using System;

namespace SqlLinqer
{
    /// <summary>
    /// A response from the SQL database
    /// </summary>
    public interface ISqlResponse
    {
        /// <summary>
        /// The state of the response
        /// </summary>
        ResponseState State { get; }

        /// <summary>
        /// The error thrown in the process of execution or from the database
        /// </summary>
        Exception Error { get; }
    }
}
