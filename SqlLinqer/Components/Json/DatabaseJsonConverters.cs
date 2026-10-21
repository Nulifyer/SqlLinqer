using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SqlLinqer.Connections;

namespace SqlLinqer.Components.Json
{
    // SQLite JSON cannot contain BLOB values. Typed column projections use hex.
    internal sealed class DatabaseHexBytesConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string hex = reader.GetString();
            if (hex.Length % 2 != 0)
                throw new JsonException("Expected an even-length SQLite hex string.");
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
            => writer.WriteStringValue(BitConverter.ToString(value).Replace("-", ""));
    }

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
                try { return DbValueConversion.ConvertTo<DateTime>(reader.GetString()); }
                catch (FormatException error) { throw new JsonException("Expected a database date/time string.", error); }
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
                try { return DbValueConversion.ConvertTo<DateTimeOffset>(reader.GetString()); }
                catch (FormatException error) { throw new JsonException("Expected a database date/time string with an offset.", error); }
            }
            throw new JsonException("Expected a database date/time string with an offset.");
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }
}
