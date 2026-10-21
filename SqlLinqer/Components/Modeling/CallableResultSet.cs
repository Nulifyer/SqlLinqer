using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Reflection;
using SqlLinqer.Extensions.MemberInfoExtensions;

namespace SqlLinqer.Components.Modeling
{
    internal sealed class CallableResultSet
    {
        internal readonly MemberInfo Member;
        internal readonly int Index;
        private readonly Type _listType;
        private readonly bool _array;
        private readonly CallableRowType _row;

        internal CallableResultSet(MemberInfo member, int index)
        {
            Member = member;
            Index = index;
            var type = member.GetFieldOrPropValueType();
            _array = type.IsArray && type.GetArrayRank() == 1;
            var item = _array ? type.GetElementType() : type.IsGenericType && type.GetGenericArguments().Length == 1
                ? type.GetGenericArguments()[0] : null;
            if (item == null || !(_array || type.IsAssignableFrom(typeof(List<>).MakeGenericType(item))))
                throw new InvalidOperationException($"Result set '{member.Name}' must accept a List<T> or be a one-dimensional array.");
            _listType = typeof(List<>).MakeGenericType(item);
            _row = new CallableRowType(item);
        }

        internal object Read(DbDataReader reader)
        {
            var rows = (IList)Activator.CreateInstance(_listType);
            var ordinals = _row.GetOrdinals(reader);
            while (reader.Read()) rows.Add(_row.Read(reader, ordinals));
            if (!_array) return rows;
            var array = Array.CreateInstance(_row.Type, rows.Count);
            rows.CopyTo(array, 0);
            return array;
        }
    }

    internal sealed class CallableRowType
    {
        internal readonly Type Type;
        private readonly bool _scalar;
        private readonly UnprocessedColumnMember[] _members;
        private readonly ConstructorInfo _constructor;
        private readonly int[] _constructorMembers;

        internal CallableRowType(Type type)
        {
            Type = type;
            var raw = Nullable.GetUnderlyingType(type) ?? type;
            _scalar = raw.IsPrimitive || raw.IsEnum || raw == typeof(string) || raw == typeof(decimal)
                || raw == typeof(Guid) || raw == typeof(DateTime) || raw == typeof(DateTimeOffset)
                || raw == typeof(TimeSpan) || raw == typeof(byte[]) || raw == typeof(object);
            if (_scalar) return;
            if (type.IsAbstract || type.IsInterface)
                throw new InvalidOperationException($"Result row type '{type}' must be concrete.");
            _members = ParsedType.GetCached(type).ColumnMembers.ToArray();
            if (_members.Length == 0)
                throw new InvalidOperationException($"Result row type '{type}' has no mapped members.");
            foreach (var member in _members) CallableDefinition.RequireReadable(member.Member);
            var names = _members.Select(x => x.ColumnInfo?.Name ?? x.Member.Name).ToArray();
            if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
                throw new InvalidOperationException($"Result row type '{type}' maps multiple members to the same column.");
            _constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (_constructor == null && !type.IsValueType)
            {
                var constructors = type.GetConstructors().Where(c => c.GetParameters().All(p =>
                    _members.Any(m => string.Equals(m.Member.Name, p.Name, StringComparison.OrdinalIgnoreCase)
                        && m.Member.GetFieldOrPropValueType() == p.ParameterType))).ToArray();
                if (constructors.Length != 1)
                    throw new InvalidOperationException($"Result row type '{type}' needs a parameterless constructor or one public constructor matching its mapped members.");
                _constructor = constructors[0];
            }
            _constructorMembers = (_constructor?.GetParameters() ?? Array.Empty<ParameterInfo>()).Select(p =>
                Array.FindIndex(_members, m => string.Equals(m.Member.Name, p.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
            for (int i = 0; i < _members.Length; i++)
                if (!_constructorMembers.Contains(i)) CallableDefinition.RequireWritable(_members[i].Member);
        }

        internal int[] GetOrdinals(DbDataReader reader)
        {
            if (_scalar)
            {
                if (reader.FieldCount != 1) throw new InvalidOperationException($"Scalar row type '{Type}' requires exactly one column.");
                return new[] { 0 };
            }
            return _members.Select(m =>
            {
                string name = m.ColumnInfo?.Name ?? m.Member.Name;
                var matches = Enumerable.Range(0, reader.FieldCount)
                    .Where(i => string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException($"Result row '{Type}' requires exactly one column named '{name}'.");
                return matches[0];
            }).ToArray();
        }

        internal object Read(DbDataReader reader, int[] ordinals)
        {
            if (_scalar) return CallableValue.Convert(reader.GetValue(0), Type);
            var values = new object[_members.Length];
            for (int i = 0; i < values.Length; i++)
            {
                try { values[i] = CallableValue.Convert(reader.GetValue(ordinals[i]), _members[i].Member.GetFieldOrPropValueType()); }
                catch (Exception error) when (error is InvalidCastException || error is FormatException || error is OverflowException)
                { throw new InvalidCastException($"Cannot load column '{reader.GetName(ordinals[i])}' into '{Type}.{_members[i].Member.Name}'.", error); }
            }
            var instance = _constructor == null ? Activator.CreateInstance(Type)
                : _constructor.Invoke(_constructorMembers.Select(i => values[i]).ToArray());
            for (int i = 0; i < values.Length; i++)
                if (!_constructorMembers.Contains(i)) _members[i].Member.SetFieldOrPropValue(instance, values[i]);
            return instance;
        }
    }

    internal static class CallableValue
    {
        internal static object Convert(object value, Type type)
        {
            if (value == null || value == DBNull.Value)
            {
                if (type.IsValueType && Nullable.GetUnderlyingType(type) == null)
                    throw new InvalidCastException($"Cannot convert SQL NULL to {type}.");
                return null;
            }
            var target = Nullable.GetUnderlyingType(type) ?? type;
            if (target.IsInstanceOfType(value)) return value;
            if (target.IsEnum)
                return value is string text ? Enum.Parse(target, text, true)
                    : Enum.ToObject(target, System.Convert.ChangeType(value, Enum.GetUnderlyingType(target), CultureInfo.InvariantCulture));
            if (target == typeof(Guid)) return Guid.Parse(System.Convert.ToString(value, CultureInfo.InvariantCulture));
            return System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }
    }
}
