using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using SqlLinqer.Components.Render;

namespace SqlLinqer.Diagnostics
{
    /// <summary>A parameter's type information and optional bounded display value. It never retains the original value object.</summary>
    public sealed class DiagnosticParameter
    {
        /// <summary>The provider binding placeholder.</summary>
        public string Name { get; internal set; }
        /// <summary>The source CLR type, or null for a null reference.</summary>
        public string ClrType { get; internal set; }
        /// <summary>The explicitly assigned DbType, or null when the provider infers it.</summary>
        public string DbType { get; internal set; }
        /// <summary>An invariant diagnostic representation, or null when value logging is disabled. This is not a SQL literal.</summary>
        public string DisplayValue { get; internal set; }
        /// <summary>True for SQL NULL, false for another value, or null when values are redacted.</summary>
        public bool? IsNull { get; internal set; }
        /// <summary>True when the displayed value was shortened.</summary>
        public bool Truncated { get; internal set; }
    }

    /// <summary>A bounded, typed parameter record suitable for structured logging.</summary>
    public sealed class DiagnosticParameterSet
    {
        /// <summary>The captured parameter metadata and any enabled display values.</summary>
        public IReadOnlyList<DiagnosticParameter> Items { get; }
        /// <summary>The number of additional bindings excluded by MaxParameterCount.</summary>
        public int OmittedCount { get; }
        private DiagnosticParameterSet(DiagnosticParameter[] items, int omitted) { Items = items; OmittedCount = omitted; }
        internal static DiagnosticParameterSet Capture(RenderedQuery query, SqlLinqerDiagnosticsOptions options)
        {
            var items = new List<DiagnosticParameter>();
            int count = 0;
            foreach (var parameter in query.Parameters.GetAll())
            {
                if (count++ >= options.MaxParameterCount) continue;
                var value = parameter.Value;
                bool isNull = value == null || value == DBNull.Value || value is SqlNull;
                string text = null;
                bool truncated = false;
                if (options.IncludeParameterValues) text = Format(value, options.MaxParameterValueLength, out truncated);
                items.Add(new DiagnosticParameter
                {
                    Name = parameter.Placehodler,
                    ClrType = value?.GetType().FullName,
                    DbType = parameter.DbType?.ToString(),
                    DisplayValue = text,
                    IsNull = options.IncludeParameterValues ? isNull : (bool?)null,
                    Truncated = truncated
                });
            }
            return new DiagnosticParameterSet(items.ToArray(), Math.Max(0, count - items.Count));
        }
        private static string Format(object value, int maximum, out bool truncated)
        {
            truncated = false;
            if (value == null || value == DBNull.Value || value is SqlNull) return "NULL";
            if (value is string text)
            {
                int length = Math.Min(text.Length, maximum);
                while (true)
                {
                    if (length > 0 && length < text.Length && char.IsHighSurrogate(text[length - 1])) length--;
                    string quoted = JsonSerializer.Serialize(text.Substring(0, length));
                    if (quoted.Length <= maximum - (length < text.Length ? 1 : 0))
                    { truncated = length < text.Length; return quoted + (truncated ? "…" : ""); }
                    length /= 2;
                }
            }
            string result;
            switch (value)
            {
                case byte[] bytes:
                    int take = Math.Min(bytes.Length, (maximum - 3) / 2);
                    result = "0x" + BitConverter.ToString(bytes, 0, take).Replace("-", "");
                    truncated = take < bytes.Length;
                    return result + (truncated ? "…" : "");
                case DateTime date: result = date.ToString("O", CultureInfo.InvariantCulture); break;
                case DateTimeOffset date: result = date.ToString("O", CultureInfo.InvariantCulture); break;
                case TimeSpan duration: result = duration.ToString("c", CultureInfo.InvariantCulture); break;
                case Guid guid: result = guid.ToString("D"); break;
                case bool boolean: result = boolean ? "true" : "false"; break;
                case char character: result = JsonSerializer.Serialize(character.ToString()); break;
                case Enum enumeration: result = enumeration.ToString(); break;
                case double number: result = number.ToString("R", CultureInfo.InvariantCulture); break;
                case float number: result = number.ToString("R", CultureInfo.InvariantCulture); break;
                case byte _:
                case sbyte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case decimal _:
                    result = Convert.ToString(value, CultureInfo.InvariantCulture); break;
                default:
                    string type = value.GetType().FullName;
                    result = (type == "System.DateOnly" || type == "System.TimeOnly") && value is IFormattable formatted
                        ? formatted.ToString("O", CultureInfo.InvariantCulture) : "<unsupported>";
                    break;
            }
            if (result.Length <= maximum) return result;
            truncated = true;
            return result.Substring(0, maximum - 1) + "…";
        }
        /// <summary>Returns JSON containing only the captured metadata and enabled bounded display values.</summary>
        public override string ToString() => JsonSerializer.Serialize(this);
    }
}
