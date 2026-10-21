using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Json;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Components.Render;

namespace SqlLinqer.Connections
{
    public abstract partial class BaseConnector
    {
        /// <summary>
        /// Executes a tabular command and buffers its results in a DataTable that the caller must dispose.
        /// DuckDB BLOB streams are copied to byte arrays before the reader closes.
        /// </summary>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <returns>The buffered table and execution state. Dispose a successful Result table after use.</returns>
        public SqlResponse<DataTable> ExecuteReaderDataTable(RenderedQuery query, DbTransaction existingTransaction = null)
        {
            return ExecuteOnConnection(query, existingTransaction, query.Transactional, ReadDataTable);
        }

        private DataTable ReadDataTable(DbCommand command)
        {
            var table = new DataTable();
            try
            {
                if (DbFlavor == DbFlavor.DuckDb)
                {
                    using (var reader = command.ExecuteReader())
                        LoadDuckDbTable(table, reader);
                    return table;
                }

                using (var dataSet = new DataSet { EnforceConstraints = false })
                using (var reader = command.ExecuteReader())
                {
                    dataSet.Tables.Add(table);
                    table.Load(reader, LoadOption.OverwriteChanges);
                    dataSet.Tables.Remove(table);
                }
                return table;
            }
            catch
            {
                table.Dispose();
                throw;
            }
        }

