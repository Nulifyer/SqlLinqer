namespace SqlLinqer
{
    /// <summary>
    /// Indicates the state of the reponse being <see cref="ResponseState.Valid"/> or <see cref="ResponseState.Error"/>
    /// </summary>
    public enum ResponseState
    {
        /// <summary>
        /// Response object contains an error
        /// </summary>
        Error = 0,
        /// <summary>
        /// Response obejct contains a valid response
        /// </summary>
        Valid = 1
    }
}
