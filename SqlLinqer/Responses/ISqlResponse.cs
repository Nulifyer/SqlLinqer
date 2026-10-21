using System;

namespace SqlLinqer
{
    /// <summary>
    /// Distinguishes a successful query from an execution or mapping failure. Check State before reading a typed result.
    /// </summary>
    public interface ISqlResponse
    {
        /// <summary>
        /// Indicates success or failure independently of the returned value. Zero affected rows and an empty row list can both be successful.
        /// </summary>
        ResponseState State { get; }

        /// <summary>
        /// Contains the failure when State is Error and is cleared on success. Query-validation errors may throw before a response exists.
        /// </summary>
        Exception Error { get; }
    }
}
