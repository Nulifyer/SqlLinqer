using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlLinqer.Components.Json
{
    // API enum names and converters cannot change database enum loading.
    internal sealed class DatabaseEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type type) => type.IsEnum;

        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options)
            => (JsonConverter)Activator.CreateInstance(typeof(DatabaseEnumConverter<>).MakeGenericType(type), true);

        private sealed class DatabaseEnumConverter<T> : JsonConverter<T> where T : struct, Enum
        {
            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                try
                {
                    if (reader.TokenType == JsonTokenType.String)
                        return (T)Enum.Parse(typeof(T), reader.GetString(), true);
                    if (reader.TokenType == JsonTokenType.Number)
                    {
                        var underlying = Enum.GetUnderlyingType(typeof(T));
                        object number = underlying == typeof(ulong) ? (object)reader.GetUInt64() : reader.GetInt64();
                        return (T)Enum.ToObject(typeof(T), Convert.ChangeType(number, underlying, CultureInfo.InvariantCulture));
                    }
                }
                catch (Exception error) when (error is ArgumentException || error is OverflowException || error is FormatException)
                {
                    throw new JsonException($"Invalid database value for enum '{typeof(T)}'.", error);
                }
                throw new JsonException($"Expected a database enum name or number for '{typeof(T)}'.");
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
                => writer.WriteStringValue(value.ToString());
        }
    }
}
