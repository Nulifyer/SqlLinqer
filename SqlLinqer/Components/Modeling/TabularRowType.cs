using System;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using SqlLinqer.Extensions.MemberInfoExtensions;

namespace SqlLinqer.Components.Modeling
{
    // Shared SQL row contract for callable result sets and connector model lists.
    internal sealed class TabularRowType
    {
        internal readonly Type Type;
        private readonly bool _scalar;
        private readonly UnprocessedColumnMember[] _members;
        private readonly ConstructorInfo _constructor;
        private readonly int[] _constructorMembers;

        internal TabularRowType(Type type)
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
            foreach (var member in _members) member.Member.RequireReadable();
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
                if (!_constructorMembers.Contains(i)) _members[i].Member.RequireWritable();
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

        internal object Read(DbDataReader reader, int[] ordinals, object[] rowValues = null)
        {
            if (_scalar) return TabularValue.Convert(rowValues == null ? reader.GetValue(0) : rowValues[0], Type);
            var values = new object[_members.Length];
            for (int i = 0; i < values.Length; i++)
            {
                try { values[i] = TabularValue.Convert(rowValues == null ? reader.GetValue(ordinals[i]) : rowValues[ordinals[i]], _members[i].Member.GetFieldOrPropValueType()); }
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

}
