using System;
using System.Collections.Generic;

namespace SqlLinqer
{
    /// <summary>
    /// Contains either a successful result or an error. State, rather than the result value, determines whether execution succeeded.
    /// Failure resets Result to default(T). A new response remains an error until SetResult is called.
    /// </summary>
    /// <typeparam name="T">The selected data, affected-row count, Boolean existence result, or other execution result.</typeparam>
    public class SqlResponse<T> : ISqlResponse
    {
        /// <inheritdoc/>
        public ResponseState State { get; private set; }
        /// <inheritdoc/>
        public Exception Error { get; private set; }
        /// <summary>
        /// The successful query result, or default(T) after failure. Check State before treating null, zero, or false as database output.
        /// </summary>
        public T Result { get; private set; }

        /// <summary>
        /// Creates an unset error response. SetResult supplies a successful result.
        /// </summary>
        public SqlResponse()
        {
            SetError(new InvalidOperationException("Response has not been set."));
        }
        /// <summary>
        /// Creates an error response and resets the typed result.
        /// </summary>
        /// <param name="error">The execution or mapping failure.</param>
        public SqlResponse(Exception error)
        {
            SetError(error);
        }
        /// <summary>
        /// Creates a successful response, including for empty or zero-valued results.
        /// </summary>
        /// <param name="result">The successfully returned value.</param>
        public SqlResponse(T result)
        {
            SetResult(result);
        }

        /// <summary>
        /// Marks the response valid, stores the result, and clears the previous error.
        /// </summary>
        /// <param name="result">The successful result, including a legitimate empty or zero-valued result.</param>
        public virtual void SetResult(T result)
        {
            State = ResponseState.Valid;
            Result = result;
            Error = null;
        }
        /// <summary>
        /// Marks the response as an error and resets Result to default(T).
        /// </summary>
        /// <param name="error">The failure to retain on the response.</param>
        public virtual void SetError(Exception error)
        {
            State = ResponseState.Error;
            Result = default;
            Error = error;
        }

        /// <summary>
        /// An override-able function that creates a dictionary from the response object
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
