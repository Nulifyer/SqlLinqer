using System;
using System.Globalization;

namespace SqlLinqer.Components.Render
{
    internal static class SqliteValue
    {
        internal static T ConvertTo<T>(object value)
        {
            if (value is T typed)
                return typed;
            Type type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (type == typeof(Guid))
                return (T)(object)Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture));
            if (type.IsEnum)
                return (T)Enum.ToObject(type, value);
            return (T)Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }
    }
}
