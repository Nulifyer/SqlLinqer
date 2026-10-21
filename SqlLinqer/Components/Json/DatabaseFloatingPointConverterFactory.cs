using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlLinqer.Components.Json
{
    // A finite JSON number can still overflow Single or Double into infinity.
    internal sealed class DatabaseFloatingPointConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type type) => type == typeof(float) || type == typeof(double);

        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options)
            => (JsonConverter)Activator.CreateInstance(typeof(FloatingPointConverter<>).MakeGenericType(type), true);

        private sealed class FloatingPointConverter<T> : JsonConverter<T>
        {
            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                try
                {
                    if (reader.TokenType != JsonTokenType.Number && reader.TokenType != JsonTokenType.String)
                        throw new JsonException("Expected a database number.");
                    if (typeof(T) == typeof(float))
                    {
                        float value = reader.TokenType == JsonTokenType.Number ? reader.GetSingle()
                            : float.Parse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                        if (float.IsNaN(value) || float.IsInfinity(value)) throw new OverflowException();
                        return (T)(object)value;
                    }
                    double number = reader.TokenType == JsonTokenType.Number ? reader.GetDouble()
                        : double.Parse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (double.IsNaN(number) || double.IsInfinity(number)) throw new OverflowException();
                    return (T)(object)number;
                }
                catch (Exception error) when (error is FormatException || error is OverflowException)
                { throw new JsonException("Expected a finite database number within the target range.", error); }
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
                => throw new NotSupportedException("SQL number converters are for database result loading only.");
        }
    }
}
