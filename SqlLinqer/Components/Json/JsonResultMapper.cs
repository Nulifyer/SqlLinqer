using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SqlLinqer.Components.Json
{
    // The result transport is independent of connection and transaction ownership.
    // Both execution styles use the same SQL/API contract selection and decoding.
    internal sealed class JsonResultMapper<T> where T : class
    {
        private readonly JsonSerializerOptions options;
        private readonly bool rowByRow;
        internal JsonResultMapper(JsonSerializerOptions options, bool rowByRow)
        { this.options = options; this.rowByRow = rowByRow; }

        internal List<T> Read(DbDataReader reader)
        {
            var rows = new List<T>();
            var document = rowByRow ? null : new StringBuilder();
            while (reader.Read()) Add(reader, rows, document);
            return Complete(rows, document);
        }

        internal async Task<List<T>> ReadAsync(DbDataReader reader, CancellationToken cancellationToken)
        {
            var rows = new List<T>();
            var document = rowByRow ? null : new StringBuilder();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Add(reader, rows, document);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Complete(rows, document);
        }

        private void Add(DbDataReader reader, List<T> rows, StringBuilder document)
        {
            string json = reader[0].ToString();
            if (rowByRow) rows.Add(JsonSerializer.Deserialize<T>(json, options));
            else document.Append(json);
        }

        private List<T> Complete(List<T> rows, StringBuilder document)
        {
            if (rowByRow) return rows;
            string json = document.ToString();
            return string.IsNullOrWhiteSpace(json) ? rows : JsonSerializer.Deserialize<List<T>>(json, options);
        }
    }
}
