using System;
using System.Globalization;

namespace SqlLinqer.Connections
{
    internal static class DbValueConversion
    {
        internal static T ConvertTo<T>(object value)
        {
            if (value is T typed) return typed;
            Type target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (value == null || value == DBNull.Value)
            {
                if (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null) return default(T);
                throw new InvalidCastException($"Cannot convert SQL NULL to {typeof(T)}.");
            }
            if (target == typeof(Guid)) return (T)(object)Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture));
            if (target.IsEnum)
                return (T)(value is string text ? Enum.Parse(target, text) : Enum.ToObject(target, value));
            // DuckDB SUM of integral columns returns HUGEINT as BigInteger.
            if (value is System.Numerics.BigInteger integer)
            {
                if (target == typeof(double)) return (T)(object)(double)integer;
                if (target == typeof(float)) return (T)(object)(float)integer;
                if (target == typeof(string)) return (T)(object)integer.ToString(CultureInfo.InvariantCulture);
                value = (decimal)integer;
            }
            return (T)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }
    }
}
