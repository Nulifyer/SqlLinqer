using System;
using System.Collections.Generic;

namespace SqlLinqer
{
    /// <summary>
    /// Contains typed procedure results and standard execution metadata. Failure clears the result and every metadata value.
    /// A missing count differs from a known zero, and a nonzero return code does not automatically indicate failure.
    /// </summary>
    /// <typeparam name="T">The output row model.</typeparam>
    public class SqlStoredProcedureResponse<T> : SqlResponse<List<T>>
    {
        /// <summary>The unpaged result count supplied by the configured output parameter, or null when unavailable.</summary>
        public long? TotalResults { get; private set; }
        /// <summary>The procedure RETURN code, or null when unavailable. Check State for execution failure.</summary>
        public int? ReturnCode { get; private set; }
        /// <summary>The provider's affected-row count, or null when unavailable, including when NOCOUNT suppresses it. This is not the selected-row total.</summary>
        public int? RowsAffected { get; private set; }

        /// <summary>Creates an unset error response with no result or metadata.</summary>
        public SqlStoredProcedureResponse() { }

        /// <inheritdoc/>
        public override void SetResult(List<T> result)
        {
            base.SetResult(result);
            TotalResults = null;
            ReturnCode = null;
            RowsAffected = null;
        }

        /// <summary>Stores a successful procedure result and its metadata, clearing any previous error.</summary>
        /// <param name="result">The returned rows.</param>
        /// <param name="totalResults">The unpaged result count, or null when unavailable. Counts must not be negative.</param>
        /// <param name="returnCode">The procedure RETURN code, which does not determine response State.</param>
        /// <param name="rowsAffected">The provider's affected-row count, or null when unavailable. Counts must not be negative.</param>
        public void SetResult(List<T> result, long? totalResults, int? returnCode, int? rowsAffected)
        {
            if (totalResults < 0) throw new ArgumentOutOfRangeException(nameof(totalResults));
            if (rowsAffected < 0) throw new ArgumentOutOfRangeException(nameof(rowsAffected));
            SetResult(result);
            TotalResults = totalResults;
            ReturnCode = returnCode;
            RowsAffected = rowsAffected;
        }

        /// <inheritdoc/>
        public override void SetError(Exception error)
        {
            base.SetError(error);
            TotalResults = null;
            ReturnCode = null;
            RowsAffected = null;
        }

        /// <inheritdoc/>
        protected override Dictionary<string, object> ToDictionaryCreate()
        {
            var values = base.ToDictionaryCreate();
            values.Add(nameof(TotalResults), TotalResults);
            values.Add(nameof(ReturnCode), ReturnCode);
            values.Add(nameof(RowsAffected), RowsAffected);
            return values;
        }
    }
}
