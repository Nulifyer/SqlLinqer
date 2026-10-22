using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SqlLinqer.Components.Json
{
    // SQL JSON is a library-owned wire format, independent of API serialization.
    internal sealed class ModelConfigResolver : IJsonTypeInfoResolver
    {
        private readonly DefaultJsonTypeInfoResolver _frameworkResolver = new DefaultJsonTypeInfoResolver();

        internal static JsonSerializerOptions CreateOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                TypeInfoResolver = new ModelConfigResolver(),
                Converters =
                {
                    new DatabaseStringConverter(),
                    new DatabaseReadOnlyCollectionConverterFactory(),
                    new DatabaseModelConverterFactory(),
                    new DatabaseEnumConverterFactory(),
                    new DatabaseFloatingPointConverterFactory(),
                    new DatabaseBooleanConverter(),
                    new DatabaseDateTimeConverter(),
                    new DatabaseDateTimeOffsetConverter(),
                },
            };
        }

        public JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            // Framework collections cannot carry application-defined attributes.
            // Their element/value contracts still resolve through this SQL-only resolver.
            if (typeof(IEnumerable).IsAssignableFrom(type)
                && (type.Assembly == typeof(List<>).Assembly
                    || type.Assembly == typeof(HashSet<>).Assembly
                    || (type.Namespace == "System.Collections.Immutable"
                        && type.Assembly.GetName().Name == "System.Collections.Immutable")))
                return _frameworkResolver.GetTypeInfo(type, options);

            // This factory skips JsonConverter attributes. Model converters also
            // bypass application property and constructor serialization attributes.
            var info = JsonTypeInfo.CreateJsonTypeInfo(type, options);
            if ((info.Kind == JsonTypeInfoKind.Enumerable || info.Kind == JsonTypeInfoKind.Dictionary)
                && !type.IsArray && !type.IsAbstract && !type.IsInterface)
            {
                var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (constructor != null)
                    info.CreateObject = () => constructor.Invoke(null);
            }
            return info;
        }
    }
}
