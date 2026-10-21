using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace SqlLinqer.Components.Json
{
    // The result transport is independent of connection and transaction ownership.
    // Both execution styles use the same library-owned SQL model contract and decoding.
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

        internal async IAsyncEnumerable<T> ReadAsyncStream(DbDataReader reader,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (rowByRow)
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Each value is one root object. Do not create an intermediate DOM
                    // or retain objects after yielding them to the caller.
                    yield return JsonSerializer.Deserialize<T>(reader.GetString(0), options);
                }
            }
            else
            {
                // Some SQL Server executions return no transport rows for an empty
                // projection. Match buffered loading instead of parsing empty input.
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) yield break;
                using (var input = new JsonFragmentStream(reader))
                {
                    await foreach (var row in JsonSerializer.DeserializeAsyncEnumerable<T>(input, options, cancellationToken)
                        .ConfigureAwait(false))
                        yield return row;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
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
