using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Extensions.MemberInfoExtensions;

namespace SqlLinqer.Components.Json
{
    internal sealed class DatabaseModelConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type type)
        {
            if (type.IsPrimitive || type.IsEnum || Nullable.GetUnderlyingType(type) != null
                || typeof(IEnumerable).IsAssignableFrom(type)
                || type.Assembly == typeof(JsonSerializer).Assembly)
                return false;

            return type != typeof(object) && type != typeof(string) && type != typeof(decimal)
                && type != typeof(Guid) && type != typeof(DateTime) && type != typeof(DateTimeOffset)
                && type != typeof(TimeSpan) && type != typeof(Uri) && type != typeof(Version)
                && type.FullName != "System.DateOnly" && type.FullName != "System.TimeOnly"
                && type.FullName != "System.Half" && type.FullName != "System.Int128"
                && type.FullName != "System.UInt128";
        }

        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options)
            => (JsonConverter)Activator.CreateInstance(typeof(DatabaseModelConverter<>).MakeGenericType(type), true);

        private sealed class DatabaseModelConverter<T> : JsonConverter<T>
        {
            private readonly Dictionary<string, MemberInfo> _members;
            private readonly ConstructorInfo _constructor;
            private readonly ParameterInfo[] _parameters;
            private readonly Dictionary<string, int> _parameterPositions;

            public DatabaseModelConverter()
            {
                var type = typeof(T);
                if (type.IsAbstract || type.IsInterface)
                    throw new NotSupportedException($"SQL result type '{type}' must be concrete.");

                var model = ParsedType.GetCached(type);
                _members = new Dictionary<string, MemberInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var member in model.ColumnMembers.Concat(model.RelationshipMembers.Select(x => x.UnprocessedMember)))
                    _members[member.Member.Name] = member.Member;

                // Constructor selection follows SQL model shape, never JsonConstructor.
                _constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (_constructor == null && !type.IsValueType)
                {
                    var constructors = type.GetConstructors().Where(IsModelConstructor).ToArray();
                    if (constructors.Length != 1)
                        throw new NotSupportedException($"SQL result type '{type}' needs a parameterless constructor or one public constructor matching its model members.");
                    _constructor = constructors[0];
                }
                _parameters = _constructor?.GetParameters() ?? Array.Empty<ParameterInfo>();
                _parameterPositions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _parameters.Length; i++)
                    _parameterPositions.Add(_parameters[i].Name, i);
            }

            private bool IsModelConstructor(ConstructorInfo constructor)
                => constructor.GetParameters().All(parameter =>
                    _members.TryGetValue(parameter.Name, out var member)
                    && member.GetFieldOrPropValueType() == parameter.ParameterType);

            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                    throw new JsonException($"Expected an SQL JSON object for '{typeof(T)}'.");

                object instance = _parameters.Length == 0
                    ? (_constructor == null ? (object)default(T) : _constructor.Invoke(null))
                    : null;
                object[] arguments = null;
                List<KeyValuePair<MemberInfo, object>> pending = null;
                if (_parameters.Length > 0)
                {
                    arguments = new object[_parameters.Length];
                    for (int i = 0; i < arguments.Length; i++)
                        arguments[i] = _parameters[i].HasDefaultValue ? _parameters[i].DefaultValue
                            : _parameters[i].ParameterType.IsValueType ? Activator.CreateInstance(_parameters[i].ParameterType) : null;
                    pending = new List<KeyValuePair<MemberInfo, object>>();
                }

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        if (arguments != null)
                        {
                            instance = _constructor.Invoke(arguments);
                            foreach (var pendingValue in pending)
                                pendingValue.Key.SetFieldOrPropValue(instance, pendingValue.Value);
                        }
                        return (T)instance;
                    }
                    if (reader.TokenType != JsonTokenType.PropertyName)
                        throw new JsonException("Expected an SQL JSON member name.");

                    string name = reader.GetString();
                    if (!reader.Read())
                        throw new JsonException("Expected an SQL JSON member value.");
                    if (!_members.TryGetValue(name, out var member))
                    {
                        reader.Skip();
                        continue;
                    }

                    object value = JsonSerializer.Deserialize(ref reader, member.GetFieldOrPropValueType(), options);
                    if (arguments == null)
                        member.SetFieldOrPropValue(instance, value);
                    else if (_parameterPositions.TryGetValue(name, out int position))
                        arguments[position] = value;
                    else
                        pending.Add(new KeyValuePair<MemberInfo, object>(member, value));
                }
                throw new JsonException("Incomplete SQL JSON object.");
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
                => throw new NotSupportedException("SQL model JSON contracts are for database result loading only.");
        }
    }
}
