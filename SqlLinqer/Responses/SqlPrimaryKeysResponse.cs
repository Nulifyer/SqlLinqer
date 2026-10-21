using System;
using System.Collections.Generic;

namespace SqlLinqer
{
    /// <summary>
    /// Contains a write result and the primary keys affected by a successful operation.
    /// </summary>
    /// <typeparam name="T">The write result type, normally an affected-row count.</typeparam>
    /// <typeparam name="TPrimaryKey">The affected key type.</typeparam>
    public class SqlPrimaryKeysResponse<T, TPrimaryKey> : SqlResponse<T>
    {
        /// <summary>
        /// The keys returned by successful insert, update, or delete execution. An error response clears this list.
        /// Batches combine their keys in provider order; sorted or input-correlated order is not guaranteed.
        /// </summary>
        public List<TPrimaryKey> TouchedPrimaryKeys { get; protected set; }

        /// <inheritdoc/>
        public override void SetResult(T result)
        {
            base.SetResult(result);
            TouchedPrimaryKeys = new List<TPrimaryKey>();
        }
       
        /// <summary>
        /// Stores a successful write result and its affected key list, clearing the previous error.
        /// </summary>
        /// <param name="result">The successful write result.</param>
        /// <param name="touchedPrimaryKeys">The primary keys affected by this operation.</param>
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
