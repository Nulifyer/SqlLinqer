using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SqlLinqer.Compilation;
using SqlLinqer.Components.Modeling;

namespace SqlLinqer.Connections
{
    public abstract partial class BaseConnector
    {
        internal SqlCallableResponse<T> ExecuteCallable<T>(PreparedCallable prepared, TabularRowType row, DbTransaction transaction, TimeSpan? commandTimeout = null)
            => CreateCallableResponse(ExecuteOnConnection(prepared.Query, transaction, true,
                (command, observation) => ReadCallableResults<T>(command, observation, prepared, row), commandTimeout: commandTimeout));

        internal async Task<SqlCallableResponse<T>> ExecuteCallableAsync<T>(PreparedCallable prepared, TabularRowType row,
            DbTransaction transaction, CancellationToken cancellationToken, TimeSpan? commandTimeout = null)
            => CreateCallableResponse(await ExecuteOnConnectionAsync(prepared.Query, transaction, true,
                (command, observation) => ReadCallableResultsAsync<T>(command, observation, prepared, row, cancellationToken), cancellationToken, commandTimeout: commandTimeout).ConfigureAwait(false));

        private static CallableResult<T> ReadCallableResults<T>(DbCommand command, ExecutionObservation observation, PreparedCallable plan, TabularRowType row)
        {
            var result = new CallableResult<T> { Rows = new List<T>() };
            int sets = 0;
            using (var reader = ReadCommand(command, observation))
            {
                do
                {
                    if (reader.FieldCount == 0) continue;
                    if (++sets != 1) throw new InvalidOperationException("The procedure returned more than one tabular result set.");
                    var ordinals = row.GetOrdinals(reader);
                    var values = row.CreateBuffer();
                    while (reader.Read()) result.Rows.Add((T)row.Read(reader, ordinals, mappedValues: values));
                } while (reader.NextResult());
                if (reader.RecordsAffected >= 0) result.RowsAffected = reader.RecordsAffected;
            }
            CompleteCallableResults(command, plan, ref result, sets);
            return result;
        }

        private static void CompleteCallableResults<T>(DbCommand command, PreparedCallable plan, ref CallableResult<T> result, int sets)
        {
            if (sets != 1) throw new InvalidOperationException("The procedure did not return its tabular result set.");
            // SQL Server populates output parameters only after the reader closes.
            result.ReturnCode = (int?)DbValueConversion.ConvertTo(command.Parameters[plan.ReturnCode].Value, typeof(int?));
            result.TotalResults = (long?)DbValueConversion.ConvertTo(command.Parameters[plan.TotalResults].Value, typeof(long?));
            if (result.TotalResults < 0) throw new InvalidOperationException("The procedure total result count cannot be negative.");
        }

        private static SqlCallableResponse<T> CreateCallableResponse<T>(SqlResponse<CallableResult<T>> response)
        {
            var result = new SqlCallableResponse<T>();
            if (response.State == ResponseState.Valid)
                result.SetResult(response.Result.Rows, response.Result.TotalResults, response.Result.ReturnCode, response.Result.RowsAffected);
            else result.SetError(response.Error);
            return result;
        }

        private static async Task<CallableResult<T>> ReadCallableResultsAsync<T>(DbCommand command, ExecutionObservation observation, PreparedCallable plan, TabularRowType row, CancellationToken cancellationToken)
        {
            var result = new CallableResult<T> { Rows = new List<T>() };
            int sets = 0;
            using (var reader = await ReadCommandAsync(command, observation, cancellationToken).ConfigureAwait(false))
            {
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reader.FieldCount == 0) continue;
                    if (++sets != 1) throw new InvalidOperationException("The procedure returned more than one tabular result set.");
                    var ordinals = row.GetOrdinals(reader);
                    var values = row.CreateBuffer();
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        result.Rows.Add((T)row.Read(reader, ordinals, mappedValues: values));
                    }
                } while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));
                if (reader.RecordsAffected >= 0) result.RowsAffected = reader.RecordsAffected;
            }
            CompleteCallableResults(command, plan, ref result, sets);
            return result;
        }
        private struct CallableResult<T> : IRowCountResult
        {
            internal List<T> Rows;
            public long RowCount => Rows.Count;
            internal long? TotalResults;
            internal int? ReturnCode;
            internal int? RowsAffected;
        }
    }
}
