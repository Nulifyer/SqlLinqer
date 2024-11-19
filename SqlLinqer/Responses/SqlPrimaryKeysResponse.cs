using System;
using System.Collections.Generic;

namespace SqlLinqer
{
    /// <summary>
    /// A response containing a set of touched primary keys
    /// </summary>
    public class SqlPrimaryKeysResponse<T, TPrimaryKey> : SqlResponse<T>
    {
        /// <summary>
        /// The set of touched primary keys
        /// </summary>
        public List<TPrimaryKey> TouchedPrimaryKeys { get; protected set; }

        /// <inheritdoc/>
        public override void SetResult(T result)
        {
            base.SetResult(result);
            TouchedPrimaryKeys = new List<TPrimaryKey>();
        }
       
        /// <summary>
        /// Sets the state as VALID and the result. Clears the error.
        /// </summary>
        /// <param name="result">The valid response result</param>
        /// <param name="touchedPrimaryKeys">The list of touch primary keys</param>
        public void SetResult(T result, List<TPrimaryKey> touchedPrimaryKeys)
        {
            base.SetResult(result);
            TouchedPrimaryKeys = touchedPrimaryKeys;
        }

        /// <inheritdoc/>
        public override void SetError(Exception error)
        {
            base.SetError(error);
            TouchedPrimaryKeys = null;
        }

        /// <inheritdoc/>
        protected override Dictionary<string, object> ToDictionaryCreate()
        {
            var dic = base.ToDictionaryCreate();
            dic.Add(nameof(this.TouchedPrimaryKeys), this.TouchedPrimaryKeys);
            return dic;
        }
    }
}
