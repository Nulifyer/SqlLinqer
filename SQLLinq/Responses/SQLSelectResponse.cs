using System;
using System.Collections.Generic;

namespace SqlLinqer
{
    /// <summary>
    /// The <see cref="SQLSelectResponse{T}"/> is the same as a <see cref="SQLResponse{T}"/> but it also contains the possible total result count from the query
    /// </summary>
    /// <typeparam name="T">The content type of the response</typeparam>
    public class SQLSelectResponse<T> : SQLResponse<T>
    {
        /// <summary>
        /// The number of total possible results without paging
        /// </summary>
        public long TotalResults { get; set; }

        /// <inheritdoc/>
        public SQLSelectResponse() : base() => TotalResults = -1;
        /// <inheritdoc/>
        public SQLSelectResponse(Exception error) : base(error) => TotalResults = -1;
        /// <inheritdoc/>
        public SQLSelectResponse(T result) : base(result) => TotalResults = -1;

        /// <summary>
        /// Sets the reponse content and updates the state to <see cref="ResponseState.Valid"/>
        /// </summary>
        /// <param name="response">the content of the repsonse</param>
        /// <returns>The current <see cref="SQLSelectResponse{T}"/> object</returns>
        public new SQLSelectResponse<T> SetResult(T response)
        {
            base.SetResult(response);
            return this;
        }
        /// <summary>
        /// Sets thre responses error and updates the state to <see cref="ResponseState.Error"/>
        /// </summary>
        /// <param name="error">The exception error</param>
        /// <returns>The current <see cref="SQLSelectResponse{T}"/> object</returns>
        public new SQLSelectResponse<T> SetError(Exception error)
        {
            base.SetError(error);
            return this;
        }
        /// <summary>
        /// Creates a dictionary representation of the class for json parsing.
        /// This creates a simpler object for serving to other systems or browser clients as json.
        /// </summary>
        /// <returns><see cref="Dictionary{TKey, TValue}"/> which contains each property name and value</returns>
        public new Dictionary<string, object> ToDictionary()
        {
            var json = base.ToDictionary();
            json.Add("TotalResults", TotalResults);
            return json;
        }
    }
}
