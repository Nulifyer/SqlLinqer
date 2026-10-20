using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlLinqer.Components.Json
{
    // MySQL JSON represents boolean database columns as numeric values.
    internal sealed class DatabaseBooleanConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True: return true;
                case JsonTokenType.False: return false;
                case JsonTokenType.Number: return reader.GetDouble() != 0;
                case JsonTokenType.String:
                    if (bool.TryParse(reader.GetString(), out bool value))
                        return value;
                    break;
            }
            throw new JsonException("Expected a boolean or a numeric boolean value.");
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
            => writer.WriteBooleanValue(value);
    }

    // MySQL date/time JSON can contain a space between the date and time.
    internal sealed class DatabaseDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                if (reader.TryGetDateTime(out var value)
                    || DateTime.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
                    return value;
            }
            throw new JsonException("Expected a database date/time string.");
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }

    internal sealed class DatabaseDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                if (reader.TryGetDateTimeOffset(out var value)
                    || DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
                    return value;
            }
            throw new JsonException("Expected a database date/time string with an offset.");
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }
}
