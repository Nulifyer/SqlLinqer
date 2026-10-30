using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using SqlLinqer.Extensions.MemberInfoExtensions;

namespace SqlLinqer.Components.Modeling
{
    // SQL metadata owns member/constructor selection. JSON attributes never participate.
    internal sealed class SqlModelType
    {
        private static readonly ConcurrentDictionary<Type, SqlModelType> GraphTypes = new ConcurrentDictionary<Type, SqlModelType>();
        private static readonly ConcurrentDictionary<Type, SqlModelType> RowTypes = new ConcurrentDictionary<Type, SqlModelType>();
        internal static SqlModelType Get(Type type, bool relationships = true) => relationships
            ? GraphTypes.GetOrAdd(type, t => new SqlModelType(t, true))
            : RowTypes.GetOrAdd(type, t => new SqlModelType(t, false));
        internal readonly Type Type;
        internal readonly Dictionary<string, MemberInfo> Members = new Dictionary<string, MemberInfo>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<MemberInfo, Action<object, object>> Setters = new Dictionary<MemberInfo, Action<object, object>>();
        internal readonly ParameterInfo[] Parameters;
        internal readonly MemberInfo[] ParameterMembers;
        internal readonly object[] Defaults;
        internal readonly Func<object[], object> Create;

        private readonly ConstructorInfo constructor;

        private SqlModelType(Type type, bool relationships)
        {
            Type = type;
            if (type.IsAbstract || type.IsInterface || type != typeof(object) && SqlLinqer.Connections.DbValueConversion.IsScalarType(type))
                throw new NotSupportedException($"Native SQL result type '{type}' must be a concrete model.");
            var parsed = ParsedType.GetCached(type);
            foreach (var member in relationships ? parsed.ColumnMembers.Concat(parsed.RelationshipMembers.Select(x => x.UnprocessedMember)) : parsed.ColumnMembers)
                Members[member.Member.Name] = member.Member;
            foreach (var member in parsed.ColumnMembers.Where(m => !string.IsNullOrEmpty(m.ColumnInfo?.Name)))
                if (!Members.ContainsKey(member.ColumnInfo.Name)) Members[member.ColumnInfo.Name] = member.Member;
            var ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (ctor == null && !type.IsValueType)
            {
                var candidates = type.GetConstructors().Where(c => c.GetParameters().All(p =>
                    Members.TryGetValue(p.Name, out var m) && m.GetFieldOrPropValueType() == p.ParameterType)).ToArray();
                if (candidates.Length != 1) throw new NotSupportedException($"SQL result type '{type}' needs a parameterless constructor or one public constructor matching its SQL members.");
                ctor = candidates[0];
            }
            constructor = ctor;
            Parameters = ctor?.GetParameters() ?? Array.Empty<ParameterInfo>();
            ParameterMembers = Parameters.Select(p => Members.TryGetValue(p.Name, out var member) ? member : null).ToArray();
            Defaults = Parameters.Select(p => p.HasDefaultValue ? p.DefaultValue
                : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            var args = Expression.Parameter(typeof(object[]), "arguments");
            Expression body = ctor == null ? (Expression)Expression.Default(type) : Expression.New(ctor,
                Parameters.Select((p, i) => Expression.Convert(Expression.ArrayIndex(args, Expression.Constant(i)), p.ParameterType)));
            Create = Expression.Lambda<Func<object[], object>>(Expression.Convert(body, typeof(object)), args).Compile();
            foreach (var member in Members.Values.Distinct())
            {
                if (ParameterMembers.Contains(member)) continue;
                Setters[member] = Setter(type, member);
            }
        }

        internal Func<object[], object> CreateForMembers(MemberInfo[] members)
        {
            var values = Expression.Parameter(typeof(object[]), "values");
            Expression body = constructor == null ? (Expression)Expression.Default(Type) : Expression.New(constructor,
                Parameters.Select((p, i) => Expression.Convert(Expression.ArrayIndex(values,
                    Expression.Constant(Array.FindIndex(members, m => Equals(m, ParameterMembers[i])))), p.ParameterType)));
            return Expression.Lambda<Func<object[], object>>(Expression.Convert(body, typeof(object)), values).Compile();
        }

        private static Action<object, object> Setter(Type type, MemberInfo member)
        {
            // Reflection preserves boxed-struct mutation and readonly backing fields.
            if (type.IsValueType) return (instance, value) => member.SetFieldOrPropValue(instance, value);
            var target = member is PropertyInfo p && p.SetMethod == null ? (MemberInfo)p.GetBackingField() : member;
            if (target == null) throw new NotSupportedException($"SQL member '{member.Name}' has no setter or backing field.");
            if (target is FieldInfo field && field.IsInitOnly)
                return (instance, value) => field.SetValue(instance, value);
            var obj = Expression.Parameter(typeof(object), "instance");
            var val = Expression.Parameter(typeof(object), "value");
            Expression access = target is FieldInfo f ? (Expression)Expression.Field(Expression.Convert(obj, f.DeclaringType), f)
                : Expression.Property(Expression.Convert(obj, target.DeclaringType), (PropertyInfo)target);
            var assign = Expression.Assign(access, Expression.Convert(val, member.GetFieldOrPropValueType()));
            return Expression.Lambda<Action<object, object>>(Expression.Block(assign, Expression.Empty()), obj, val).Compile();
        }
    }

}
