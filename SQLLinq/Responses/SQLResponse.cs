using System;
using System.Collections.Generic;

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

    /// <summary>
    /// A <see cref="SQLResponse{T}"/> contains the result of a sql query.
    /// The response is either valid and contains results or is in error and contains the error
    /// </summary>
    /// <typeparam name="T">The content type of the repsonse</typeparam>
    public class SQLResponse<T>
    {
        private T _content;
        private Exception _error;

        /// <summary>
        /// The response's state
        /// </summary>
        public ResponseState State { get; private set; }
        /// <summary>
        /// The content of the response
        /// </summary>
        public T Result { get => _content; set => SetResult(value); }
        /// <summary>
        /// The error of the reponse
        /// </summary>
        public Exception Error { get => _error; set => SetError(value); }

        /// <summary>
        /// Create a new repsonse with an error
        /// </summary>
        /// <param name="error"></param>
        public SQLResponse(Exception error)
        {
            SetError(error);
        }
        /// <summary>
        /// Create a new response with a result
        /// </summary>
        /// <param name="result">The result of the response</param>
        public SQLResponse(T result)
        {
            SetResult(result);
        }
        /// <summary>
        /// Create a new response. Default state is in error with no repsonse.
        /// </summary>
        public SQLResponse()
            : this(new InvalidOperationException("Response has not been set."))
        {

        }

        /// <summary>
        /// Sets the content of the response and updates the state to <see cref="ResponseState.Valid"/>
        /// </summary>
        /// <param name="response">the content of the reponse</param>
        /// <returns>The current <see cref="SQLResponse{T}"/> object</returns>
        public SQLResponse<T> SetResult(T response)
        {
            State = ResponseState.Valid;
            _content = response;
            _error = null;
            return this;
        }
        /// <summary>
        /// Sets the error of the response and updates the state to <see cref="ResponseState.Error"/>
        /// </summary>
        /// <param name="error">The error of the response</param>
        /// <returns>The current <see cref="SQLResponse{T}"/> object</returns>
        public SQLResponse<T> SetError(Exception error)
        {
            State = ResponseState.Error;
            _content = default;
            _error = error;
            return this;
        }
        /// <summary>
        /// Creates a dictionary representation of the class for json parsing.
        /// This creates a simpler object for serving to other systems or browser clients as json.
        /// </summary>
        /// <returns><see cref="Dictionary{TKey, TValue}"/> which contains each property name and value</returns>
        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>()
            {
                { "State", State },
                { "Response", Result },
                { "Error", Error?.Message }
            };
        }
    }
}
