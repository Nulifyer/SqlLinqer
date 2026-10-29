using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Compilation;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Connections
{
    public abstract partial class BaseConnector
    {
        internal SqlMultiCallableResponse<T> ExecuteMultiCallable<T>(PreparedCallable prepared, MultiResultShape<T> shape,
            DbTransaction transaction, TimeSpan? commandTimeout)
            => CreateMultiCallableResponse(ExecuteOnConnection(prepared.Query, transaction, true,
                (command, observation) => ReadMultiCallable(command, observation, prepared, shape), commandTimeout: commandTimeout));

        internal async Task<SqlMultiCallableResponse<T>> ExecuteMultiCallableAsync<T>(PreparedCallable prepared, MultiResultShape<T> shape,
            DbTransaction transaction, CancellationToken cancellationToken, TimeSpan? commandTimeout)
            => CreateMultiCallableResponse(await ExecuteOnConnectionAsync(prepared.Query, transaction, true,
                (command, observation) => ReadMultiCallableAsync(command, observation, prepared, shape, cancellationToken),
                cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false));

        private static MultiCallableResult<T> ReadMultiCallable<T>(DbCommand command, ExecutionObservation observation,
            PreparedCallable plan, MultiResultShape<T> shape)
        {
            var tables = new object[shape.Tables.Length];
            int sets = 0;
            var result = new MultiCallableResult<T>();
            using (var reader = ReadCommand(command, observation))
            {
                do
                {
                    if (reader.FieldCount == 0) continue;
                    var table = NextTable(shape, sets);
                    var ordinals = table.Row.GetOrdinals(reader);
                    var values = table.Row.CreateBuffer();
                    var rows = table.CreateBuffer();
                    while (reader.Read()) rows.Add(table.Row.Read(reader, ordinals, mappedValues: values));
                    result.RowCount += rows.Count;
                    tables[sets++] = table.Materialize(rows);
                } while (reader.NextResult());
                if (reader.RecordsAffected >= 0) result.RowsAffected = reader.RecordsAffected;
            }
            CompleteMultiCallable(command, plan, shape, tables, sets, ref result);
            return result;
        }

        private static async Task<MultiCallableResult<T>> ReadMultiCallableAsync<T>(DbCommand command, ExecutionObservation observation,
            PreparedCallable plan, MultiResultShape<T> shape, CancellationToken cancellationToken)
        {
            var tables = new object[shape.Tables.Length];
            int sets = 0;
            var result = new MultiCallableResult<T>();
            using (var reader = await ReadCommandAsync(command, observation, cancellationToken).ConfigureAwait(false))
            {
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reader.FieldCount == 0) continue;
                    var table = NextTable(shape, sets);
                    var ordinals = table.Row.GetOrdinals(reader);
                    var values = table.Row.CreateBuffer();
                    var rows = table.CreateBuffer();
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        rows.Add(table.Row.Read(reader, ordinals, mappedValues: values));
                    }
                    result.RowCount += rows.Count;
                    tables[sets++] = table.Materialize(rows);
                    cancellationToken.ThrowIfCancellationRequested();
                } while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));
                if (reader.RecordsAffected >= 0) result.RowsAffected = reader.RecordsAffected;
            }
            cancellationToken.ThrowIfCancellationRequested();
            CompleteMultiCallable(command, plan, shape, tables, sets, ref result);
            return result;
        }

        private static ResultTable NextTable<T>(MultiResultShape<T> shape, int set)
        {
            if (set >= shape.Tables.Length) throw new InvalidOperationException($"The procedure returned more than {shape.Tables.Length} tabular result sets.");
            return shape.Tables[set];
        }

        private static void CompleteMultiCallable<T>(DbCommand command, PreparedCallable plan, MultiResultShape<T> shape,
            object[] tables, int sets, ref MultiCallableResult<T> result)
        {
            if (sets != tables.Length) throw new InvalidOperationException($"The procedure returned {sets} tabular result sets; expected {tables.Length}.");
            // Return parameters become available only after the reader closes.
            result.ReturnCode = (int?)DbValueConversion.ConvertTo(command.Parameters[plan.ReturnCode].Value, typeof(int?));
            result.Tables = shape.Create(tables);
        }

        private static SqlMultiCallableResponse<T> CreateMultiCallableResponse<T>(SqlResponse<MultiCallableResult<T>> response)
        {
            var result = new SqlMultiCallableResponse<T>();
            if (response.State == ResponseState.Valid)
                result.SetResult(response.Result.Tables, response.Result.ReturnCode, response.Result.RowsAffected);
            else result.SetError(response.Error);
            return result;
        }

        private struct MultiCallableResult<T> : IRowCountResult
        {
            internal T Tables;
            public long RowCount { get; set; }
            internal int? ReturnCode;
            internal int? RowsAffected;
        }
    }
}
