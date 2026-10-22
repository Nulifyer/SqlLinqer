using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using SqlLinqer.Extensions.MemberInfoExtensions;

namespace SqlLinqer.Components.Modeling
{
    // Shared SQL row contract for procedure rows and connector model lists.
    internal sealed class TabularRowType
    {
        private static readonly ConcurrentDictionary<Type, TabularRowType> Types = new ConcurrentDictionary<Type, TabularRowType>();
        internal static TabularRowType GetCached(Type type) => Types.GetOrAdd(type, t => new TabularRowType(t));

        internal readonly Type Type;
        private readonly bool _scalar;
        private readonly UnprocessedColumnMember[] _members;
        private readonly ConstructorInfo _constructor;
        private readonly int[] _constructorMembers;
        private readonly bool[] _fromConstructor;
        private readonly Type[] _memberTypes;
        private readonly Func<object[], object> _create;

        internal TabularRowType(Type type)
        {
            Type = type;
            _scalar = SqlLinqer.Connections.DbValueConversion.IsScalarType(type);
            if (_scalar) return;
            if (type.IsAbstract || type.IsInterface)
                throw new InvalidOperationException($"Result row type '{type}' must be concrete.");
            _members = ParsedType.GetCached(type).ColumnMembers.ToArray();
            if (_members.Length == 0)
                throw new InvalidOperationException($"Result row type '{type}' has no mapped members.");
            foreach (var member in _members) member.Member.RequireReadable();
            _memberTypes = _members.Select(x => x.Member.GetFieldOrPropValueType()).ToArray();
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
            var arguments = Expression.Parameter(typeof(object[]), "values");
            Expression create = _constructor == null ? (Expression)Expression.Default(type)
                : Expression.New(_constructor, _constructorMembers.Select(i =>
                    Expression.Convert(Expression.ArrayIndex(arguments, Expression.Constant(i)), _memberTypes[i])));
            _create = Expression.Lambda<Func<object[], object>>(Expression.Convert(create, typeof(object)), arguments).Compile();
            _fromConstructor = new bool[_members.Length];
            foreach (int member in _constructorMembers) _fromConstructor[member] = true;
            for (int i = 0; i < _members.Length; i++)
                if (!_fromConstructor[i]) _members[i].Member.RequireWritable();
        }

        internal int[] GetOrdinals(DbDataReader reader)
        {
            if (_scalar)
            {
                if (reader.FieldCount != 1) throw new InvalidOperationException($"Scalar row type '{Type}' requires exactly one column.");
                return new[] { 0 };
            }
            var names = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string name = reader.GetName(i);
                names[name] = names.ContainsKey(name) ? -1 : i;
            }
            var ordinals = new int[_members.Length];
            var used = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < _members.Length; i++)
            {
                var member = _members[i];
                string sqlName = member.ColumnInfo?.Name ?? member.Member.Name;
                bool sql = names.TryGetValue(sqlName, out int sqlOrdinal);
                bool clr = names.TryGetValue(member.Member.Name, out int clrOrdinal);
                if ((!sql && !clr) || (sql && sqlOrdinal < 0) || (clr && clrOrdinal < 0)
                    || (sql && clr && sqlOrdinal != clrOrdinal))
                    throw new InvalidOperationException($"Result row '{Type}' requires exactly one unambiguous column named '{sqlName}' or '{member.Member.Name}'.");
                int ordinal = sql ? sqlOrdinal : clrOrdinal;
                if (!used.Add(ordinal))
                    throw new InvalidOperationException($"Result row '{Type}' maps multiple members to column '{reader.GetName(ordinal)}'.");
                ordinals[i] = ordinal;
            }
            return ordinals;
        }

        internal object[] CreateBuffer() => _scalar ? null : new object[_members.Length];

        internal object Read(DbDataReader reader, int[] ordinals, object[] rowValues = null, object[] mappedValues = null)
        {
            if (_scalar) return SqlLinqer.Connections.DbValueConversion.ConvertTo(rowValues == null ? reader.GetValue(0) : rowValues[0], Type);
            var values = mappedValues ?? CreateBuffer();
            for (int i = 0; i < values.Length; i++)
            {
                try { values[i] = SqlLinqer.Connections.DbValueConversion.ConvertTo(rowValues == null ? reader.GetValue(ordinals[i]) : rowValues[ordinals[i]], _memberTypes[i]); }
                catch (Exception error) when (error is InvalidCastException || error is FormatException || error is OverflowException)
                { throw new InvalidCastException($"Cannot load column '{reader.GetName(ordinals[i])}' into '{Type}.{_members[i].Member.Name}'.", error); }
            }
            var instance = _create(values);
            for (int i = 0; i < values.Length; i++)
                if (!_fromConstructor[i]) _members[i].Member.SetFieldOrPropValue(instance, values[i]);
            return instance;
        }
    }

}
