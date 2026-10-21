namespace SqlLinqer
{
    /// <summary>
    /// Indicates whether a response contains a successful result or an error. The result value alone does not indicate success.
    /// </summary>
    public enum ResponseState
    {
        /// <summary>
        /// Response object contains an error
        /// </summary>
        Error = 0,
        /// <summary>
        /// Response object contains a valid response
        /// </summary>
        Valid = 1
    }
}
