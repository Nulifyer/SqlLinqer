using System;
using System.Collections.Generic;

namespace SqlLinqer
{
    /// <summary>
    /// A response containing a total result count
    /// </summary>
    public class SqlSelectResponse<T> : SqlResponse<List<T>>
    {
        /// <summary>
        /// The total number of results reguardless of paging, limits, or offsets.
        /// </summary>
        public long TotalResults { get; private set; }

        /// <inheritdoc/>
        public override void SetResult(List<T> response)
        {
            base.SetResult(response);
            TotalResults = 0;
        }

        /// <summary>
        /// Sets the state as VALID and the result. Clears the error.
        /// </summary>
        /// <param name="result">The valid response result</param>
        /// <param name="totalResults">The total number of results</param>
        public void SetResult(List<T> result, long totalResults)
        {
            SetResult(result);
            TotalResults = totalResults;
        }

        /// <inheritdoc/>
        public override void SetError(Exception error)
        {
            base.SetError(error);
            TotalResults = 0;
        }

        /// <inheritdoc/>
        protected override Dictionary<string, object> ToDictionaryCreate()
        {
            var dic = base.ToDictionaryCreate();
            dic.Add(nameof(this.TotalResults), this.TotalResults);
            return dic;
        }
    }
}
