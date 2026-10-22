using System;
using System.Collections.Generic;
using System.Globalization;
using SqlLinqer.Connections;

namespace SqlLinqer.Queries.Execution
{
    internal static class PrimaryKeyResults
    {
        internal static SqlPrimaryKeysResponse<int, TKey> Create<TKey>(SqlResponse<int> execution, List<object> primaryKeys, DbFlavor flavor)
        {
            var response = new SqlPrimaryKeysResponse<int, TKey>();
            switch (execution.State)
            {
                case ResponseState.Valid:
                    var keys = new List<TKey>(primaryKeys.Count);
                    foreach (var key in primaryKeys)
                        keys.Add(flavor == DbFlavor.Sqlite ? ConvertSqliteKey<TKey>(key)
                            : flavor == DbFlavor.DuckDb ? DbValueConversion.ConvertTo<TKey>(key) : (TKey)key);
                    response.SetResult(execution.Result, keys);
                    break;
                case ResponseState.Error:
                    response.SetError(execution.Error);
                    break;
                default:
                    throw new NotSupportedException($"The {nameof(ResponseState)} {execution.State} is not supported");
            }
            return response;
        }

        private static TKey ConvertSqliteKey<TKey>(object value)
        {
            if (value is TKey typed) return typed;
            var type = Nullable.GetUnderlyingType(typeof(TKey)) ?? typeof(TKey);
            if (type == typeof(Guid)) return (TKey)(object)Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture));
            if (type.IsEnum) return (TKey)Enum.ToObject(type, value);
            return (TKey)Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }
    }
}
