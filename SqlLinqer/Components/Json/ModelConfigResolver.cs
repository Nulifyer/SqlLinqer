using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SqlLinqer.Extensions.MemberInfoExtensions;

namespace SqlLinqer.Components.Json
{
    internal sealed class ModelConfigResolver : DefaultJsonTypeInfoResolver
    {
        internal static JsonSerializerOptions CreateOptions()
        {
            return new JsonSerializerOptions
            {
                IncludeFields = true,
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
                TypeInfoResolver = new ModelConfigResolver(),
                Converters =
                {
                    new JsonStringEnumConverter(),
                    new DatabaseBooleanConverter(),
                    new DatabaseDateTimeConverter(),
                    new DatabaseDateTimeOffsetConverter(),
                },
            };
        }

        public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            var info = base.GetTypeInfo(type, options);
            if (info.Kind != JsonTypeInfoKind.Object)
                return info;

            // Models may use private default constructors rather than public ones.
            if (type.IsClass && !type.IsAbstract)
            {
                var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (!constructors.Any(x => x.IsDefined(typeof(JsonConstructorAttribute), false)))
                {
                    var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (constructor != null)
                        info.CreateObject = () => constructor.Invoke(null);
                }
            }

            foreach (var property in info.Properties)
            {
                if (!(property.AttributeProvider is PropertyInfo member))
                    continue;
                var ignoreCondition = member.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition;
                if (ignoreCondition == JsonIgnoreCondition.Always || ignoreCondition == JsonIgnoreCondition.WhenReading)
                    continue;

                if (property.Set == null && (member.SetMethod != null || member.GetBackingField() != null))
                    property.Set = (target, value) => member.SetFieldOrPropValue(target, value);
            }

            return info;
        }
    }
}
