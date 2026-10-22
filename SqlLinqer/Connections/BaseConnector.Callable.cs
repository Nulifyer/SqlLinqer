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
        internal SqlCallableResponse<T> ExecuteCallable<T>(PreparedCallable prepared, TabularRowType row, DbTransaction transaction)
            => CreateCallableResponse(ExecuteOnConnection(prepared.Query, transaction, true,
                command => ReadCallableResults<T>(command, prepared, row)));

        internal async Task<SqlCallableResponse<T>> ExecuteCallableAsync<T>(PreparedCallable prepared, TabularRowType row,
            DbTransaction transaction, CancellationToken cancellationToken)
            => CreateCallableResponse(await ExecuteOnConnectionAsync(prepared.Query, transaction, true,
                command => ReadCallableResultsAsync<T>(command, prepared, row, cancellationToken), cancellationToken).ConfigureAwait(false));

        private static CallableResult<T> ReadCallableResults<T>(DbCommand command, PreparedCallable plan, TabularRowType row)
        {
            var result = new CallableResult<T> { Rows = new List<T>() };
            int sets = 0;
            using (var reader = command.ExecuteReader())
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

        private static async Task<CallableResult<T>> ReadCallableResultsAsync<T>(DbCommand command, PreparedCallable plan, TabularRowType row, CancellationToken cancellationToken)
        {
            var result = new CallableResult<T> { Rows = new List<T>() };
            int sets = 0;
            using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
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
        private struct CallableResult<T>
        {
            internal List<T> Rows;
            internal long? TotalResults;
            internal int? ReturnCode;
            internal int? RowsAffected;
        }
    }
}
