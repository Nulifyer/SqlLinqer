using System;
using System.Globalization;
using System.Numerics;
using System.Data.Common;

namespace SqlLinqer.Connections
{
    // One value contract for scalar results, tabular rows, and callable outputs.
    internal static class DbValueConversion
    {
        private static readonly string[] ClockFormats =
        {
            "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF"
        };
        private static readonly string[] OffsetFormats =
        {
            "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
            "yyyy-MM-dd HH:mm:sszzz", "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz",
            "yyyy-MM-dd'T'HH:mm:sszz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzz",
            "yyyy-MM-dd HH:mm:sszz", "yyyy-MM-dd HH:mm:ss.FFFFFFFzz",
            "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
            "yyyy-MM-dd HH:mm:ss'Z'", "yyyy-MM-dd HH:mm:ss.FFFFFFF'Z'"
        };

        internal static bool IsScalarType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
                || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan) || type == typeof(byte[]) || type == typeof(object)
                || type.FullName == "System.DateOnly" || type.FullName == "System.TimeOnly";
        }

        internal static T ConvertTo<T>(object value) => (T)ConvertTo(value, typeof(T));

        internal static object NormalizeProviderValue(object value, DbDataReader reader, int ordinal, DbFlavor flavor)
        {
            // DuckDB.NET returns TIMESTAMPTZ as a UTC clock with Kind=Unspecified.
            // Only SQL type metadata makes it safe to restore the UTC kind.
            if (flavor == DbFlavor.DuckDb && value is DateTime date
                && string.Equals(reader.GetDataTypeName(ordinal), "TIMESTAMPTZ", StringComparison.OrdinalIgnoreCase))
                return DateTime.SpecifyKind(date, DateTimeKind.Utc);
            return value;
        }

        internal static object ConvertTo(object value, Type type)
        {
            Type target = Nullable.GetUnderlyingType(type) ?? type;
            if (value == null || value == DBNull.Value)
            {
                if (!type.IsValueType || Nullable.GetUnderlyingType(type) != null) return null;
                throw new InvalidCastException($"Cannot convert SQL NULL to {type}.");
            }
            if (target == typeof(object)) return value;
            if (IsNumeric(target)) RequireFinite(value);
            if (target.IsInstanceOfType(value)) return value;
            if (target == typeof(DateTime)) return ToDateTime(value);
            if (target == typeof(DateTimeOffset)) return ToDateTimeOffset(value);
            if (target == typeof(TimeSpan) && value is string time)
                return TimeSpan.ParseExact(time, "c", CultureInfo.InvariantCulture);
            // Newer providers can expose these runtime types even though this
            // library targets netstandard2.0. Formatting avoids a dependency on them.
            if (value is IFormattable formattable)
            {
                if (value.GetType().FullName == "System.DateOnly")
                {
                    string day = formattable.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (target == typeof(string)) return day;
                }
                if (value.GetType().FullName == "System.TimeOnly")
                {
                    string clock = formattable.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
                    if (target == typeof(TimeSpan)) return TimeSpan.ParseExact(clock, "c", CultureInfo.InvariantCulture);
                    if (target == typeof(string)) return clock;
                }
            }
            if (target == typeof(string))
            {
                if (value is DateTime date) return date.ToString("O", CultureInfo.InvariantCulture);
                if (value is DateTimeOffset offset) return offset.ToString("O", CultureInfo.InvariantCulture);
                if (value is TimeSpan span) return span.ToString("c", CultureInfo.InvariantCulture);
                if (value is BigInteger big) return big.ToString(CultureInfo.InvariantCulture);
            }
            if (target == typeof(Guid)) return Guid.Parse(System.Convert.ToString(value, CultureInfo.InvariantCulture));
            if (target.IsEnum)
                return value is string text ? Enum.Parse(target, text, true)
                    : Enum.ToObject(target, ConvertTo(value, Enum.GetUnderlyingType(target)));

            if (value is string number)
            {
                // NumberStyles.Float permits exponent notation but excludes group
                // separators, which can turn localized decimal text into a different value.
                if (target == typeof(decimal)) return decimal.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (target == typeof(double)) return Finite(double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture));
                if (target == typeof(float)) return Finite(float.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture));
            }

            if (IsInteger(target))
            {
                // Convert.ChangeType otherwise rounds fractional values to an integer.
                if ((value is decimal dec && decimal.Truncate(dec) != dec)
                    || (value is double dbl && Math.Truncate(dbl) != dbl)
                    || (value is float single && Math.Truncate(single) != single))
                    throw new InvalidCastException($"Cannot convert a fractional number to {target}.");
            }
            if (value is BigInteger integer)
            {
                if (target == typeof(sbyte)) return (sbyte)integer;
                if (target == typeof(byte)) return (byte)integer;
                if (target == typeof(short)) return (short)integer;
                if (target == typeof(ushort)) return (ushort)integer;
                if (target == typeof(int)) return (int)integer;
                if (target == typeof(uint)) return (uint)integer;
                if (target == typeof(long)) return (long)integer;
                if (target == typeof(ulong)) return (ulong)integer;
                if (target == typeof(double)) return Finite((double)integer);
                if (target == typeof(float)) return Finite((float)integer);
                value = (decimal)integer;
            }
            object result = System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            if (IsNumeric(target)) RequireFinite(result);
            return result;
        }

        private static DateTime ToDateTime(object value)
        {
            if (value is DateTimeOffset offset) return offset.UtcDateTime;
            if (value is IFormattable date && value.GetType().FullName == "System.DateOnly")
                return DateTime.ParseExact(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (value is string text)
            {
                if (DateTimeOffset.TryParseExact(text, OffsetFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var instant)) return instant.UtcDateTime;
                if (DateTime.TryParseExact(text, ClockFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var clock)) return DateTime.SpecifyKind(clock, DateTimeKind.Unspecified);
            }
            throw new FormatException("Expected an ISO or SQL date/time value.");
        }

        private static DateTimeOffset ToDateTimeOffset(object value)
        {
            if (value is DateTime date && date.Kind != DateTimeKind.Unspecified)
                return new DateTimeOffset(date);
            if (value is string text && DateTimeOffset.TryParseExact(text, OffsetFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var offset)) return offset;
            throw new FormatException("A DateTimeOffset requires an explicit offset, UTC suffix, or DateTime kind.");
        }

        private static bool IsInteger(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64: return true;
                default: return false;
            }
        }

        private static bool IsNumeric(Type type)
            => IsInteger(type) || type == typeof(decimal) || type == typeof(float) || type == typeof(double);

        private static void RequireFinite(object value)
        {
            if (value is double dbl) Finite(dbl);
            else if (value is float single) Finite(single);
        }

        private static double Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new OverflowException("Expected a finite database number.");
            return value;
        }

        private static float Finite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new OverflowException("Expected a finite database number.");
            return value;
        }
    }
}
