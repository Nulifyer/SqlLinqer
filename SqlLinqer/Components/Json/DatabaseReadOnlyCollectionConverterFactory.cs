using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlLinqer.Components.Json
{
    // These framework wrappers require constructor arguments that the framework
    // JSON collection converters cannot supply. Elements retain the SQL contract.
    internal sealed class DatabaseReadOnlyCollectionConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type type) => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>)
                || type.GetGenericTypeDefinition() == typeof(ReadOnlyDictionary<,>));

        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options)
        {
            var converter = type.GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>)
                ? typeof(CollectionConverter<>) : typeof(DictionaryConverter<,>);
            return (JsonConverter)Activator.CreateInstance(converter.MakeGenericType(type.GetGenericArguments()), true);
        }

        private sealed class CollectionConverter<T> : JsonConverter<ReadOnlyCollection<T>>
        {
            public override ReadOnlyCollection<T> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
                => new ReadOnlyCollection<T>(JsonSerializer.Deserialize<List<T>>(ref reader, options));
            public override void Write(Utf8JsonWriter writer, ReadOnlyCollection<T> value, JsonSerializerOptions options)
                => throw new NotSupportedException("SQL collection converters are for database result loading only.");
        }

        private sealed class DictionaryConverter<TKey, TValue> : JsonConverter<ReadOnlyDictionary<TKey, TValue>>
        {
            public override ReadOnlyDictionary<TKey, TValue> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
                => new ReadOnlyDictionary<TKey, TValue>(JsonSerializer.Deserialize<Dictionary<TKey, TValue>>(ref reader, options));
            public override void Write(Utf8JsonWriter writer, ReadOnlyDictionary<TKey, TValue> value, JsonSerializerOptions options)
                => throw new NotSupportedException("SQL collection converters are for database result loading only.");
        }
    }
}
