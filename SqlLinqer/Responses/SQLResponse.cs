using System;
using System.Collections.Generic;

namespace SqlLinqer
{
    /// <summary>
    /// A response from the database. The response is either valid with a result or error with an error.
    /// </summary>
    public class SqlResponse<T> : ISqlResponse
    {
        /// <inheritdoc/>
        public ResponseState State { get; private set; }
        /// <inheritdoc/>
        public Exception Error { get; private set; }
        /// <summary>
        /// The resulting value of the response
        /// </summary>
        public T Result { get; private set; }

        /// <summary>
        /// A response from the database. The response is either valid with a result or error with an error.
        /// </summary>
        public SqlResponse()
        {
            SetError(new InvalidOperationException("Response has not been set."));
        }
        /// <summary>
        /// A response from the database. The response is either valid with a result or error with an error.
        /// </summary>
        /// <param name="error">The response error</param>
        public SqlResponse(Exception error)
        {
            SetError(error);
        }
        /// <summary>
        /// A response from the database. The response is either valid with a result or error with an error.
        /// </summary>
        /// <param name="result">The valid response result</param>
        public SqlResponse(T result)
        {
            SetResult(result);
        }

        /// <summary>
        /// Sets the state as VALID and the result. Clears the error.
        /// </summary>
        /// <param name="result">The valid response result</param>
        public virtual void SetResult(T result)
        {
            State = ResponseState.Valid;
            Result = result;
            Error = null;
        }
        /// <summary>
        /// Sets the state as ERROR and the error. Clears the result.
        /// </summary>
        /// <param name="error">The response error</param>
        public virtual void SetError(Exception error)
        {
            State = ResponseState.Error;
            Result = default;
            Error = error;
        }
 
        /// <summary>
        /// An override-able fucntion that creates a dictionary from the response object
        /// </summary>
        protected virtual Dictionary<string, object> ToDictionaryCreate()
        {
            return new Dictionary<string, object>()
            {
                { nameof(this.State).ToLower() , this.State == ResponseState.Valid },
                { nameof(this.Error).ToLower() , this.Error?.Message },
                { nameof(this.Result).ToLower() , this.Result },
            };
        }

        /// <summary>
        /// Create a dictionary of the response object. Mainly used for simple serialization.
        /// </summary>
        public Dictionary<string, object> ToDictionary()
        {
            return ToDictionaryCreate();
        }
    }
}
