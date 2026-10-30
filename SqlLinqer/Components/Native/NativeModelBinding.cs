using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Data.Common;
using System.IO;
using SqlLinqer.Connections;
using SqlLinqer.Components.Modeling;
using SqlLinqer.Extensions.MemberInfoExtensions;
using SqlLinqer.Extensions.TypeExtensions;

namespace SqlLinqer.Components.Native
{
    internal sealed class NativeModelBinding
    {
        internal readonly SqlModelType Model;
        internal readonly MemberInfo[] Members;
        internal readonly Type[] Types;
        private readonly int[] parameters;
        private readonly Action<object, object>[] setters;

        internal NativeModelBinding(Type type, IEnumerable<string> aliases)
        {
            Model = SqlModelType.Get(type);
            Members = aliases.Select(alias => Model.Members.TryGetValue(alias, out var member) ? member : null).ToArray();
            Types = Members.Select(m => m?.GetFieldOrPropValueType()).ToArray();
            parameters = Model.ParameterMembers.Select(parameter => parameter == null ? -1
                : Array.FindLastIndex(Members, member => Equals(member, parameter))).ToArray();
            setters = Members.Select(m => m != null && Model.Setters.TryGetValue(m, out var setter) ? setter : null).ToArray();
        }

        internal object Construct(object[] values, object[] arguments = null)
        {
            object[] args = null;
            if (parameters.Length != 0)
            {
                args = arguments ?? new object[Model.Defaults.Length];
                Array.Copy(Model.Defaults, args, args.Length);
                for (int i = 0; i < parameters.Length; i++) if (parameters[i] >= 0) args[i] = values[parameters[i]];
            }
            object instance = Model.Create(args);
            for (int i = 0; i < values.Length; i++) setters[i]?.Invoke(instance, values[i]);
            return instance;
        }

        internal object ReadFlat(DbDataReader reader, NativeResultScalar[] scalars, bool[] utc, object[] values, object[] arguments)
        {
            object instance = Model.Parameters.Length == 0 ? Model.Create(null) : null;
            for (int i = 0; i < scalars.Length; i++)
            {
                if (Types[i] == null) continue;
                int ordinal = scalars[i].Ordinal;
                object value = NativeValueReader.Read(reader, ordinal, utc[ordinal]);
                value = DbValueConversion.ConvertTo(value, Types[i]);
                if (instance != null) setters[i]?.Invoke(instance, value);
                else values[i] = value;
            }
            return instance ?? Construct(values, arguments);
        }
    }

    internal sealed class NativeCollectionBinding
    {
        private static readonly ConcurrentDictionary<Type, NativeCollectionBinding> Cache = new ConcurrentDictionary<Type, NativeCollectionBinding>();
        internal static NativeCollectionBinding Get(Type type) => Cache.GetOrAdd(type, t => new NativeCollectionBinding(t));
        internal readonly Type ElementType;
        private readonly Type collectionType;
        private readonly Func<object> create;
        private readonly Func<int, object> createList;
        private readonly Action<object, object> add;
        private readonly ConstructorInfo enumerableConstructor;
        private readonly Func<object, object> rangeFactory;
        private readonly Type listType;

        internal NativeCollectionBinding(Type type)
        {
            collectionType = type;
            ElementType = type.GetItemTypeFromIEnumerable() ?? throw new NotSupportedException($"SQL collection '{type}' has no unambiguous element type.");
            listType = typeof(List<>).MakeGenericType(ElementType);
            if (type.IsArray) return;
            if (type.IsAssignableFrom(listType)) { createList = ListFactory(listType); add = Adder(listType); return; }
            var ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            var coll = typeof(ICollection<>).MakeGenericType(ElementType);
            if (ctor != null && coll.IsAssignableFrom(type) && type.Namespace != "System.Collections.Immutable") { create = Factory(type); add = Adder(coll); return; }
            enumerableConstructor = type.GetConstructors().Where(c => c.GetParameters().Length == 1
                && c.GetParameters()[0].ParameterType != typeof(object)
                && c.GetParameters()[0].ParameterType.IsAssignableFrom(listType))
                .OrderByDescending(c => c.GetParameters()[0].ParameterType == typeof(IEnumerable<>).MakeGenericType(ElementType)).FirstOrDefault();
            if (enumerableConstructor != null) { createList = ListFactory(listType); return; }
            // Immutable collections expose CreateRange on their non-generic
            // companion type. Reflect that public contract without a dependency.
            var companion = type.Assembly.GetType(type.FullName.Split('`')[0]);
            var range = companion?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "CreateRange"
                && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType.IsGenericType
                && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (range == null) throw new NotSupportedException($"SQL collection '{type}' cannot be constructed from its selected elements.");
            var input = Expression.Parameter(typeof(object));
            rangeFactory = Expression.Lambda<Func<object, object>>(Expression.Convert(Expression.Call(range.MakeGenericMethod(ElementType),
                Expression.Convert(input, typeof(IEnumerable<>).MakeGenericType(ElementType))), typeof(object)), input).Compile();
            createList = ListFactory(listType);
        }
        private static Func<int, object> ListFactory(Type type)
        {
            var capacity = Expression.Parameter(typeof(int), "capacity");
            return Expression.Lambda<Func<int, object>>(Expression.Convert(
                Expression.New(type.GetConstructor(new[] { typeof(int) }), capacity), typeof(object)), capacity).Compile();
        }
        private static Func<object> Factory(Type type) => Expression.Lambda<Func<object>>(Expression.Convert(
            Expression.New(type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)), typeof(object))).Compile();
        private Action<object, object> Adder(Type type)
        {
            var obj = Expression.Parameter(typeof(object)); var value = Expression.Parameter(typeof(object));
            return Expression.Lambda<Action<object, object>>(Expression.Call(Expression.Convert(obj, type), type.GetMethod("Add"),
                Expression.Convert(value, ElementType)), obj, value).Compile();
        }
        internal object Build(IList<object> values)
        {
            if (collectionType.IsArray)
            {
                var array = Array.CreateInstance(ElementType, values.Count);
                for (int i = 0; i < values.Count; i++) array.SetValue(values[i], i);
                return array;
            }
            if (enumerableConstructor != null || rangeFactory != null)
            {
                var list = (IList)createList(values.Count);
                for (int i = 0; i < values.Count; i++) list.Add(values[i]);
                return rangeFactory != null ? rangeFactory(list) : enumerableConstructor.Invoke(new object[] { list });
            }
            var instance = createList != null ? createList(values.Count) : create();
            for (int i = 0; i < values.Count; i++) add(instance, values[i]);
            return instance;
        }
    }

    internal static class NativeValueReader
    {
        internal static object Read(DbDataReader reader, int ordinal, bool utc = false)
        {
            object value = reader.GetValue(ordinal);
            if (utc && value is DateTime date) return DateTime.SpecifyKind(date, DateTimeKind.Utc);
            if (value is byte[] bytes) return (byte[])bytes.Clone();
            if (value is Stream stream)
                using (stream) using (var copy = new MemoryStream()) { stream.CopyTo(copy); return copy.ToArray(); }
            return value;
        }
    }
}
