using System;
using System.Collections.Generic;

namespace SqlLinqer
{
    /// <summary>
    /// Contains selected rows and the total root projection count before paging. Grouped and DISTINCT queries count their projected rows.
    /// Failure clears rows and resets TotalResults to zero.
    /// </summary>
    /// <typeparam name="T">The selected model or projection type.</typeparam>
    public class SqlSelectResponse<T> : SqlResponse<List<T>>
    {
        /// <summary>
        /// The number of projected results before root TOP, limit, and offset. It counts grouped or distinct results rather than their input rows.
        /// An empty page can have a positive total. Failure resets this value to zero.
        /// </summary>
        public long TotalResults { get; private set; }

        /// <inheritdoc/>
        public override void SetResult(List<T> response)
        {
            base.SetResult(response);
            TotalResults = 0;
        }

        /// <summary>
        /// Stores a successful selected page and its unpaged projection count, clearing any previous error.
        /// </summary>
        /// <param name="result">The selected page, which can be empty.</param>
        /// <param name="totalResults">The total number of projected results before root paging.</param>
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