        private static void LoadDuckDbTable(DataTable table, DbDataReader reader)
        {
            // DuckDB returns BLOB values as streams over native result memory.
            // DataTable.Load closes the reader before callers can copy those streams.
            while (reader.FieldCount == 0)
                if (!reader.NextResult()) return;
            InitializeResultColumns(table, reader);

            while (reader.Read())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] is Stream stream)
                    {
                        using (stream)
                        using (var buffer = new MemoryStream())
                        {
                            stream.CopyTo(buffer);
                            values[i] = buffer.ToArray();
                        }
                    }
                }
                table.Rows.Add(values);
            }
        }

        private static void InitializeResultColumns(DataTable table, DbDataReader reader)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string originalName = reader.GetName(i);
                if (string.IsNullOrEmpty(originalName))
                    originalName = $"Column{i + 1}";
                string name = originalName;
                int suffix = 1;
                while (table.Columns.Contains(name))
                    name = originalName + suffix++;
                var type = reader.GetFieldType(i);
                table.Columns.Add(name, typeof(Stream).IsAssignableFrom(type) ? typeof(byte[]) : type);
            }
        }

        /// <summary>
        /// Asynchronously executes a tabular command and buffers its results in a DataTable that the caller must dispose. Requested cancellation throws.
        /// DuckDB BLOB streams are copied to byte arrays before the reader closes.
        /// </summary>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, and result loading.</param>
        /// <returns>The buffered table and execution state. Dispose a successful Result table after use.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public Task<SqlResponse<DataTable>> ExecuteReaderDataTableAsync(RenderedQuery query,
            DbTransaction existingTransaction = null, CancellationToken cancellationToken = default)
        {
            return ExecuteOnConnectionAsync(query, existingTransaction, query.Transactional,
                command => ReadDataTableAsync(command, cancellationToken), cancellationToken);
        }

        private async Task<DataTable> ReadDataTableAsync(DbCommand command, CancellationToken cancellationToken)
        {
            var table = new DataTable();
            try
            {
                using (var dataSet = new DataSet { EnforceConstraints = false })
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    dataSet.Tables.Add(table);
                    while (reader.FieldCount == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                        {
                            dataSet.Tables.Remove(table);
                            return table;
                        }
                    }
                    if (DbFlavor == DbFlavor.DuckDb) InitializeResultColumns(table, reader);
                    else using (var schema = new SchemaOnlyReader(reader)) table.Load(schema, LoadOption.OverwriteChanges);
                    table.BeginLoadData();
                    try
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var values = new object[reader.FieldCount];
                            reader.GetValues(values);
                            for (int i = 0; i < values.Length; i++)
                                if (values[i] is Stream stream)
                                    using (stream)
                                    using (var buffer = new MemoryStream())
                                    {
                                        await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                                        values[i] = buffer.ToArray();
                                    }
                            if (DbFlavor == DbFlavor.DuckDb) table.Rows.Add(values);
                            else table.LoadDataRow(values, LoadOption.OverwriteChanges);
                        }
                    }
                    finally { table.EndLoadData(); }
                    dataSet.Tables.Remove(table);
                }
                cancellationToken.ThrowIfCancellationRequested();
                return table;
            }
            catch { table.Dispose(); throw; }
        }

        /// <summary>
        /// Executes a tabular command and maps its reader into a buffered list.
        /// Matches columns to flat model members by SqlColumn or CLR name, ignoring case and honoring SqlIgnore.
        /// Each mapped member requires one column; extra columns are ignored. Relationships are not loaded.
        /// Provider values retain their types and DateTime kinds during mapping.
        /// </summary>
        /// <typeparam name="T">A concrete mapped row type, or a scalar type for a single-column result.</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <returns>The mapped rows and execution state. Execution or mapping failures produce an error response without partial rows.</returns>
        public SqlResponse<List<T>> ExecuteReaderList<T>(RenderedQuery query, DbTransaction existingTransaction = null)
        {
            return ExecuteOnConnection(query, existingTransaction, query.Transactional, command =>
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.FieldCount == 0)
                        if (!reader.NextResult()) return new List<T>();
                    var rowType = new TabularRowType(typeof(T));
                    var ordinals = rowType.GetOrdinals(reader);
                    var rows = new List<T>();
                    while (reader.Read())
                    {
                        var values = new object[reader.FieldCount];
                        reader.GetValues(values);
                        for (int i = 0; i < values.Length; i++)
                        {
                            values[i] = DbValueConversion.NormalizeProviderValue(values[i], reader, i, DbFlavor);
                            if (values[i] is Stream stream)
                                using (stream)
                                using (var buffer = new MemoryStream())
                                { stream.CopyTo(buffer); values[i] = buffer.ToArray(); }
                        }
                        rows.Add((T)rowType.Read(reader, ordinals, values));
                    }
                    return rows;
                }
            });
        }

        /// <summary>
        /// Asynchronously executes a tabular command and maps its reader into a buffered list.
        /// Uses the same flat model mappings as ExecuteReaderList. Requested cancellation throws during loading or mapping.
        /// </summary>
        /// <typeparam name="T">A concrete mapped row type, or a scalar type for a single-column result.</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, row loading, and model mapping.</param>
        /// <returns>The mapped rows and execution state. Execution or mapping failures produce an error response without partial rows.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public Task<SqlResponse<List<T>>> ExecuteReaderListAsync<T>(RenderedQuery query,
            DbTransaction existingTransaction = null, CancellationToken cancellationToken = default)
        {
            return ExecuteOnConnectionAsync(query, existingTransaction, query.Transactional, async command =>
            {
                using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (reader.FieldCount == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!await reader.NextResultAsync(cancellationToken).ConfigureAwait(false)) return new List<T>();
                    }
                    var rowType = new TabularRowType(typeof(T));
                    var ordinals = rowType.GetOrdinals(reader);
                    var rows = new List<T>();
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var values = new object[reader.FieldCount];
                        reader.GetValues(values);
                        for (int i = 0; i < values.Length; i++)
                        {
                            values[i] = DbValueConversion.NormalizeProviderValue(values[i], reader, i, DbFlavor);
                            if (values[i] is Stream stream)
                                using (stream)
                                using (var buffer = new MemoryStream())
                                {
                                    await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                                    values[i] = buffer.ToArray();
                                }
                        }
                        rows.Add((T)rowType.Read(reader, ordinals, values));
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    return rows;
                }
            }, cancellationToken);
        }
    }
}
