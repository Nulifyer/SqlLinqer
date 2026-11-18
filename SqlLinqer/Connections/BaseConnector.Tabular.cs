using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Components.Native;
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
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The buffered table and execution state. Dispose a successful Result table after use.</returns>
        public SqlResponse<DataTable> ExecuteReaderDataTable(RenderedQuery query, DbTransaction existingTransaction = null, TimeSpan? commandTimeout = null)
            => ExecuteReaderDataTable(query, existingTransaction, null, query.Transactional, commandTimeout);

        internal SqlResponse<DataTable> ExecuteReaderDataTable(RenderedQuery query, DbTransaction existingTransaction, DbConnection existingConnection,
            bool transactional, TimeSpan? commandTimeout)
        {
            return ExecuteOnConnection(query, existingTransaction, transactional, ReadDataTable,
                commandTimeout: commandTimeout, existingConnection: existingConnection);
        }

        private DataTable ReadDataTable(DbCommand command, ExecutionObservation observation)
        {
            var table = new DataTable();
            try
            {
                if (DbFlavor == DbFlavor.DuckDb)
                {
                    using (var reader = ReadCommand(command, observation))
                        LoadDuckDbTable(table, reader);
                    return table;
                }

                using (var dataSet = new DataSet { EnforceConstraints = false })
                using (var reader = ReadCommand(command, observation))
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
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The buffered table and execution state. Dispose a successful Result table after use.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public Task<SqlResponse<DataTable>> ExecuteReaderDataTableAsync(RenderedQuery query,
            DbTransaction existingTransaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteReaderDataTableAsync(query, existingTransaction, null, query.Transactional, cancellationToken, commandTimeout);

        internal Task<SqlResponse<DataTable>> ExecuteReaderDataTableAsync(RenderedQuery query, DbTransaction existingTransaction, DbConnection existingConnection,
            bool transactional, CancellationToken cancellationToken, TimeSpan? commandTimeout)
        {
            return ExecuteOnConnectionAsync(query, existingTransaction, transactional,
                (command, observation) => ReadDataTableAsync(command, observation, cancellationToken), cancellationToken,
                commandTimeout: commandTimeout, existingConnection: existingConnection);
        }

        private async Task<DataTable> ReadDataTableAsync(DbCommand command, ExecutionObservation observation, CancellationToken cancellationToken)
        {
            var table = new DataTable();
            try
            {
                using (var dataSet = new DataSet { EnforceConstraints = false })
                using (var reader = await ReadCommandAsync(command, observation, cancellationToken).ConfigureAwait(false))
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
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The mapped rows and execution state. Execution or mapping failures produce an error response without partial rows.</returns>
        public SqlResponse<List<T>> ExecuteReaderList<T>(RenderedQuery query, DbTransaction existingTransaction = null, TimeSpan? commandTimeout = null)
            => ExecuteReaderList<T>(query, existingTransaction, null, query.Transactional, commandTimeout);

        internal SqlResponse<List<T>> ExecuteReaderList<T>(RenderedQuery query, DbTransaction existingTransaction, DbConnection existingConnection,
            bool transactional, TimeSpan? commandTimeout)
        {
            return ExecuteOnConnection(query, existingTransaction, transactional, (command, observation) =>
            {
                using (var reader = ReadCommand(command, observation))
                {
                    while (reader.FieldCount == 0)
                        if (!reader.NextResult()) return new List<T>();
                    var rowType = TabularRowType.GetCached(typeof(T));
                    var ordinals = rowType.GetOrdinals(reader);
                    var rows = new List<T>();
                    var values = new object[reader.FieldCount];
                    var mapped = rowType.CreateBuffer();
                    while (reader.Read())
                    {
                        reader.GetValues(values);
                        for (int i = 0; i < values.Length; i++)
                        {
                            values[i] = DbValueConversion.NormalizeProviderValue(values[i], reader, i, DbFlavor);
                            if (values[i] is Stream stream)
                                using (stream)
                                using (var buffer = new MemoryStream())
                                { stream.CopyTo(buffer); values[i] = buffer.ToArray(); }
                        }
                        rows.Add((T)rowType.Read(reader, ordinals, values, mapped));
                    }
                    return rows;
                }
            }, commandTimeout: commandTimeout, existingConnection: existingConnection);
        }

        /// <summary>
        /// Asynchronously executes a tabular command and maps its reader into a buffered list.
        /// Uses the same flat model mappings as ExecuteReaderList. Requested cancellation throws during loading or mapping.
        /// </summary>
        /// <typeparam name="T">A concrete mapped row type, or a scalar type for a single-column result.</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, row loading, and model mapping.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The mapped rows and execution state. Execution or mapping failures produce an error response without partial rows.</returns>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public Task<SqlResponse<List<T>>> ExecuteReaderListAsync<T>(RenderedQuery query,
            DbTransaction existingTransaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteReaderListAsync<T>(query, existingTransaction, null, query.Transactional, cancellationToken, commandTimeout);

        internal Task<SqlResponse<List<T>>> ExecuteReaderListAsync<T>(RenderedQuery query, DbTransaction existingTransaction, DbConnection existingConnection,
            bool transactional, CancellationToken cancellationToken, TimeSpan? commandTimeout)
        {
            return ExecuteOnConnectionAsync(query, existingTransaction, transactional, async (command, observation) =>
            {
                using (var reader = await ReadCommandAsync(command, observation, cancellationToken).ConfigureAwait(false))
                {
                    while (reader.FieldCount == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!await reader.NextResultAsync(cancellationToken).ConfigureAwait(false)) return new List<T>();
                    }
                    var rowType = TabularRowType.GetCached(typeof(T));
                    var ordinals = rowType.GetOrdinals(reader);
                    var rows = new List<T>();
                    var values = new object[reader.FieldCount];
                    var mapped = rowType.CreateBuffer();
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
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
                        rows.Add((T)rowType.Read(reader, ordinals, values, mapped));
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    return rows;
                }
            }, cancellationToken, commandTimeout: commandTimeout, existingConnection: existingConnection);
        }

        // Reads the current row and replaces provider-specific values, as the list readers do.
        private void LoadRowValues(DbDataReader reader, object[] values)
        {
            reader.GetValues(values);
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = DbValueConversion.NormalizeProviderValue(values[i], reader, i, DbFlavor);
                if (values[i] is Stream stream)
                    using (stream)
                    using (var buffer = new MemoryStream())
                    { stream.CopyTo(buffer); values[i] = buffer.ToArray(); }
            }
        }

        /// <summary>
        /// Executes a command that returns several result sets and buffers each one into its own collection.
        /// Each result set maps as ExecuteReaderList maps its rows. The number of result sets must equal the number of tuple elements.
        /// </summary>
        /// <typeparam name="TResults">A value tuple of collections in result-set order, such as (List&lt;Order&gt;, List&lt;OrderLine&gt;).</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The mapped collections and execution state. Execution or mapping failures produce an error response without partial results.</returns>
        /// <exception cref="InvalidOperationException">TResults is not a value tuple of at least two supported collections.</exception>
        public SqlResponse<TResults> ExecuteReaderLists<TResults>(RenderedQuery query, DbTransaction existingTransaction = null, TimeSpan? commandTimeout = null)
            => ExecuteReaderLists<TResults>(query, existingTransaction, null, query.Transactional, commandTimeout);

        internal SqlResponse<TResults> ExecuteReaderLists<TResults>(RenderedQuery query, DbTransaction existingTransaction, DbConnection existingConnection,
            bool transactional, TimeSpan? commandTimeout)
        {
            var shape = new MultiResultShape<TResults>();
            return ExecuteOnConnection(query, existingTransaction, transactional, (command, observation) =>
            {
                var tables = new object[shape.Tables.Length];
                int sets = 0;
                using (var reader = ReadCommand(command, observation))
                {
                    do
                    {
                        if (reader.FieldCount == 0) continue;
                        var table = NextResultSet(shape, sets);
                        var ordinals = table.Row.GetOrdinals(reader);
                        var values = new object[reader.FieldCount];
                        var mapped = table.Row.CreateBuffer();
                        var rows = table.CreateBuffer();
                        while (reader.Read())
                        {
                            LoadRowValues(reader, values);
                            rows.Add(table.Row.Read(reader, ordinals, values, mapped));
                        }
                        tables[sets++] = table.Materialize(rows);
                    } while (reader.NextResult());
                }
                return CompleteResultSets(shape, tables, sets);
            }, commandTimeout: commandTimeout, existingConnection: existingConnection);
        }

        /// <summary>
        /// Asynchronously executes a command that returns several result sets and buffers each one into its own collection.
        /// Uses the same mapping as ExecuteReaderLists. Requested cancellation throws.
        /// </summary>
        /// <typeparam name="TResults">A value tuple of collections in result-set order, such as (List&lt;Order&gt;, List&lt;OrderLine&gt;).</typeparam>
        /// <param name="query">The parameterized SQL text and bindings to execute.</param>
        /// <param name="existingTransaction">An optional caller-owned transaction with an active connection. The library does not complete or dispose it.</param>
        /// <param name="cancellationToken">Cancels provider connection opening, command execution, row loading, and model mapping.</param>
        /// <param name="commandTimeout">Overrides the connector command timeout for this execution. Null uses the connector default; zero disables the timeout.</param>
        /// <returns>The mapped collections and execution state. Execution or mapping failures produce an error response without partial results.</returns>
        /// <exception cref="InvalidOperationException">TResults is not a value tuple of at least two supported collections.</exception>
        /// <exception cref="System.OperationCanceledException">The supplied cancellation token was cancelled.</exception>
        public Task<SqlResponse<TResults>> ExecuteReaderListsAsync<TResults>(RenderedQuery query,
            DbTransaction existingTransaction = null, CancellationToken cancellationToken = default, TimeSpan? commandTimeout = null)
            => ExecuteReaderListsAsync<TResults>(query, existingTransaction, null, query.Transactional, cancellationToken, commandTimeout);

        internal Task<SqlResponse<TResults>> ExecuteReaderListsAsync<TResults>(RenderedQuery query, DbTransaction existingTransaction, DbConnection existingConnection,
            bool transactional, CancellationToken cancellationToken, TimeSpan? commandTimeout)
        {
            var shape = new MultiResultShape<TResults>();
            return ExecuteOnConnectionAsync(query, existingTransaction, transactional, async (command, observation) =>
            {
                var tables = new object[shape.Tables.Length];
                int sets = 0;
                using (var reader = await ReadCommandAsync(command, observation, cancellationToken).ConfigureAwait(false))
                {
                    do
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (reader.FieldCount == 0) continue;
                        var table = NextResultSet(shape, sets);
                        var ordinals = table.Row.GetOrdinals(reader);
                        var values = new object[reader.FieldCount];
                        var mapped = table.Row.CreateBuffer();
                        var rows = table.CreateBuffer();
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            LoadRowValues(reader, values);
                            rows.Add(table.Row.Read(reader, ordinals, values, mapped));
                        }
                        tables[sets++] = table.Materialize(rows);
                    } while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));
                }
                cancellationToken.ThrowIfCancellationRequested();
                return CompleteResultSets(shape, tables, sets);
            }, cancellationToken, commandTimeout: commandTimeout, existingConnection: existingConnection);
        }

        private static ResultTable NextResultSet<TResults>(MultiResultShape<TResults> shape, int set)
        {
            if (set >= shape.Tables.Length) throw new InvalidOperationException($"The command returned more than {shape.Tables.Length} result sets.");
            return shape.Tables[set];
        }

        private static TResults CompleteResultSets<TResults>(MultiResultShape<TResults> shape, object[] tables, int sets)
        {
            if (sets != tables.Length) throw new InvalidOperationException($"The command returned {sets} result sets; expected {tables.Length}.");
            return shape.Create(tables);
        }
    }
}
